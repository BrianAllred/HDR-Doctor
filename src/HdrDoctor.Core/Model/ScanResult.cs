namespace HdrDoctor.Core.Model;

/// <summary>The outcome of one full scan: what was found, and what could not be looked at.</summary>
public sealed record ScanResult(
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<SkippedCheck> Skipped,
    EnvironmentSummary Environment,
    DateTimeOffset CompletedAt,
    TimeSpan Duration)
{
    /// <summary>
    /// When the file verification step was run into this result, or null if it never
    /// was.
    /// </summary>
    public DateTimeOffset? VerifiedAt { get; init; }

    public IEnumerable<Finding> Problems => Findings.Where(f => f.Severity != Severity.Ok);

    public bool HasProblems => Problems.Any();

    public Severity? WorstSeverity => Problems.Select(f => f.Severity).DefaultIfEmpty().Min() is var min && HasProblems
        ? min
        : null;

    public int CountOf(Severity severity) => Findings.Count(f => f.Severity == severity);

    /// <summary>
    /// How many problems of each severity, worst first, skipping the ones with none.
    /// Both the report summary and the window's status line render this, so they say
    /// the same thing.
    /// </summary>
    public IEnumerable<(Severity Severity, int Count)> ProblemCounts() =>
        new[] { Severity.Critical, Severity.Error, Severity.Warning, Severity.Info }
            .Select(s => (Severity: s, Count: CountOf(s)))
            .Where(x => x.Count > 0);

    /// <summary>
    /// Folds a separately-run file verification into this scan.
    /// </summary>
    /// <remarks>
    /// Anything the previous run of that check left behind is dropped first
    /// so that the new findings replace it. Prevents duplicate findings.
    /// </remarks>
    public ScanResult WithVerification(VerificationResult verification)
    {
        var findings = Findings
            .Where(f => f.CheckId != verification.CheckId)
            .Concat(verification.Findings)
            .OrderBy(f => f.Severity);

        var skipped = Skipped.Where(s => s.CheckId != verification.CheckId);

        if (verification.Skipped is not null)
        {
            skipped = skipped.Append(verification.Skipped);
        }

        return this with
        {
            Findings = [.. findings],
            Skipped = [.. skipped],
            VerifiedAt = verification.Ran ? DateTimeOffset.Now : null,
        };
    }
}

/// <summary>
/// The outcome of the file verification step.
/// </summary>
/// <param name="CheckId">
/// The check these came from.
/// </param>
/// <param name="Findings">What verification found, worst first. Empty when it could not run.</param>
/// <param name="Skipped">Set only when verification could not run, and says why.</param>
/// <param name="Duration">How long it took, which on a real SD card is worth reporting.</param>
public sealed record VerificationResult(
    string CheckId,
    IReadOnlyList<Finding> Findings,
    SkippedCheck? Skipped,
    TimeSpan Duration)
{
    public bool Ran => Skipped is null;
}

/// <param name="CheckId">The check that did not run.</param>
/// <param name="DisplayName">Its human-readable name.</param>
/// <param name="Reason">Why it was skipped — wrong platform, no network, missing prerequisite.</param>
public sealed record SkippedCheck(string CheckId, string DisplayName, string Reason);

/// <summary>
/// The header of the report: what was scanned and what it turned out to be.
/// </summary>
public sealed record EnvironmentSummary
{
    public required string SourceDescription { get; init; }

    public required string SourceKind { get; init; }

    public required InstallPlatform Platform { get; init; }

    public string? HdrVersion { get; init; }

    public string? AssetsVersion { get; init; }

    public ReleaseChannel Channel { get; init; }

    public string? MainModFolder { get; init; }

    public string? EmulatorName { get; init; }

    public string? EmulatorConfigPath { get; init; }

    public bool FixesAvailable { get; init; }

    public bool FileListAvailable { get; init; }
}
