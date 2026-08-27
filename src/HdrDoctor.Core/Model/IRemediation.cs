using HdrDoctor.Core.Sources;

namespace HdrDoctor.Core.Model;

/// <summary>
/// A fix the app can apply for the user. Detection never applies one:
/// the scan reports, the user picks, and only then does <see cref="ApplyAsync"/> run.
/// </summary>
/// <remarks>
/// <see cref="ApplyAsync"/> takes an <see cref="IMutableInstallSource"/> rather than
/// an <see cref="IInstallSource"/> on purpose so that FTP sources can't be passed in.
/// </remarks>
public interface IRemediation
{
    /// <summary>Shown as is in the confirmation dialog. Say exactly what will happen.</summary>
    string Description { get; }

    /// <summary>True when this deletes data.</summary>
    bool IsDestructive { get; }

    Task ApplyAsync(IMutableInstallSource source, CancellationToken ct);
}
