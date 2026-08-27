using HdrDoctor.Core.Model;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Core.Remediations;

/// <summary>
/// Moves a plugin out of the active plugins folder into <c>disabled_plugins</c>
/// </summary>
/// <remarks>
/// Preferred over deletion on Switch in case the user wants to re-enable it.
/// </remarks>
public sealed class DisablePluginRemediation(string fileName) : IRemediation
{
    public string Description { get; } = $"Move {fileName} from {HdrPaths.PluginsDir} to {HdrPaths.DisabledPluginsDir}, "
                      + "so it stops loading but is not deleted.";

    public bool IsDestructive => false;

    public async Task ApplyAsync(IMutableInstallSource source, CancellationToken ct)
    {
        await source.CreateDirectoryAsync(HdrPaths.DisabledPluginsDir, ct).ConfigureAwait(false);
        await source.MoveAsync(
            $"{HdrPaths.PluginsDir}/{fileName}",
            $"{HdrPaths.DisabledPluginsDir}/{fileName}",
            ct).ConfigureAwait(false);
    }
}
