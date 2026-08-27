using System.Text.Json;
using System.Text.Json.Serialization;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Core.Checks;

/// <summary>
/// Checks the desktop launcher's own configuration.
/// </summary>
/// <remarks>
/// The launcher keeps two paths: the emulator executable it launches, and the SD
/// folder it installs into. When pointing at an incorrect emulator
/// data directory, for example, the launcher happily reports a successful update while the
/// emulator the user actually plays on never changes.
///
/// Schema and locations come from <c>hdr-launcher-react src/main/config.ts</c>.
/// </remarks>
public sealed class LauncherConfigCheck(IAppPaths paths) : ICheck
{
    public string Id => "launcher-config";

    public CheckCategory Category => CheckCategory.LauncherConfig;

    public PlatformScope Scope => PlatformScope.EmulatorOnly;

    public string DisplayName => "Launcher configuration";

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var configPath = FindConfigPath();

        if (configPath is null)
        {
            findings.Add(new Finding(
                Id,
                Severity.Info,
                "Launcher configuration not found",
                $"Looked in {string.Join(" and ", CandidatePaths())}.",
                "The desktop launcher stores which emulator and which SD folder it uses. Not finding it just means the "
                + "launcher has not been run on this machine, which is fine if you install HDR manually.",
                []));

            return findings;
        }

        LauncherConfig? config;
        try
        {
            var json = await File.ReadAllTextAsync(configPath, ct).ConfigureAwait(false);
            config = JsonSerializer.Deserialize<LauncherConfig>(json);
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            findings.Add(new Finding(
                Id,
                Severity.Error,
                "Launcher configuration is unreadable",
                $"{configPath}: {e.Message}",
                "The launcher will not start properly with a corrupt config. Delete the file. The launcher recreates "
                + "it and will ask you for your emulator and SD folder again.",
                [configPath]));

            return findings;
        }

        CheckEmulatorPath(config, configPath, findings);
        CheckSdcardPath(ctx, config, configPath, findings);
        CheckForResetBackups(configPath, findings);
        await CheckStaleDownloadsAsync(ctx, findings, ct).ConfigureAwait(false);

        return findings;
    }

    private void CheckEmulatorPath(LauncherConfig? config, string configPath, List<Finding> findings)
    {
        var path = config?.RyuPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            findings.Add(new Finding(
                Id,
                Severity.Error,
                "Launcher has no emulator set",
                $"ryuPath is empty in {configPath}.",
                "The launcher will keep asking for your emulator executable every time it starts and will not get past "
                + "that prompt. Point it at your emulator once and it will remember.",
                [configPath]));

            return;
        }

        if (!File.Exists(path))
        {
            findings.Add(new Finding(
                Id,
                Severity.Error,
                "Launcher points at an emulator that no longer exists",
                $"ryuPath is {path}, which is not a file.",
                "This happens after moving, renaming, or updating the emulator. The launcher cannot start the game until this is corrected.",
                [configPath, path]));
        }
    }

    private void CheckSdcardPath(ScanContext ctx, LauncherConfig? config, string configPath, List<Finding> findings)
    {
        var path = config?.SdcardPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            findings.Add(new Finding(
                Id,
                Severity.Error,
                "Launcher has no SD folder set",
                $"sdcardPath is empty in {configPath}.",
                "Without this the launcher cannot install or update HDR — it closes with an error on startup.",
                [configPath]));

            return;
        }

        if (!Directory.Exists(path))
        {
            findings.Add(new Finding(
                Id,
                Severity.Error,
                "Launcher points at an SD folder that does not exist",
                $"sdcardPath is {path}.",
                "The launcher will fail to install or update anything. Correct the path so it points at the same "
                + "folder your emulator uses for its SD card.",
                [configPath, path]));

            return;
        }

        var scanned = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ctx.Source.RootDescription));
        var launcher = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        if (!string.Equals(scanned, launcher, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new Finding(
                Id,
                Severity.Warning,
                "The launcher installs to a different folder than the one just scanned",
                $"Launcher installs to {launcher}; this scan looked at {scanned}.",
                "This means every launcher action you've run went into a folder your emulator "
                + "may not be reading. It is the usual explanation for 'the launcher says I am up to date but the game "
                + "has not changed'. Either repoint the launcher, or scan the folder the launcher "
                + "actually uses.",
                [configPath, launcher, scanned]));

            return;
        }

        var looksLikeSdRoot = Directory.Exists(Path.Combine(path, "ultimate"))
                              || Directory.Exists(Path.Combine(path, "atmosphere"));

        if (!looksLikeSdRoot)
        {
            findings.Add(new Finding(
                Id,
                Severity.Warning,
                "The launcher's SD folder does not look like an SD root",
                $"{path} contains neither an 'ultimate' nor an 'atmosphere' folder.",
                "The launcher will install into it anyway, creating a second, unused copy of HDR. Point it at the "
                + "folder your emulator treats as the SD card. Usually one named 'sdmc' or 'sdcard'.",
                [configPath, path]));

            return;
        }

        findings.Add(new Finding(
            Id,
            Severity.Ok,
            "Launcher is pointed at this folder",
            launcher,
            string.Empty,
            [configPath]));
    }

    private void CheckForResetBackups(string configPath, List<Finding> findings)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        var backups = Directory
            .EnumerateFiles(directory, Path.GetFileName(configPath) + ".bak*")
            .ToList();

        if (backups.Count == 0)
        {
            return;
        }

        findings.Add(new Finding(
            Id,
            Severity.Info,
            $"{backups.Count} launcher config backup{(backups.Count == 1 ? "" : "s")} found",
            string.Join(", ", backups.Select(Path.GetFileName)),
            "The launcher makes one of these each time its settings are reset. Harmless, but it tells you the "
            + "launcher has been reset before.",
            backups));
    }

    private async Task CheckStaleDownloadsAsync(ScanContext ctx, List<Finding> findings, CancellationToken ct)
    {
        var leftovers = new List<string>();

        foreach (var name in new[] { "hdr-install.zip", "content_hashes.json", "deletions.json" })
        {
            var path = $"{HdrPaths.DownloadsDir}/{name}";
            if (await ctx.Source.FileExistsAsync(path, ct).ConfigureAwait(false))
            {
                leftovers.Add(path);
            }
        }

        if (leftovers.Count == 0)
        {
            return;
        }

        findings.Add(new Finding(
            Id,
            Severity.Info,
            "Leftover launcher download files",
            string.Join(", ", leftovers),
            "The launcher downloads updates into this folder and does not always clean up. They take up space and are "
            + "safe to delete. If an update failed part-way through, a leftover archive here is a hint that is what "
            + "happened.",
            leftovers));
    }

    private string? FindConfigPath() => CandidatePaths().FirstOrDefault(File.Exists);

    /// <summary>
    /// Where the launcher keeps its config. On Linux it is the XDG config
    /// directory; everywhere else the launcher writes relative to its own working
    /// directory, which in practice is its install folder.
    /// </summary>
    private IEnumerable<string> CandidatePaths()
    {
        const string fileName = "launcher-config.json";

        yield return Path.Combine(paths.ConfigHome, "hdr-launcher", fileName);
        yield return Path.Combine(paths.LocalPrograms, "hdr-launcher", fileName);
        yield return Path.Combine(paths.LocalPrograms, "HDR Launcher", fileName);
    }

    private sealed record LauncherConfig(
        [property: JsonPropertyName("ryuPath")] string? RyuPath,
        [property: JsonPropertyName("sdcardPath")] string? SdcardPath);
}
