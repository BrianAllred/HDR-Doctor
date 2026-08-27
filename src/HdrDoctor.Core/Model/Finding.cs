namespace HdrDoctor.Core.Model;

/// <summary>One thing the scan noticed about an installation.</summary>
/// <param name="CheckId">Stable identifier of the check that produced this, for the report and tests.</param>
/// <param name="Severity">How badly this affects the install.</param>
/// <param name="Title">One line, scannable in a list.</param>
/// <param name="Detail">What was actually observed.</param>
/// <param name="Explanation">
/// Details and why it matters.
/// </param>
/// <param name="Paths">Affected paths, relative to the install root.</param>
/// <param name="Remediation">A fix the user may choose to perform, or null if there is nothing safe to do.</param>
public sealed record Finding(
    string CheckId,
    Severity Severity,
    string Title,
    string Detail,
    string Explanation,
    IReadOnlyList<string> Paths,
    IRemediation? Remediation = null)
{
    public CheckCategory Category { get; init; } = CheckCategory.InstallLayout;

    public bool CanBeFixed => Remediation is not null;

    public static Finding Ok(string checkId, string title, string detail = "") =>
        new(checkId, Severity.Ok, title, detail, string.Empty, []);
}
