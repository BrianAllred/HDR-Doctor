using HdrDoctor.Core.Model;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Core.Remediations;

/// <summary>Deletes a directory and everything under it.</summary>
public sealed class DeleteDirectoryRemediation(string relativePath, string description) : IRemediation
{
    public string Description { get; } = description;

    public bool IsDestructive => true;

    public Task ApplyAsync(IMutableInstallSource source, CancellationToken ct) =>
        source.DeleteDirectoryAsync(relativePath, ct);
}
