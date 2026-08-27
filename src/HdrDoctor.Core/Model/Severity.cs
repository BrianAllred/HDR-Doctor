namespace HdrDoctor.Core.Model;

/// <summary>
/// How badly a finding affects the installation. Ordered most-severe-first so
/// that <c>OrderBy(f => f.Severity)</c> produces the display order we want.
/// </summary>
public enum Severity
{
    /// <summary>The game will not boot, or will panic with no dialog.</summary>
    Critical = 0,

    /// <summary>The game boots but something is broken or will desync online.</summary>
    Error = 1,

    /// <summary>Non-standard, risky, or a likely cause of the reported symptom.</summary>
    Warning = 2,

    /// <summary>Worth mentioning while troubleshooting, but not itself a problem.</summary>
    Info = 3,

    /// <summary>Explicitly verified as correct.</summary>
    Ok = 4,
}

public static class SeverityExtensions
{
    /// <summary>The severity's name as the UI and the report both spell it.</summary>
    public static string Label(this Severity severity) => severity switch
    {
        Severity.Critical => "Critical",
        Severity.Error => "Error",
        Severity.Warning => "Warning",
        Severity.Info => "Note",
        _ => "OK",
    };

    /// <summary>
    /// The noun for a count of these, pluralized — "1 critical problem", "3 errors".
    /// </summary>
    public static string Describe(this Severity severity, int count)
    {
        var noun = severity switch
        {
            Severity.Critical => "critical problem",
            Severity.Error => "error",
            Severity.Warning => "warning",
            Severity.Info => "note",
            _ => "passing check",
        };

        return $"{count} {noun}{(count == 1 ? "" : "s")}";
    }
}
