using System.Text.RegularExpressions;
using HdrDoctor.Core.Model;

namespace HdrDoctor.Core.Services;

/// <param name="Pattern">What to look for in the log.</param>
/// <param name="Severity">How bad a match is.</param>
/// <param name="Title">Finding title.</param>
/// <param name="Explanation">What the message actually means.</param>
public sealed record LogRule(Regex Pattern, Severity Severity, string Title, string Explanation);

/// <summary>Scans a Skyline log for the messages that explain a broken install.</summary>
/// <remarks>
/// Skyline's output is verbose — a normal boot produces thousands of "adding file"
/// lines — so this looks only for messages that map onto a known cause, and reports
/// the most recent occurrence of each with a little surrounding context. Dumping the
/// whole log on the user helps nobody.
/// </remarks>
public static class SkylineLogParser
{
    private const int ContextLines = 2;

    /// <summary>Never read more than this much of a log; they can grow very large.</summary>
    private const long MaxBytes = 32L * 1024 * 1024;

    private static readonly LogRule[] Rules =
    [
        new(new Regex(@"panicked at|plugin as panicked|Skyline plugin has panicked", RegexOptions.IgnoreCase),
            Severity.Critical,
            "A plugin crashed",
            "A Skyline plugin panicked, which takes the whole game down. The line itself usually names the file or "
            + "value it choked on — that is the thing to fix."),

        new(new Regex(@"Unable to (?:load file for|parse) '?fighter/common/hdr/param", RegexOptions.IgnoreCase),
            Severity.Critical,
            "HDR could not load its fighter parameters",
            "HDR's parameter files are missing or corrupt. This is nearly always a partial install or an interrupted "
            + "update. Reinstalling the full package fixes it."),

        new(new Regex(@"Could not retrieve (?:Common|Shared|Agent) ParamModule", RegexOptions.IgnoreCase),
            Severity.Critical,
            "HDR's plugin and its assets are out of sync",
            "The plugin asked for a parameter its asset files do not contain. That means plugin.nro and hdr-assets "
            + "came from different versions — usually one was updated and the other was not. Reinstall the full "
            + "package so both come from the same release."),

        new(new Regex(@"hdr-assets is not enabled", RegexOptions.IgnoreCase),
            Severity.Critical,
            "hdr-assets is installed but not switched on",
            "The folder is on the SD card but ARCropolis is not loading it. On a Switch, enable hdr-assets in the "
            + "ARCropolis mod manager when it offers, or from the launcher's options."),

        new(new Regex(@"No lib\w+\.nro found", RegexOptions.IgnoreCase),
            Severity.Critical,
            "A required plugin is missing",
            "HDR's own startup check could not find one of the plugins it needs. The plugins section of this report "
            + "says which one."),

        new(new Regex(@"Stale libhdr\.nro found", RegexOptions.IgnoreCase),
            Severity.Critical,
            "A stale copy of HDR was detected at boot",
            "HDR found an old libhdr.nro from a previous packaging layout. Two copies of HDR loading at once causes "
            + "undefined behavior and crashes."),

        new(new Regex(@"Failed to (?:find filepath index|patch) ", RegexOptions.IgnoreCase),
            Severity.Warning,
            "ARCropolis could not apply some file replacements",
            "Files a mod expected to replace were not where it expected. This is usually a mod built for a different "
            + "game version, or two mods conflicting. It is often harmless, but it is where to look if specific "
            + "stages or characters misbehave."),

        new(new Regex(@"Failed to read 'sd:/ultimate/arcropolis/hashes\.txt'", RegexOptions.IgnoreCase),
            Severity.Info,
            "ARCropolis hash list not present",
            "This only affects how readable ARCropolis's own logs are — it makes them show raw numbers instead of file "
            + "names. It has no effect on the game."),
    ];

    /// <summary>Finds a log to read, or null if there is not one.</summary>
    public static string? FindLog(string sdRoot, EmulatorInstallation? emulator, IAppPaths paths)
    {
        var candidates = new List<string>
        {
            Path.Combine(sdRoot, "skyline.log"),
            Path.Combine(sdRoot, "atmosphere", "skyline.log"),
        };

        // Under an emulator, Skyline's output goes to the
        // emulator's own log file.
        if (emulator is not null)
        {
            var logDirectory = emulator.Family == EmulatorFamily.Ryujinx
                ? Path.Combine(paths.ConfigHome, "Ryujinx", "Logs")
                : Path.Combine(paths.DataHome, emulator.ProductName, "log");

            if (Directory.Exists(logDirectory))
            {
                var newest = new DirectoryInfo(logDirectory)
                    .EnumerateFiles("*.txt")
                    .Concat(new DirectoryInfo(logDirectory).EnumerateFiles("*.log"))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (newest is not null)
                {
                    candidates.Add(newest.FullName);
                }
            }
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Reads a log and returns one finding per rule that matched.</summary>
    public static async Task<IReadOnlyList<Finding>> ParseAsync(string logPath, CancellationToken ct)
    {
        var info = new FileInfo(logPath);
        if (info.Length > MaxBytes)
        {
            return
            [
                new Finding(
                    "skyline-log",
                    Severity.Info,
                    "Log file too large to scan",
                    $"{logPath} is {info.Length / 1024 / 1024} MB.",
                    "Skyline logs this big are usually the result of a plugin printing in a loop. Delete it and "
                    + "reproduce the problem once to get a readable log.",
                    [logPath])
                {
                    Category = CheckCategory.SkylineLog,
                }
            ];
        }

        var lines = await File.ReadAllLinesAsync(logPath, ct).ConfigureAwait(false);
        var findings = new List<Finding>();

        foreach (var rule in Rules)
        {
            // Search backwards: the most recent occurrence is the one describing the
            // run the user is actually complaining about.
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                ct.ThrowIfCancellationRequested();

                if (!rule.Pattern.IsMatch(lines[i]))
                {
                    continue;
                }

                var from = Math.Max(0, i - ContextLines);
                var to = Math.Min(lines.Length - 1, i + ContextLines);
                var context = lines[from..(to + 1)].Select(StripAnsi).ToList();

                findings.Add(new Finding(
                    "skyline-log",
                    rule.Severity,
                    rule.Title,
                    StripAnsi(lines[i]).Trim(),
                    rule.Explanation + $"\n\nFrom {Path.GetFileName(logPath)}, line {i + 1}:\n"
                                     + string.Join("\n", context),
                    [logPath])
                {
                    Category = CheckCategory.SkylineLog,
                });

                break;
            }
        }

        if (findings.Count == 0)
        {
            findings.Add(new Finding(
                "skyline-log",
                Severity.Ok,
                "No known problems in the log",
                $"Read {lines.Length} lines from {Path.GetFileName(logPath)}.",
                string.Empty,
                [logPath])
            {
                Category = CheckCategory.SkylineLog,
            });
        }

        return findings;
    }

    /// <summary>Skyline colors some of its output; the escape codes are noise in a report.</summary>
    private static string StripAnsi(string line) => AnsiPattern.Replace(line, string.Empty);

    private static readonly Regex AnsiPattern = new(@"\x1B\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);
}
