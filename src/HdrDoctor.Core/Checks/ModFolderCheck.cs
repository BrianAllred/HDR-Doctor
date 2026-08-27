using HdrDoctor.Core.Model;

namespace HdrDoctor.Core.Checks;

/// <summary>
/// Checks that HDR's own mod folders are there and readable, and reports the
/// versions.
/// </summary>
/// <remarks>
/// Structure only; the contents of the folders are checked in <c>FileVerificationCheck</c>.
/// </remarks>
public sealed class ModFolderCheck : ICheck
{
    public string Id => "mod-folders";

    public CheckCategory Category => CheckCategory.HdrModFolders;

    public PlatformScope Scope => PlatformScope.Any;

    public string DisplayName => "HDR mod folders";

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct)
    {
        var findings = new List<Finding>();

        await CheckFoldersExistAsync(ctx, findings, ct).ConfigureAwait(false);
        CheckVersions(ctx, findings);

        return findings;
    }

    private async Task CheckFoldersExistAsync(ScanContext ctx, List<Finding> findings, CancellationToken ct)
    {
        if (ctx.Versions.MainModFolder is null)
        {
            findings.Add(new Finding(
                Id,
                Severity.Critical,
                "HDR is not installed",
                $"No folder under {HdrPaths.ModsDir} contains both a plugin.nro and a ui/hdr_version.txt.",
                "The main HDR mod folder is missing entirely. If you expected HDR to be here, either the install never "
                + "finished or the launcher was pointed at a different folder. Check the launcher check below.",
                [HdrPaths.HdrDir]));
        }

        foreach (var (folder, name, purpose) in new[]
                 {
                     (HdrPaths.HdrAssetsDir, "hdr-assets", "every model, texture, sound and animation HDR replaces"),
                     (HdrPaths.HdrStagesDir, "hdr-stages", "HDR's stage layouts and stage variants"),
                 })
        {
            if (await ctx.Source.DirectoryExistsAsync(folder, ct).ConfigureAwait(false))
            {
                continue;
            }

            findings.Add(new Finding(
                Id,
                Severity.Critical,
                $"{name} is missing",
                $"{folder} was not found.",
                $"This folder holds {purpose}. Without it the install is incomplete: HDR itself flags this at boot and "
                + "the game will be visibly wrong or crash. Install the full HDR package rather than just the plugin.",
                [folder]));
        }
    }

    private void CheckVersions(ScanContext ctx, List<Finding> findings)
    {
        if (ctx.Versions.PluginVersion is null)
        {
            return;
        }

        findings.Add(new Finding(
            Id,
            Severity.Ok,
            $"HDR {ctx.Versions.PluginVersion}",
            ctx.Versions.AssetsVersion is null
                ? "Assets version could not be read."
                : $"Assets {ctx.Versions.AssetsVersion}.",
            string.Empty,
            [HdrPaths.HdrVersionFile]));

        if (ctx.Versions.AssetsVersion is null)
        {
            findings.Add(new Finding(
                Id,
                Severity.Warning,
                "Assets version could not be read",
                $"{HdrPaths.RomfsVersionFile} is missing or empty.",
                "HDR reads this file to show the assets version in-game. Its absence usually means hdr-assets was "
                + "installed by hand or only partly extracted, which tends to go with mismatched assets.",
                [HdrPaths.RomfsVersionFile]));
        }
    }
}
