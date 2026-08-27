using HdrDoctor.Core.Model;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Core.Services;

/// <param name="Finding">The finding the fix belonged to.</param>
/// <param name="Succeeded">Whether it was applied.</param>
/// <param name="Error">Why it failed, when it did.</param>
public sealed record RemediationOutcome(Finding Finding, bool Succeeded, string? Error);

/// <summary>Applies the fixes a user has chosen.</summary>
/// <remarks>
/// Deliberately separate from scanning. Nothing here runs as part of a scan, and it
/// takes an <see cref="IMutableInstallSource"/>, so it cannot be pointed at a
/// read-only source such as an FTP connection.
///
/// One failing fix does not stop the rest — a user who selected six things should not
/// have four of them silently skipped because the second one hit a locked file.
/// </remarks>
public static class RemediationService
{
    public static async Task<IReadOnlyList<RemediationOutcome>> ApplyAsync(
        IMutableInstallSource source,
        IEnumerable<Finding> findings,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var outcomes = new List<RemediationOutcome>();

        foreach (var finding in findings)
        {
            ct.ThrowIfCancellationRequested();

            if (finding.Remediation is null)
            {
                continue;
            }

            progress?.Report(new ScanProgress("Applying fixes", finding.Title, null));

            try
            {
                await finding.Remediation.ApplyAsync(source, ct).ConfigureAwait(false);
                outcomes.Add(new RemediationOutcome(finding, true, null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                outcomes.Add(new RemediationOutcome(finding, false, e.Message));
            }
        }

        return outcomes;
    }

    /// <summary>
    /// The text shown before anything is touched. Destructive steps are listed first
    /// so they cannot be missed by someone skimming.
    /// </summary>
    public static string DescribePlan(IEnumerable<Finding> findings)
    {
        var steps = findings
            .Where(f => f.Remediation is not null)
            .Select(f => f.Remediation!)
            .OrderByDescending(r => r.IsDestructive)
            .ToList();

        if (steps.Count == 0)
        {
            return "Nothing selected.";
        }

        var lines = steps.Select(r => (r.IsDestructive ? "Deletes data — " : string.Empty) + r.Description);
        return string.Join("\n\n", lines);
    }
}
