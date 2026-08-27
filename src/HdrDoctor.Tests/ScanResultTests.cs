using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

/// <summary>
/// Folding the separately-run file verification back into a scan. A report whose
/// files were verified and one where nobody looked must not read alike.
/// </summary>
public class ScanResultTests
{
    [Fact]
    public void An_unverified_scan_says_so_in_the_report()
    {
        var report = ReportWriter.Write(Scan());

        Assert.Contains("Files:         not verified", report);
        Assert.Contains("Not checked", report);
        Assert.Contains("File verification", report);
        Assert.Contains("separate step you start yourself", report);
    }

    [Fact]
    public void Verifying_replaces_the_not_run_note_with_what_was_found()
    {
        var verified = Scan().WithVerification(Verification(
            new Finding(FileVerificationCheck.CheckId, Severity.Error, "2 files do not match the official release",
                "detail", "explanation", ["ultimate/mods/hdr/config.json"])
            {
                Category = CheckCategory.FileVerification,
            }));

        Assert.DoesNotContain(verified.Skipped, s => s.CheckId == FileVerificationCheck.CheckId);
        Assert.True(verified.Findings.Has("do not match the official release"));

        var report = ReportWriter.Write(verified);
        Assert.Contains("Files:         verified against the release", report);
        Assert.Contains("File verification", report);
        Assert.DoesNotContain("separate step you start yourself", report);
    }

    [Fact]
    public void Verifying_twice_replaces_the_earlier_answer_rather_than_stacking_it()
    {
        // Verify, put something right, verify again: the second answer is the true one.
        var once = Scan().WithVerification(Verification(
            new Finding(FileVerificationCheck.CheckId, Severity.Error, "2 files do not match the official release",
                string.Empty, string.Empty, []) { Category = CheckCategory.FileVerification }));

        var twice = once.WithVerification(Verification(
            Finding.Ok(FileVerificationCheck.CheckId, "All 12 files match the official v9.9.9 release")
            with { Category = CheckCategory.FileVerification }));

        Assert.Single(twice.Findings, f => f.CheckId == FileVerificationCheck.CheckId);
        Assert.False(twice.Findings.Has("do not match the official release"));
    }

    [Fact]
    public void A_verification_that_could_not_run_puts_its_reason_back()
    {
        // Silence here would read as "verified, nothing wrong" — the opposite of true.
        var attempted = Scan().WithVerification(new VerificationResult(
            FileVerificationCheck.CheckId,
            [],
            new SkippedCheck(FileVerificationCheck.CheckId, FileVerificationCheck.Name,
                "Could not download the file list for v9.9.9: the network is unreachable."),
            TimeSpan.FromSeconds(1)));

        Assert.Single(attempted.Skipped);
        Assert.Null(attempted.VerifiedAt);

        var report = ReportWriter.Write(attempted);
        Assert.Contains("Files:         not verified", report);
        Assert.Contains("network is unreachable", report);
    }

    [Fact]
    public void Verification_findings_are_ordered_with_the_rest_of_the_scan()
    {
        var verified = Scan().WithVerification(Verification(
            new Finding(FileVerificationCheck.CheckId, Severity.Critical, "9 files missing from the HDR folders",
                string.Empty, string.Empty, []) { Category = CheckCategory.FileVerification }));

        Assert.Equal(Severity.Critical, verified.Findings[0].Severity);
        Assert.Equal(Severity.Critical, verified.WorstSeverity);
    }

    private static VerificationResult Verification(params Finding[] findings) =>
        new(FileVerificationCheck.CheckId, findings, null, TimeSpan.FromMinutes(2));

    /// <summary>A scan as ScanService produces one: findings, plus a note that the files were not verified.</summary>
    private static ScanResult Scan() => new(
        [
            new Finding("plugins", Severity.Warning, "Something is a bit wrong", "detail", "why", [])
            {
                Category = CheckCategory.SkylinePlugins,
            },
        ],
        [
            new SkippedCheck(FileVerificationCheck.CheckId, FileVerificationCheck.Name,
                "Not run. Checking every file against the release's published hashes reads the whole "
                + "install, so it is a separate step you start yourself."),
        ],
        new EnvironmentSummary
        {
            SourceDescription = "/home/user/sdmc",
            SourceKind = "Local folder",
            Platform = InstallPlatform.Emulator,
            HdrVersion = "v9.9.9-prerelease",
            Channel = ReleaseChannel.PreRelease,
            FileListAvailable = true,
        },
        DateTimeOffset.Now,
        TimeSpan.FromSeconds(4));
}
