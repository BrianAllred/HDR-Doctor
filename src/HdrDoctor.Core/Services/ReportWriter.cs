using System.Text;
using HdrDoctor.Core.Model;

namespace HdrDoctor.Core.Services;

/// <summary>
/// Renders a scan into plain text for sharing.
/// </summary>
/// <remarks>
/// The audience is somebody else reading it cold — in a support channel, usually.
/// So the environment block comes first and is never abbreviated, problems are
/// ordered worst-first, and the things that were <em>not</em> checked are stated
/// explicitly. A report that quietly omits a skipped check invites the wrong
/// conclusion from its silence.
/// </remarks>
public static class ReportWriter
{
    public static string Write(ScanResult result, bool includePassing = false)
    {
        var text = new StringBuilder();

        WriteHeader(text, result);
        WriteCounts(text, result);
        WriteFindings(text, result, includePassing);
        WriteSkipped(text, result);

        return text.ToString();
    }

    private static void WriteHeader(StringBuilder text, ScanResult result)
    {
        var env = result.Environment;

        text.AppendLine("HDR Doctor report");
        text.AppendLine("=========================");
        text.AppendLine($"Generated:     {result.CompletedAt:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine($"Scanned:       {env.SourceDescription}");
        text.AppendLine($"Source:        {env.SourceKind}");
        text.AppendLine($"Platform:      {env.Platform}");
        text.AppendLine($"HDR version:   {env.HdrVersion ?? "not detected"}");
        text.AppendLine($"Assets:        {env.AssetsVersion ?? "not detected"}");
        text.AppendLine($"Channel:       {env.Channel}");

        if (env.MainModFolder is not null)
        {
            text.AppendLine($"Mod folder:    {env.MainModFolder}");
        }

        if (env.EmulatorName is not null)
        {
            text.AppendLine($"Emulator:      {env.EmulatorName}");
        }

        if (env.EmulatorConfigPath is not null)
        {
            text.AppendLine($"Emulator conf: {env.EmulatorConfigPath}");
        }

        text.AppendLine($"Scan took:     {result.Duration.TotalSeconds:0.0}s");

        // Whether the files were hashed against the release is its own line, because
        // verification is a step somebody has to start: a report that simply says
        // nothing about it reads as one where the files came back clean.
        text.AppendLine(result.VerifiedAt is { } verified
            ? $"Files:         verified against the release at {verified:yyyy-MM-dd HH:mm:ss}"
            : "Files:         not verified");
        text.AppendLine();
    }

    private static void WriteCounts(StringBuilder text, ScanResult result)
    {
        var counts = result.ProblemCounts().Select(c => c.Severity.Describe(c.Count)).ToList();

        text.AppendLine(counts.Count == 0
            ? "Summary:       No problems found."
            : $"Summary:       {string.Join(", ", counts)}.");
        text.AppendLine();
    }

    private static void WriteFindings(StringBuilder text, ScanResult result, bool includePassing)
    {
        var shown = result.Findings
            .Where(f => includePassing || f.Severity != Severity.Ok)
            .ToList();

        if (shown.Count == 0)
        {
            text.AppendLine("Nothing to report — this installation looks correct.");
            text.AppendLine();
            return;
        }

        foreach (var group in shown.GroupBy(f => f.Category).OrderBy(g => g.Key))
        {
            text.AppendLine(group.Key.DisplayName());
            text.AppendLine(new string('-', group.Key.DisplayName().Length));
            text.AppendLine();

            foreach (var finding in group.OrderBy(f => f.Severity))
            {
                text.AppendLine($"[{finding.Severity.Label().ToUpperInvariant()}] {finding.Title}");

                if (!string.IsNullOrWhiteSpace(finding.Detail))
                {
                    text.AppendLine($"  {finding.Detail}");
                }

                if (!string.IsNullOrWhiteSpace(finding.Explanation))
                {
                    foreach (var line in Wrap(finding.Explanation, 92))
                    {
                        text.AppendLine($"  {line}");
                    }
                }

                if (finding.Paths.Count > 0 && finding.Severity != Severity.Ok)
                {
                    foreach (var path in finding.Paths)
                    {
                        text.AppendLine($"    - {path}");
                    }
                }

                text.AppendLine();
            }
        }
    }

    private static void WriteSkipped(StringBuilder text, ScanResult result)
    {
        if (result.Skipped.Count == 0)
        {
            return;
        }

        text.AppendLine("Not checked");
        text.AppendLine("-----------");
        text.AppendLine();

        foreach (var skipped in result.Skipped)
        {
            text.AppendLine($"  {skipped.DisplayName}: {skipped.Reason}");
        }

        text.AppendLine();
    }

    /// <summary>
    /// Wraps explanation text so a pasted report stays readable in a chat window,
    /// preserving the author's own line breaks.
    /// </summary>
    private static IEnumerable<string> Wrap(string text, int width)
    {
        foreach (var paragraph in text.Split('\n'))
        {
            if (paragraph.Length <= width)
            {
                yield return paragraph;
                continue;
            }

            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    yield return line.ToString();
                    line.Clear();
                }

                if (line.Length > 0)
                {
                    line.Append(' ');
                }

                line.Append(word);
            }

            if (line.Length > 0)
            {
                yield return line.ToString();
            }
        }
    }
}
