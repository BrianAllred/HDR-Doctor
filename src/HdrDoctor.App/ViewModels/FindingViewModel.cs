using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using HdrDoctor.Core.Model;

namespace HdrDoctor.App.ViewModels;

/// <summary>
/// One finding as the results list shows it.
/// </summary>
public sealed partial class FindingViewModel(Finding finding) : ViewModelBase
{
    /// <summary>
    /// Whether the user has ticked this finding's fix to be applied.
    /// </summary>
    [ObservableProperty]
    public partial bool SelectedForFix { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = finding.Severity == Severity.Critical;

    public Finding Finding { get; } = finding;

    public Severity Severity => Finding.Severity;

    public string Title => Finding.Title;

    public string Detail => Finding.Detail;

    public string Explanation => Finding.Explanation;

    public IReadOnlyList<string> Paths => Finding.Paths;

    /// <summary>The paths as one block of text, for the expanded row.</summary>
    public string PathText => string.Join("\n", Finding.Paths);

    public bool HasPaths => Finding.Paths.Count > 0;

    public bool HasExplanation => !string.IsNullOrWhiteSpace(Finding.Explanation);

    public bool CanBeFixed => Finding.CanBeFixed;

    public string? FixDescription => Finding.Remediation?.Description;

    /// <summary>The first path, used for the "show me" button.</summary>
    public string? PrimaryPath => Finding.Paths.FirstOrDefault();

    public string SeverityLabel => Severity.Label();

    /// <summary>
    /// Resource key for the severity color.
    /// </summary>
    public string SeverityBrushKey => Severity switch
    {
        Severity.Critical => "SeverityCriticalBrush",
        Severity.Error => "SeverityErrorBrush",
        Severity.Warning => "SeverityWarningBrush",
        Severity.Info => "SeverityInfoBrush",
        _ => "SeverityOkBrush",
    };
}

/// <summary>
/// A category heading with its findings underneath.
/// </summary>
public sealed class FindingGroupViewModel(CheckCategory category, IEnumerable<FindingViewModel> findings)
{
    public CheckCategory Category { get; } = category;

    public string Title => Category.DisplayName();

    public IReadOnlyList<FindingViewModel> Findings { get; } = [.. findings.OrderBy(f => f.Severity)];

    public string Summary
    {
        get
        {
            var problems = Findings.Count(f => f.Severity != Severity.Ok);
            return problems == 0
                ? "All clear"
                : $"{problems} to look at";
        }
    }
}
