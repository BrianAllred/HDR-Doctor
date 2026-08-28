using System.Diagnostics;
using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Profiles;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Core.Services;

/// <summary>Runs a full scan of one installation and produces the report.</summary>
public sealed class ScanService(ReleaseManifestClient manifests, IAppPaths paths)
{
    /// <summary>
    /// Every check a scan runs, in report order. Registration order decides the order
    /// findings are produced in; the UI regroups by category afterwards.
    /// </summary>
    /// <remarks>
    /// <see cref="FileVerificationCheck"/> is deliberately absent: it hashes every file
    /// in the install, so it is a step the user starts, not something a scan does on
    /// their behalf. <see cref="VerifyFilesAsync"/> runs it.
    /// </remarks>
    private IReadOnlyList<ICheck> BuildChecks(InstallProfile profile) =>
    [
        new SdCardFilesystemCheck(
            profile.Kind == ProfileKind.Local && !profile.IsFtpMount ? profile.Path : null),
        new InstallLayoutCheck(),
        new SkylinePluginsCheck(),
        new HidModuleCheck(),
        new StageAltsCheck(),
        new ModFolderCheck(),
        new ThirdPartyModCheck(),
        new EmulatorConfigCheck(),
        new LauncherConfigCheck(paths),
        new SkylineLogCheck(paths),
        new CrashReportCheck(),
    ];

    public async Task<ScanResult> ScanAsync(
        InstallProfile profile,
        IInstallSource source,
        bool ignoreMusicFiles,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var skipped = new List<SkippedCheck>();

        progress?.Report(new ScanProgress("Reading version information", null, null));
        var versions = await HdrVersionReader.ReadAsync(source, ct).ConfigureAwait(false);

        var (manifest, manifestReason) = await TryGetManifestAsync(versions, progress, ct).ConfigureAwait(false);

        var ctx = new ScanContext
        {
            Source = source,
            Platform = profile.Platform,
            Versions = versions,
            Manifest = manifest,
            ManifestUnavailableReason = manifestReason,
            Emulator = profile.Emulator,
            IgnoreMusicFiles = ignoreMusicFiles,
            Progress = progress,
        };

        var findings = await new CheckRunner(BuildChecks(profile))
            .RunAsync(ctx, skipped, ct)
            .ConfigureAwait(false);

        skipped.Add(DescribeUnverified(manifestReason));

        stopwatch.Stop();

        return new ScanResult(
            [.. findings.OrderBy(f => f.Severity)],
            skipped,
            BuildSummary(profile, source, versions, manifest),
            DateTimeOffset.Now,
            stopwatch.Elapsed);
    }

    /// <summary>
    /// Runs the file verification step on its own. Nothing calls this as part of a
    /// scan — the user asks for it.
    /// </summary>
    /// <remarks>
    /// It deliberately re-reads the versions and re-fetches the manifest instead of
    /// taking them from the scan that came before it: the step can be run minutes or
    /// hours later, against an install a reinstall may have changed underneath, and
    /// verifying against a stale file list is worse than not verifying at all.
    /// </remarks>
    public async Task<VerificationResult> VerifyFilesAsync(
        InstallProfile profile,
        IInstallSource source,
        bool ignoreMusicFiles,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        progress?.Report(new ScanProgress("Reading version information", null, null));
        var versions = await HdrVersionReader.ReadAsync(source, ct).ConfigureAwait(false);

        var (manifest, manifestReason) = await TryGetManifestAsync(versions, progress, ct).ConfigureAwait(false);

        if (manifest is null)
        {
            stopwatch.Stop();
            return new VerificationResult(
                FileVerificationCheck.CheckId,
                [],
                DescribeUnverified(manifestReason),
                stopwatch.Elapsed);
        }

        var ctx = new ScanContext
        {
            Source = source,
            Platform = profile.Platform,
            Versions = versions,
            Manifest = manifest,
            Emulator = profile.Emulator,
            IgnoreMusicFiles = ignoreMusicFiles,
            Progress = progress,
        };

        // Through the runner rather than calling the check directly, so a verification
        // that blows up halfway becomes a finding like it would in a scan.
        var skipped = new List<SkippedCheck>();
        var findings = await new CheckRunner([new FileVerificationCheck()])
            .RunAsync(ctx, skipped, ct)
            .ConfigureAwait(false);

        stopwatch.Stop();

        return new VerificationResult(
            FileVerificationCheck.CheckId,
            [.. findings.OrderBy(f => f.Severity)],
            skipped.FirstOrDefault(),
            stopwatch.Elapsed);
    }

    /// <summary>
    /// Why the install's files have not been checked against the release — either
    /// there is nothing to compare against, or nobody has asked yet.
    /// </summary>
    /// <remarks>
    /// Every scan carries one of these, because a report that simply omits the file
    /// verification reads as an install whose files were checked and found correct.
    /// </remarks>
    private static SkippedCheck DescribeUnverified(string? manifestReason) => new(
        FileVerificationCheck.CheckId,
        FileVerificationCheck.Name,
        manifestReason
        ?? "Not run. Checking every file against the release's published hashes reads the whole "
        + "install, so it is a separate step you start yourself.");

    /// <summary>
    /// Fetches the release manifest, or explains why it could not. A failure here
    /// downgrades the scan to structural checks rather than failing it — an offline
    /// user still gets the plugin, stage-alts and config findings, which is most of
    /// the value.
    /// </summary>
    private async Task<(ReleaseManifest?, string?)> TryGetManifestAsync(
        HdrVersionInfo versions,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        if (versions.PluginVersion is null)
        {
            return (null, "HDR does not appear to be installed, so there is no version to compare against.");
        }

        if (versions.Channel == ReleaseChannel.Unknown || versions.ReleaseTag is null)
        {
            return (null,
                $"Version '{versions.PluginVersion}' is not a public beta or pre-release build, so there is no "
                + "published file list to compare against. This is normal for private and developer builds.");
        }

        progress?.Report(new ScanProgress("Downloading file list", versions.ReleaseTag, null));

        try
        {
            var manifest = await manifests
                .GetManifestAsync(versions.Channel, versions.ReleaseTag, ct)
                .ConfigureAwait(false);

            return (manifest, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return (null, $"Could not download the file list for {versions.ReleaseTag}: {e.Message}");
        }
    }

    private static EnvironmentSummary BuildSummary(
        InstallProfile profile,
        IInstallSource source,
        HdrVersionInfo versions,
        ReleaseManifest? manifest) => new()
    {
        SourceDescription = source.RootDescription,
        SourceKind = profile.Kind == ProfileKind.Ftp ? "Switch over FTP (read-only)"
            : profile.IsFtpMount ? "FTP mount (read-only)"
            : "Local folder",
        Platform = profile.Platform,
        HdrVersion = versions.PluginVersion,
        AssetsVersion = versions.AssetsVersion,
        Channel = versions.Channel,
        MainModFolder = versions.MainModFolder,
        EmulatorName = profile.EmulatorName,
        EmulatorConfigPath = profile.EmulatorConfigPath,
        FixesAvailable = profile.SupportsFixes,
        FileListAvailable = manifest is not null,
    };
}
