using HdrDoctor.Core.Model;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Core.Remediations;

/// <param name="ModFolderName">Folder name under <see cref="HdrPaths.ModsDir"/>.</param>
/// <param name="ModPath">The install-relative path to that folder.</param>
/// <param name="Files">
/// The conflicting files this mod provides, filtered to the ones
/// that are actually still on disk.
/// </param>
/// <param name="DeleteWholeFolder">
/// True when removing <see cref="Files"/> would leave nothing ARCropolis would load,
/// so the whole folder itself is deleted.
/// </param>
public sealed record ModConflictOption(
    string ModFolderName,
    string ModPath,
    IReadOnlyList<string> Files,
    bool DeleteWholeFolder);

/// <summary>
/// Resolves one ARCropolis file conflict by keeping one mod and deleting the
/// conflicting files out of the others.
/// </summary>
/// <param name="keep">
/// Folder name of the mod that's kept.
/// </param>
/// <param name="deletable">
/// Every mod in this conflict that is not in <see cref="HdrPaths.ProtectedModFolderNames"/>.
/// Those mods should not be deleted.
/// </param>
public sealed class ModConflictRemediation(string keep, IReadOnlyList<ModConflictOption> deletable) : IRemediation
{
    private readonly IReadOnlyList<ModConflictOption> _deletable = deletable;

    /// <summary>The mod that survives.</summary>
    public string Keep { get; } = keep;

    /// <summary>
    /// The mods the user could keep instead, or empty when there is nothing to ask
    /// because the other mods are all protected.
    /// </summary>
    public IReadOnlyList<string> Choices { get; } =
        deletable.Any(o => string.Equals(o.ModFolderName, keep, StringComparison.OrdinalIgnoreCase))
            ? [.. deletable.Select(o => o.ModFolderName)]
            : [];

    /// <summary>The mods this deletes out of.</summary>
    public IReadOnlyList<ModConflictOption> Losers { get; } =
        [.. deletable.Where(o => !string.Equals(o.ModFolderName, keep, StringComparison.OrdinalIgnoreCase))];

    /// <summary>The same fix with a different mod kept.</summary>
    public ModConflictRemediation With(string keep) => new(keep, _deletable);

    public string Description
    {
        get
        {
            var lines = Losers.Select(o => o.DeleteWholeFolder
                ? $"Delete {o.ModPath} entirely. Every file in it that ARCropolis would load is one {Keep} also provides."
                : $"Delete {o.Files.Count} file{(o.Files.Count == 1 ? "" : "s")} from {o.ModPath} that {Keep} also provides. "
                  + "The rest of that mod is left alone.");

            return string.Join("\n", lines);
        }
    }

    public bool IsDestructive => true;

    public async Task ApplyAsync(IMutableInstallSource source, CancellationToken ct)
    {
        foreach (var loser in Losers)
        {
            if (loser.DeleteWholeFolder)
            {
                await source.DeleteDirectoryAsync(loser.ModPath, ct).ConfigureAwait(false);
                continue;
            }

            foreach (var file in loser.Files)
            {
                ct.ThrowIfCancellationRequested();
                await source.DeleteFileAsync(file, ct).ConfigureAwait(false);
            }
        }
    }
}
