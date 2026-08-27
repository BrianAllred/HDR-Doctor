using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

public class ReportWriterTests
{
    [Fact]
    public void The_report_leads_with_what_was_scanned()
    {
        // Somebody reading this cold in a support channel needs the environment
        // before the findings, or the findings mean nothing.
        var report = ReportWriter.Write(Result());

        var header = report[..report.IndexOf("Summary", StringComparison.Ordinal)];
        Assert.Contains("/home/user/sdmc", header);
        Assert.Contains("v9.9.9-prerelease", header);
        Assert.Contains("Emulator", header);
    }

    [Fact]
    public void Passing_checks_are_hidden_unless_asked_for()
    {
        var report = ReportWriter.Write(Result());
        Assert.DoesNotContain("Everything is fine", report);

        var verbose = ReportWriter.Write(Result(), includePassing: true);
        Assert.Contains("Everything is fine", verbose);
    }

    [Fact]
    public void Skipped_checks_are_stated_so_their_silence_is_not_mistaken_for_a_pass()
    {
        var report = ReportWriter.Write(Result());

        Assert.Contains("Not checked", report);
        Assert.Contains("Only applies to Switch installs.", report);
    }

    [Fact]
    public void Findings_are_grouped_and_ordered_worst_first()
    {
        var report = ReportWriter.Write(Result());

        Assert.Contains("Skyline plugins", report);
        Assert.True(
            report.IndexOf("[CRITICAL]", StringComparison.Ordinal)
            < report.IndexOf("[WARNING]", StringComparison.Ordinal));
    }

    [Fact]
    public void A_clean_install_says_so_plainly()
    {
        var clean = new ScanResult(
            [Finding.Ok("x", "Everything is fine")],
            [],
            Summary(),
            DateTimeOffset.Now,
            TimeSpan.FromSeconds(3));

        var report = ReportWriter.Write(clean);

        Assert.Contains("No problems found", report);
        Assert.Contains("this installation looks correct", report);
    }

    private static ScanResult Result() => new(
        [
            new Finding("a", Severity.Critical, "Something is very wrong", "detail here",
                "This is why it matters.", ["ultimate/mods/hdr"]) { Category = CheckCategory.SkylinePlugins },
            new Finding("b", Severity.Warning, "Something is a bit wrong", "more detail",
                "Explanation.", []) { Category = CheckCategory.SkylinePlugins },
            Finding.Ok("c", "Everything is fine") with { Category = CheckCategory.StageAlts },
        ],
        [new SkippedCheck("hid", "HID module", "Only applies to Switch installs.")],
        Summary(),
        DateTimeOffset.Now,
        TimeSpan.FromSeconds(12));

    private static EnvironmentSummary Summary() => new()
    {
        SourceDescription = "/home/user/sdmc",
        SourceKind = "Local folder",
        Platform = InstallPlatform.Emulator,
        HdrVersion = "v9.9.9-prerelease",
        AssetsVersion = "v8.8.8",
        Channel = ReleaseChannel.PreRelease,
        EmulatorName = "eden",
        FixesAvailable = true,
    };
}
