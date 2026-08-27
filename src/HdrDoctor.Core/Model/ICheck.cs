namespace HdrDoctor.Core.Model;

/// <summary>
/// A single validation pass over an installation.
/// </summary>
public interface ICheck
{
    string Id { get; }

    CheckCategory Category { get; }

    PlatformScope Scope { get; }

    /// <summary>Shown in progress output while this check runs.</summary>
    string DisplayName { get; }

    Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct);
}
