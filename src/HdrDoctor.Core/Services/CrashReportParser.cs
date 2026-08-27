using System.Globalization;
using System.Text.RegularExpressions;
using HdrDoctor.Core.Model;

namespace HdrDoctor.Core.Services;

/// <summary>
/// One crash report on the SD card.
/// </summary>
/// <param name="RelativePath">Install-root-relative path, for the report and for revealing the file.</param>
/// <param name="Size">File size in bytes.</param>
/// <param name="CrashedAt">
/// When the crash happened, in local time, from the timestamp in the file name.
/// </param>
/// <param name="ProgramId">Lowercase 16-hex-digit title ID</param>
public sealed record CrashReportFile(string RelativePath, long Size, DateTimeOffset? CrashedAt, string? ProgramId)
{
    public string FileName => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];

    /// <summary>
    /// False when the timestamp is zero (clock isn't set, for example)
    /// </summary>
    public bool ClockWasSet => CrashedAt is { } at && at > DateTimeOffset.UnixEpoch;

    public bool IsFor(string titleId) =>
        ProgramId is not null && ProgramId.Equals(titleId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a crash report says</summary>
/// <remarks>
/// Every field is optional because Atmosphere's report layout is not versioned.
/// </remarks>
/// <param name="AtmosphereVersion">From the report's first line, e.g. <c>1.7.1</c>.</param>
/// <param name="ProcessName">The crashed process, e.g. <c>Application</c>.</param>
/// <param name="ProgramId">Title ID.</param>
/// <param name="Result">Atmosphere's result code.</param>
/// <param name="ExceptionType">e.g. <c>Data Abort (0x101)</c>.</param>
/// <param name="FaultAddress">Where execution was when it died.</param>
/// <param name="AccessAddress">The address it tried to touch, for a data abort.</param>
/// <param name="Modules">Module names the stack trace attributes frames to, most recent frame first.</param>
public sealed record CrashReportSummary(
    string? AtmosphereVersion,
    string? ProcessName,
    string? ProgramId,
    string? Result,
    string? ExceptionType,
    string? FaultAddress,
    string? AccessAddress,
    IReadOnlyList<string> Modules)
{
    public string? SkylineFrame =>
        Modules.FirstOrDefault(m => m.StartsWith("subsdk", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Reads Atmosphere crash reports out of <c>sd:/atmosphere/crash_reports</c> and
/// turns the newest ones into findings.
/// </summary>
/// <remarks>
/// Format from Atmosphere's <c>stratosphere/creport/source/creport_crash_report.cpp</c>,
/// which writes <c>&lt;timestamp&gt;_&lt;program id&gt;.log</c>.
/// The timestamp is zero-padded to a fixed width, so an ordinal sort of
/// the file names is a chronological sort.
///
/// The body is parsed as indented <c>Label: value</c> pairs under section headings
/// rather than by position, and every field is optional.
/// </remarks>
public static partial class CrashReportParser
{
    public const string CheckId = "crash-report";

    /// <summary>
    /// A crash report is normally well under a megabyte.
    /// </summary>
    private const long MaxBytes = 8L * 1024 * 1024;

    /// <summary><c>%011lu_%016lx.log</c>, as creport writes it.</summary>
    [GeneratedRegex(@"^(?<time>\d{1,19})_(?<program>[0-9a-fA-F]{16})\.log$")]
    private static partial Regex NamePattern { get; }

    [GeneratedRegex(@"Crash Report \(v?(?<version>[0-9][0-9.]*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderPattern { get; }

    /// <summary>An indented <c>Label:</c> followed by a value.</summary>
    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<label>[A-Za-z][^:]*?):[ \t]+(?<value>\S.*)$")]
    private static partial Regex FieldPattern { get; }

    /// <summary>An unindented <c>Section Name:</c> with nothing after it.</summary>
    [GeneratedRegex(@"^(?<label>[A-Za-z][^:]*?):[ \t]*$")]
    private static partial Regex SectionPattern { get; }

    /// <summary>Stack trace values look like <c>subsdk9[0000000000123456]</c>.</summary>
    [GeneratedRegex(@"^(?<module>[A-Za-z0-9_.\-]+)\[")]
    private static partial Regex FramePattern { get; }

    /// <summary>
    /// What an exception type means for somebody troubleshooting HDR.
    /// </summary>
    /// <remarks>
    /// These read the fault in terms of an HDR install.
    /// </remarks>
    private static readonly (string Type, string Meaning)[] ExceptionMeanings =
    [
        ("user break",
            "A break instruction — the process deliberately aborted rather than being killed by the CPU. A Rust "
            + "panic inside a Skyline plugin ends up here, so this is the hardware counterpart of a \"panicked at\" "
            + "line in a Skyline log. The cause is nearly always a plugin reading something that is missing or in "
            + "the wrong format."),

        ("data abort",
            "The process read or wrote memory that is not there. In an HDR install that usually means a plugin "
            + "followed a pointer into a file or parameter that was never loaded — a partial install, or a plugin "
            + "and its assets that came from different releases."),

        ("instruction abort",
            "The process tried to execute an address that holds no code. That normally means a hook was installed "
            + "over a function that has since moved, which happens when a plugin was built for a different game or "
            + "HDR version than the one installed."),

        ("prefetch abort",
            "The process tried to fetch an instruction from an address that holds no code — the same shape of "
            + "problem as an instruction abort: a hook pointing somewhere that is no longer valid."),

        ("undefined instruction",
            "The process executed something that is not a valid instruction, so execution had already gone "
            + "somewhere it should not be. As with an instruction abort, suspect a plugin built against a "
            + "different version."),

        ("unaligned",
            "The process accessed memory at an address the CPU will not accept for an access of that size. That "
            + "is a corrupted pointer rather than a missing file, so hash-verifying the install is unlikely to "
            + "turn anything up."),
    ];

    /// <summary>
    /// Turns a directory listing into the crash reports it contains, newest first.
    /// </summary>
    /// <remarks>
    /// Anything that is not a <c>.log</c> is dropped.
    /// </remarks>
    public static IReadOnlyList<CrashReportFile> Describe(IEnumerable<Sources.SourceEntry> entries) =>
    [
        .. entries
            .Where(e => !e.IsDirectory && e.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            .Select(Describe)
            .OrderByDescending(r => r.CrashedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(r => r.FileName, StringComparer.Ordinal)
    ];

    private static CrashReportFile Describe(Sources.SourceEntry entry)
    {
        var match = NamePattern.Match(entry.Name);

        if (!match.Success
            || !long.TryParse(match.Groups["time"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds < 0
            || seconds > 253_402_300_799)
        {
            return new CrashReportFile(entry.RelativePath, entry.Size, null, null);
        }

        return new CrashReportFile(
            entry.RelativePath,
            entry.Size,
            // Atmosphere records UTC. Convert to local time.
            DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime(),
            match.Groups["program"].Value.ToLowerInvariant());
    }

    /// <summary>Reads what it can out of a report's text.</summary>
    public static CrashReportSummary Summarize(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var modules = new List<string>();
        string? section = null;
        string? version = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (version is null && HeaderPattern.Match(line) is { Success: true } header)
            {
                version = header.Groups["version"].Value;
                continue;
            }

            if (FieldPattern.Match(line) is { Success: true } field)
            {
                var label = field.Groups["label"].Value.Trim();
                var value = field.Groups["value"].Value.Trim();
                var indented = field.Groups["indent"].Length > 0;
                var key = indented && section is not null ? $"{section}/{label}" : label;
                fields.TryAdd(key, value);

                if (indented
                    && section is not null
                    && section.Contains("Stack Trace", StringComparison.OrdinalIgnoreCase)
                    && FramePattern.Match(value) is { Success: true } frame)
                {
                    modules.Add(frame.Groups["module"].Value);
                }

                continue;
            }

            if (SectionPattern.Match(line) is { Success: true } heading)
            {
                section = heading.Groups["label"].Value.Trim();
            }
        }

        return new CrashReportSummary(
            version,
            Field(fields, "Process Info/Program Name", "Process Info/Process Name", "Program Name", "Process Name"),
            Field(fields, "Process Info/Program ID", "Program ID")?.ToLowerInvariant(),
            Field(fields, "Result"),
            Field(fields, "Exception Info/Type", "Type"),
            Field(fields, "Exception Info/Address", "Address"),
            Field(fields, "Exception Info/Access Address", "Access Address"),
            modules);
    }

    private static string? Field(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Turns one report into a finding.
    /// </summary>
    public static Finding Explain(
        CrashReportFile file,
        string? text,
        Severity severity,
        string title,
        string lead)
    {
        if (text is null)
        {
            return new Finding(
                CheckId,
                Severity.Info,
                "A crash report could not be read",
                file.RelativePath,
                "The file is listed on the SD card but could not be opened. Copying it off the card and opening it "
                + "in a text editor will still show what crashed.",
                [file.RelativePath])
            {
                Category = CheckCategory.CrashReport,
            };
        }

        var summary = Summarize(text);
        var explanation = new List<string> { lead };

        if (DescribeException(summary.ExceptionType) is { } meaning)
        {
            explanation.Add(meaning);
        }

        if (summary.SkylineFrame is { } frame)
        {
            explanation.Add(
                $"The stack trace points into {frame}, which is where Skyline lives — Skyline is installed as one of "
                + "the game's subsdks. So the code that died was Skyline or one of the plugins it loaded, not the "
                + "game's own. Compare this against the plugins section of this report.");
        }

        explanation.Add(
            "A crash report describes one moment in the past, not the state of the install now. If you have "
            + "reinstalled or changed plugins since "
            + (file.ClockWasSet ? $"{file.CrashedAt:yyyy-MM-dd HH:mm}" : "it was written")
            + ", it may already be fixed.");

        explanation.Add("From " + file.FileName + ":\n" + Quote(summary));

        return new Finding(
            CheckId,
            severity,
            title,
            Detail(file, summary),
            string.Join("\n\n", explanation),
            [file.RelativePath])
        {
            Category = CheckCategory.CrashReport,
        };
    }

    private static string Detail(CrashReportFile file, CrashReportSummary summary)
    {
        var parts = new List<string>();

        if (summary.ExceptionType is { } type)
        {
            parts.Add(type);
        }

        parts.Add(file.ClockWasSet
            ? $"at {file.CrashedAt:yyyy-MM-dd HH:mm:ss}"
            : "at an unknown time — the console's clock had not been set");

        return string.Join(", ", parts);
    }

    /// <summary>The handful of report lines worth pasting into the finding.</summary>
    private static string Quote(CrashReportSummary summary)
    {
        var lines = new List<string>();

        Add("Atmosphere", summary.AtmosphereVersion);
        Add("Process", summary.ProcessName);
        Add("Program ID", summary.ProgramId);
        Add("Result", summary.Result);
        Add("Exception", summary.ExceptionType);
        Add("Address", summary.FaultAddress);
        Add("Access address", summary.AccessAddress);

        if (summary.Modules.Count > 0)
        {
            Add("Stack (innermost first)", string.Join(" < ", summary.Modules.Distinct().Take(6)));
        }

        return lines.Count == 0
            ? "  (none of the expected fields were present — the file may be truncated)"
            : string.Join("\n", lines);

        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                lines.Add($"  {label}: {value}");
            }
        }
    }

    private static string? DescribeException(string? exceptionType)
    {
        if (exceptionType is null)
        {
            return null;
        }

        foreach (var (type, meaning) in ExceptionMeanings)
        {
            if (exceptionType.Contains(type, StringComparison.OrdinalIgnoreCase))
            {
                return meaning;
            }
        }

        return null;
    }

    /// <summary>True when the file is small enough to be worth reading into memory.</summary>
    public static bool IsReadableSize(CrashReportFile file) => file.Size <= MaxBytes;
}
