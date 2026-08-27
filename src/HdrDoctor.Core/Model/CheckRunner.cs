namespace HdrDoctor.Core.Model;

/// <summary>
/// Runs a set of checks against one installation and collects their findings.
/// </summary>
/// <remarks>
/// A check that throws is reported as a finding rather than aborting the scan.
///
/// Every finding is stamped with its check's <see cref="ICheck.Category"/> on the way
/// out, so a check never has to remember to set it. <see cref="Finding"/>'s own
/// default is <see cref="CheckCategory.InstallLayout"/>, and a check that forgot used
/// to file its findings there silently.
/// </remarks>
public sealed class CheckRunner(IEnumerable<ICheck> checks)
{
    private readonly IReadOnlyList<ICheck> _checks = [.. checks];

    public async Task<IReadOnlyList<Finding>> RunAsync(
        ScanContext ctx,
        List<SkippedCheck> skipped,
        CancellationToken ct)
    {
        var findings = new List<Finding>();
        var total = _checks.Count;
        var index = 0;

        foreach (var check in _checks)
        {
            ct.ThrowIfCancellationRequested();
            index++;

            if (!check.Scope.AppliesTo(ctx.Platform))
            {
                skipped.Add(new SkippedCheck(
                    check.Id,
                    check.DisplayName,
                    $"Only applies to {(check.Scope == PlatformScope.SwitchOnly ? "Switch" : "emulator")} installs."));
                continue;
            }

            ctx.Report(check.DisplayName, fraction: (double)index / total);

            try
            {
                findings.AddRange(Categorize(check, await check.RunAsync(ctx, ct).ConfigureAwait(false)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                findings.Add(new Finding(
                    check.Id,
                    Severity.Warning,
                    $"Could not complete the {check.DisplayName.ToLowerInvariant()} check",
                    e.Message,
                    "This check hit an unexpected error, so this part of the install was not verified. "
                    + "The rest of the report is still valid.",
                    [])
                {
                    Category = check.Category,
                });
            }
        }

        return findings;
    }

    /// <summary>
    /// Files a check's findings under its own category. Tests that run a check
    /// directly go through here too, so both paths agree.
    /// </summary>
    public static IEnumerable<Finding> Categorize(ICheck check, IEnumerable<Finding> findings) =>
        findings.Select(f => f with { Category = check.Category });
}
