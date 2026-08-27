using HdrDoctor.Core.Model;

namespace HdrDoctor.Core.Checks;

/// <summary>
/// Compares HDR's own mod folders against the file list published with the release.
/// </summary>
/// <remarks>
/// A port of the launcher's verify (<c>hdr-launcher-react verify.ts:46-215</c>),
/// including its ignore lists.
///
/// Three separate things are checked: every file the release should contain is
/// present, every one of them has the right contents, and nothing extra has been
/// added to the HDR folders.
/// </remarks>
public sealed class FileVerificationCheck : ICheck
{
    public const string CheckId = "file-verification";

    public const string Name = "File verification";

    private static readonly string[] AlwaysIgnored =
    [
        "changelog.toml",
        "hdr-launcher.nro",
        "ui_stage_db.prcxml",
    ];

    /// <summary>
    /// Music files, which can be modified by music/sound mods. Ignored
    /// only when the user asks for it. From <c>verify.ts:20-27</c>.
    /// </summary>
    private static readonly string[] MusicFiles =
    [
        "bgm_property.bin",
        "ui_bgm_db.prc",
        "ui_series_db.prc",
        "msg_bgm.msbt",
        "msg_title.mbst",
    ];

    /// <summary>
    /// Restrict concurrent hashes to avoid thrashing a slow SD card.
    /// </summary>
    private const int MaxConcurrentHashes = 4;

    public string Id => CheckId;

    public CheckCategory Category => CheckCategory.FileVerification;

    public PlatformScope Scope => PlatformScope.Any;

    public string DisplayName => Name;

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct)
    {
        var findings = new List<Finding>();

        if (ctx.Manifest is null)
        {
            return findings;
        }

        await VerifyAgainstManifestAsync(ctx, ctx.Manifest, findings, ct).ConfigureAwait(false);
        await FindUnexpectedFilesAsync(ctx, ctx.Manifest, findings, ct).ConfigureAwait(false);

        return findings;
    }

    private async Task VerifyAgainstManifestAsync(
        ScanContext ctx,
        Services.ReleaseManifest manifest,
        List<Finding> findings,
        CancellationToken ct)
    {
        var paths = manifest.Paths.Where(p => !ShouldIgnore(p, ctx.IgnoreMusicFiles)).ToList();

        var missing = new List<string>();
        var wrong = new List<string>();
        var unreadable = new List<string>();
        var done = 0;

        using var limiter = new SemaphoreSlim(MaxConcurrentHashes);
        var gate = new object();

        var tasks = paths.Select(async path =>
        {
            await limiter.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!await ctx.Source.FileExistsAsync(path, ct).ConfigureAwait(false))
                {
                    lock (gate)
                    {
                        missing.Add(path);
                    }

                    return;
                }

                manifest.TryGetHash(path, out var expected);
                var actual = await ctx.Source.ComputeMd5Async(path, ct).ConfigureAwait(false);

                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    lock (gate)
                    {
                        wrong.Add(path);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                lock (gate)
                {
                    unreadable.Add($"{path}: {e.Message}");
                }
            }
            finally
            {
                var current = Interlocked.Increment(ref done);
                if (current % 25 == 0)
                {
                    ctx.Report("Verifying files", $"{current} of {paths.Count}", (double)current / paths.Count);
                }

                limiter.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (missing.Count > 0)
        {
            findings.Add(new Finding(
                Id,
                Severity.Critical,
                $"{missing.Count} file{Plural(missing.Count)} missing from the HDR folders",
                $"Compared against the published file list for {manifest.Tag}.",
                "These files are part of the release but are not on your SD card. Depending on which ones, this ranges "
                + "from a cosmetic glitch to a crash on boot. Reinstalling the full package is the reliable fix. A "
                + "partial copy usually means an interrupted download or extraction.",
                Truncate([.. missing.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)])));
        }

        if (wrong.Count > 0)
        {
            findings.Add(new Finding(
                Id,
                Severity.Error,
                $"{wrong.Count} file{Plural(wrong.Count)} do not match the official release",
                $"Compared against the published file list for {manifest.Tag}.",
                "These files exist but their contents differ from the official release. That is a corrupted "
                + "download, a mod that has overwritten part of HDR, or a mixed install. Regardless, this is a non-standard install and "
                + "will cause issues, including potential crashes and/or desyncs.",
                Truncate([.. wrong.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)])));
        }

        if (unreadable.Count > 0)
        {
            findings.Add(new Finding(
                Id,
                Severity.Warning,
                $"{unreadable.Count} file{Plural(unreadable.Count)} could not be read",
                "These files exist but could not be opened, so they were not verified.",
                "This is usually a permissions problem or a failing SD card. If it is a card, copy your data off it "
                + "before doing anything else.",
                Truncate(unreadable)));
        }

        if (missing.Count == 0 && wrong.Count == 0 && unreadable.Count == 0)
        {
            findings.Add(new Finding(
                Id,
                Severity.Ok,
                $"All {paths.Count} files match the official {manifest.Tag} release",
                string.Empty,
                string.Empty,
                []));
        }
    }

    private async Task FindUnexpectedFilesAsync(
        ScanContext ctx,
        Services.ReleaseManifest manifest,
        List<Finding> findings,
        CancellationToken ct)
    {
        var unexpected = new List<string>();

        foreach (var folder in HdrPaths.HdrOwnedFolders)
        {
            ctx.Report("Checking for unexpected files", folder);

            var entries = await ctx.Source.ListAsync(folder, recursive: true, ct).ConfigureAwait(false);

            foreach (var entry in entries.Where(e => !e.IsDirectory))
            {
                if (manifest.Contains(entry.RelativePath) ||
                    ShouldIgnore(entry.RelativePath, ctx.IgnoreMusicFiles))
                {
                    continue;
                }

                unexpected.Add(entry.RelativePath);
            }
        }

        if (unexpected.Count == 0)
        {
            return;
        }

        findings.Add(new Finding(
            Id,
            Severity.Warning,
            $"{unexpected.Count} unexpected file{Plural(unexpected.Count)} inside the HDR folders",
            "These files are not part of the official release but sit inside HDR's own mod folders.",
            "Anything inside ultimate/mods/hdr, hdr-assets or hdr-stages is loaded exactly as if it were part of HDR. "
            + "Files that are not part of the release therefore change how the game plays without appearing as a "
            + "separate mod, and will probably desync you online. Unless you put them there on purpose, they should be removed.",
            Truncate([.. unexpected.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)])));
    }

    private static bool ShouldIgnore(string path, bool ignoreMusic)
    {
        if (AlwaysIgnored.Any(name => path.EndsWith(name, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return ignoreMusic && MusicFiles.Any(name => path.EndsWith(name, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> Truncate(IReadOnlyList<string> paths, int limit = 50) =>
        paths.Count <= limit
            ? paths
            : [.. paths.Take(limit), $"... and {paths.Count - limit} more"];

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
