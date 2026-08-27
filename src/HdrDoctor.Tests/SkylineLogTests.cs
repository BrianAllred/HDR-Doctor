using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

public class SkylineLogTests
{
    /// <summary>The ANSI escape character. Skyline colors parts of its output.</summary>
    private static readonly string Esc = ((char)27).ToString();

    [Fact]
    public async Task A_panic_is_reported_as_critical_with_surrounding_context()
    {
        using var log = new TempLog("""
            adding file: sd:/ultimate/mods/hdr/plugin.nro
            [hdr] installing hooks
            thread '<unnamed>' panicked at 'called `Option::unwrap()` on a `None` value'
            note: run with `RUST_BACKTRACE=1`
            """);

        var findings = await SkylineLogParser.ParseAsync(log.Path, CancellationToken.None);

        var finding = findings.Single("plugin crashed");
        Assert.Equal(Severity.Critical, finding.Severity);

        // The lines around the panic are what identify which plugin died, so they
        // have to survive into the report.
        Assert.Contains("installing hooks", finding.Explanation);
    }

    [Fact]
    public async Task A_param_mismatch_is_explained_as_a_version_mismatch()
    {
        using var log = new TempLog(
            "Could not retrieve Agent ParamModule string: some_new_param");

        var findings = await SkylineLogParser.ParseAsync(log.Path, CancellationToken.None);

        var finding = findings.Single("out of sync");
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("different versions", finding.Explanation);
    }

    [Fact]
    public async Task The_arcropolis_hashes_message_is_correctly_treated_as_cosmetic()
    {
        // This appears in every healthy log. Reporting it as a problem would send
        // people chasing it instead of the real fault.
        using var log = new TempLog(
            "[arcropolis::hashes] Failed to read 'sd:/ultimate/arcropolis/hashes.txt' for hashes.");

        var findings = await SkylineLogParser.ParseAsync(log.Path, CancellationToken.None);

        Assert.Equal(Severity.Info, findings.Single("hash list not present").Severity);
    }

    [Fact]
    public async Task Ansi_color_codes_are_stripped_from_reported_lines()
    {
        using var log = new TempLog(
            $"[arcropolis::fs] Failed to patch '{Esc}[93mui/stage.prc{Esc}[39m' filesize!");

        var findings = await SkylineLogParser.ParseAsync(log.Path, CancellationToken.None);

        var finding = findings.Single("could not apply some file replacements");
        Assert.False(finding.Detail.Contains(Esc), "escape codes should be stripped from the report");
        Assert.Contains("ui/stage.prc", finding.Detail);
    }

    [Fact]
    public async Task Only_the_most_recent_occurrence_of_a_problem_is_reported()
    {
        using var log = new TempLog("""
            hdr-assets is not enabled! Please enable hdr-assets in arcropolis config.
            ... later run ...
            hdr-assets is not enabled! Please enable hdr-assets in arcropolis config.
            """);

        var findings = await SkylineLogParser.ParseAsync(log.Path, CancellationToken.None);

        Assert.Single(findings, f => f.Title.Contains("not switched on"));
    }

    [Fact]
    public async Task A_clean_log_reports_nothing_wrong()
    {
        using var log = new TempLog("""
            adding file: sd:/ultimate/mods/hdr/plugin.nro
            [hdr] version v9.9.9-prerelease
            """);

        var findings = await SkylineLogParser.ParseAsync(log.Path, CancellationToken.None);

        Assert.Empty(findings.Problems());
    }

    private sealed class TempLog : IDisposable
    {
        public TempLog(string contents)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"skyline-{Guid.NewGuid():n}.log");
            File.WriteAllText(Path, contents);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
