using HdrDoctor.Core.Model;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Core.Remediations;

/// <summary>Deletes a single file.</summary>
public sealed class DeleteFileRemediation(string relativePath, string description) : IRemediation
{
    public string Description { get; } = description;

    public bool IsDestructive => true;

    public Task ApplyAsync(IMutableInstallSource source, CancellationToken ct) =>
        source.DeleteFileAsync(relativePath, ct);
}
