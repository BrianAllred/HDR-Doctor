using HdrDoctor.Core.Model;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Core.Services;

/// <summary>Reads HDR's version files out of an install.</summary>
/// <remarks>
/// The main mod folder is not reliably named <c>hdr</c>. CI builds pull-request
/// packages into <c>hdr-pr</c>, and a local
/// dev build defaults to <c>hdr-dev</c>. Identify the folder by what is inside it, 
/// which should be a <c>plugin.nro</c> next to a <c>ui/hdr_version.txt</c>, and
/// only use the name as a tiebreaker.
/// </remarks>
public static class HdrVersionReader
{
    /// <summary>Preferred order when more than one candidate folder exists.</summary>
    private static readonly string[] PreferredOrder = ["hdr", "hdr-pr", "hdr-private", "hdr-dev"];

    public static async Task<HdrVersionInfo> ReadAsync(IInstallSource source, CancellationToken ct)
    {
        var mainFolder = await FindMainModFolderAsync(source, ct).ConfigureAwait(false);

        var pluginVersion = mainFolder is null
            ? null
            : Clean(await source.ReadAllTextAsync($"{mainFolder}/ui/hdr_version.txt", ct).ConfigureAwait(false));

        var assetsVersion = Clean(
            await source.ReadAllTextAsync(HdrPaths.RomfsVersionFile, ct).ConfigureAwait(false));

        return new HdrVersionInfo(
            pluginVersion,
            assetsVersion,
            HdrVersionInfo.ChannelFor(pluginVersion),
            mainFolder);
    }

    /// <summary>
    /// Finds the mod folder HDR's plugin is installed in, or null if there is not one.
    /// </summary>
    public static async Task<string?> FindMainModFolderAsync(IInstallSource source, CancellationToken ct)
    {
        var entries = await source.ListAsync(HdrPaths.ModsDir, recursive: false, ct).ConfigureAwait(false);

        var candidates = new List<string>();
        foreach (var entry in entries.Where(e => e.IsDirectory))
        {
            var hasPlugin = await source.FileExistsAsync($"{entry.RelativePath}/plugin.nro", ct).ConfigureAwait(false);
            var hasVersion = await source
                .FileExistsAsync($"{entry.RelativePath}/ui/hdr_version.txt", ct)
                .ConfigureAwait(false);

            if (hasPlugin && hasVersion)
            {
                candidates.Add(entry.RelativePath);
            }
        }

        if (candidates.Count <= 1)
        {
            return candidates.FirstOrDefault();
        }

        // More than one HDR build installed. Prefer the official folder name so the
        // report describes the build that is most likely actually in use; the
        // duplicate itself is reported separately by the mod-folder check.
        foreach (var preferred in PreferredOrder)
        {
            var match = candidates.FirstOrDefault(c =>
                string.Equals(c[(c.LastIndexOf('/') + 1)..], preferred, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        return candidates[0];
    }

    private static string? Clean(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().TrimEnd('\0').Trim();
}
