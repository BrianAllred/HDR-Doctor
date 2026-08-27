using HdrDoctor.Core;
using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;

namespace HdrDoctor.Tests;

public class CrashReportTests
{
    private const string HidTitleId = HdrPaths.HidTitleId;

    /// <summary>An album crash, standing in for "something that is not the game".</summary>
    private const string AlbumTitleId = "010000000000100D";

    [Fact]
    public async Task A_card_with_no_crash_reports_reports_nothing_wrong()
    {
        using var sd = new SdFixture().WithHealthyInstall();

        var findings = await sd.RunAsync(new CrashReportCheck(), InstallPlatform.Switch);

        Assert.Empty(findings.Problems());
        Assert.True(findings.Has("No crash reports"));
    }

    [Fact]
    public async Task The_newest_report_is_the_one_read()
    {
        using var sd = new SdFixture().WithHealthyInstall();

        WriteReport(sd, Yesterday, HdrPaths.SmashTitleId, SmashReport("Data Abort (0x101)"));
        WriteReport(sd, Today, HdrPaths.SmashTitleId, SmashReport("Undefined Instruction (0x102)"));

        var findings = await sd.RunAsync(new CrashReportCheck(), InstallPlatform.Switch);

        var finding = findings.Single("Smash crashed");
        Assert.Contains("Undefined Instruction", finding.Detail);
        Assert.DoesNotContain("Data Abort", finding.Explanation);

        // The user should be able to tell how much history is sitting on the card.
        Assert.Contains("2 crash reports", finding.Explanation);
    }

    [Fact]
    public async Task A_smash_crash_is_an_error_and_names_the_file_it_came_from()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        var name = WriteReport(sd, Today, HdrPaths.SmashTitleId, SmashReport("Data Abort (0x101)"));

        var findings = await sd.RunAsync(new CrashReportCheck(), InstallPlatform.Switch);

        var finding = findings.Single("Smash crashed");
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Equal($"{HdrPaths.CrashReportsDir}/{name}", Assert.Single(finding.Paths));

        // The exception type is the part that says what kind of fault it was, so it
        // has to be explained rather than just quoted.
        Assert.Contains("read or wrote memory that is not there", finding.Explanation);
        Assert.Contains("2000-0101", finding.Explanation);
    }

    [Fact]
    public async Task A_break_is_explained_as_the_hardware_face_of_a_plugin_panic()
    {
        // A Rust panic in a Skyline plugin aborts, which reaches Atmosphere as a
        // user break rather than as anything mentioning Rust — so this is the one
        // mapping somebody reading the raw file is least likely to make themselves.
        using var sd = new SdFixture().WithHealthyInstall();
        WriteReport(sd, Today, HdrPaths.SmashTitleId, SmashReport("User Break (0x105)"));

        var findings = await sd.RunAsync(new CrashReportCheck(), InstallPlatform.Switch);

        Assert.Contains("panic", findings.Single("Smash crashed").Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_stack_trace_inside_subsdk9_is_identified_as_skyline()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        WriteReport(sd, Today, HdrPaths.SmashTitleId, SmashReport("Data Abort (0x101)"));

        var findings = await sd.RunAsync(new CrashReportCheck(), InstallPlatform.Switch);

        var finding = findings.Single("Smash crashed");
        Assert.Contains("Skyline or one of the plugins it loaded", finding.Explanation);
        Assert.Contains("subsdk9", finding.Explanation);
    }

    [Fact]
    public async Task A_crash_in_the_games_own_code_is_not_blamed_on_skyline()
    {
        using var sd = new SdFixture().WithHealthyInstall();

        WriteReport(sd, Today, HdrPaths.SmashTitleId, """
            Atmosphere Crash Report (v1.7.1):
            Result:                          2000-0101 (0x14a01)
            Process Info:
                Program Name:                Application
                Program ID:                  01006a800016e000
            Exception Info:
                Type:                        Data Abort (0x101)
                Address:                     0000007a1c0d4e20
            Stack Trace:
                ReturnAddress[00]:           main[0000000002a1b3c4]
            """);

        var findings = await sd.RunAsync(new CrashReportCheck(), InstallPlatform.Switch);

        Assert.DoesNotContain("Skyline", findings.Single("Smash crashed").Explanation);
    }

    [Fact]
    public async Task A_card_whose_only_crashes_are_other_titles_reports_nothing_wrong()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        WriteReport(sd, Today, AlbumTitleId, OtherTitleReport(AlbumTitleId));
        WriteReport(sd, Yesterday, HidTitleId, OtherTitleReport(HidTitleId));

        var findings = await sd.RunAsync(new CrashReportCheck(), InstallPlatform.Switch);

        Assert.Empty(findings.Problems());

        // Saying "no crash reports" over a card holding two of them would be a lie by
        // omission; the reader has to be able to tell what was passed over.
        var finding = findings.Single("No crash reports from Smash");
        Assert.Contains("2 crash reports", finding.Detail);
        Assert.Contains("not read", finding.Detail);
    }

    [Fact]
    public async Task Nothing_from_another_title_reaches_the_report()
    {
        // A crash in some other title says nothing about an HDR install, and reading
        // one only creates the opportunity to explain it as an HDR fault.
        using var sd = new SdFixture().WithHealthyInstall();

        WriteReport(sd, Yesterday, HdrPaths.SmashTitleId, SmashReport("Data Abort (0x101)"));
        WriteReport(sd, Today, AlbumTitleId, OtherTitleReport(AlbumTitleId).Replace("Album", "PhotoViewerMarker"));

        var findings = await sd.RunAsync(new CrashReportCheck(), InstallPlatform.Switch);

        Assert.All(findings, f => Assert.DoesNotContain("PhotoViewerMarker", f.Explanation));
        Assert.All(findings, f => Assert.DoesNotContain(AlbumTitleId.ToLowerInvariant(), f.Explanation));
    }

    [Fact]
    public async Task A_more_recent_crash_elsewhere_does_not_hide_the_smash_one()
    {
        using var sd = new SdFixture().WithHealthyInstall();

        var name = WriteReport(sd, Yesterday, HdrPaths.SmashTitleId, SmashReport("Data Abort (0x101)"));
        WriteReport(sd, Today, AlbumTitleId, OtherTitleReport(AlbumTitleId));

        var findings = await sd.RunAsync(new CrashReportCheck(), InstallPlatform.Switch);

        var smash = findings.Single("Smash crashed earlier on");
        Assert.Equal(Severity.Error, smash.Severity);
        Assert.Equal($"{HdrPaths.CrashReportsDir}/{name}", Assert.Single(smash.Paths));

        // That something newer crashed is worth saying — it comes off the file names
        // and stops this reading as the last thing that happened to the console.
        Assert.Contains("Something else on the console has crashed since", smash.Explanation);
        Assert.Contains("2 crash reports on the card, 1 of them from Smash", smash.Explanation);
    }

    [Fact]
    public async Task Binary_dumps_are_not_mistaken_for_reports()
    {
        using var sd = new SdFixture().WithHealthyInstall();

        sd.WithFile($"{HdrPaths.CrashReportsDir}/dumps/{Stamp(Today)}_{HdrPaths.SmashTitleId.ToLowerInvariant()}.bin", "\0\0\0");
        sd.WithFile($"{HdrPaths.CrashReportsDir}/{Stamp(Today)}_{HdrPaths.SmashTitleId.ToLowerInvariant()}.bin", "\0\0\0");

        var findings = await sd.RunAsync(new CrashReportCheck(), InstallPlatform.Switch);

        Assert.Empty(findings.Problems());
        Assert.True(findings.Has("No crash reports"));
    }

    [Fact]
    public async Task A_console_with_no_clock_set_says_so_rather_than_claiming_1970()
    {
        // Atmosphere writes a zero timestamp when the console has never had its
        // clock set. Printing that as a date would read as a crash from 1970.
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile(
            $"{HdrPaths.CrashReportsDir}/00000000000_{HdrPaths.SmashTitleId.ToLowerInvariant()}.log",
            SmashReport("Data Abort (0x101)"));

        var findings = await sd.RunAsync(new CrashReportCheck(), InstallPlatform.Switch);

        var finding = findings.Single("Smash crashed");
        Assert.Contains("clock had not been set", finding.Detail);
        Assert.DoesNotContain("1970", finding.Detail);
    }

    [Fact]
    public async Task A_truncated_report_still_produces_a_finding()
    {
        // Reports get truncated when the card fills up mid-write. Losing the whole
        // finding over a missing field would throw away the one thing we do know:
        // that the game crashed.
        using var sd = new SdFixture().WithHealthyInstall();
        WriteReport(sd, Today, HdrPaths.SmashTitleId, "Atmosphere Crash Report (v1.7.1):\nResult:  ");

        var findings = await sd.RunAsync(new CrashReportCheck(), InstallPlatform.Switch);

        Assert.Equal(Severity.Error, findings.Single("Smash crashed").Severity);
    }

    [Fact]
    public async Task Crash_reports_are_not_looked_for_under_an_emulator()
    {
        // Emulators do not write these. A Switch's reports copied into an sdmc
        // folder would describe crashes that never happened on this install.
        using var sd = new SdFixture().WithHealthyInstall();
        WriteReport(sd, Today, HdrPaths.SmashTitleId, SmashReport("Data Abort (0x101)"));

        await using var source = sd.Source();
        var skipped = new List<SkippedCheck>();

        var findings = await new CheckRunner([new CrashReportCheck()]).RunAsync(
            new ScanContext
            {
                Source = source,
                Platform = InstallPlatform.Emulator,
                Versions = HdrVersionInfo.Empty,
            },
            skipped,
            CancellationToken.None);

        Assert.Empty(findings);
        Assert.Contains(skipped, s => s.CheckId == "crash-report");
    }

    // ---- Fixtures ------------------------------------------------------------

    private static DateTimeOffset Today => new(2026, 8, 20, 14, 30, 0, TimeSpan.Zero);

    private static DateTimeOffset Yesterday => Today.AddDays(-1);

    /// <summary>The zero-padded seconds Atmosphere puts at the front of a report's name.</summary>
    private static string Stamp(DateTimeOffset when) => when.ToUnixTimeSeconds().ToString("D11");

    /// <summary>Writes a report under its real name, and returns that name.</summary>
    private static string WriteReport(SdFixture sd, DateTimeOffset when, string titleId, string contents)
    {
        var name = $"{Stamp(when)}_{titleId.ToLowerInvariant()}.log";
        sd.WithFile($"{HdrPaths.CrashReportsDir}/{name}", contents);
        return name;
    }

    private static string SmashReport(string exceptionType) => $"""
        Atmosphere Crash Report (v1.7.1):
        Result:                          2000-0101 (0x14a01)
        Process Info:
            Program Name:                Application
            Program ID:                  01006a800016e000
            Process ID:                  0000000000000063
            Process Flags:               0000000f (Is64Bit|AddressSpace64Bit|EnableDebug)
        Exception Info:
            Type:                        {exceptionType}
            Address:                     0000007a1c0d4e20
            Access Address:              0000000000000000
        General Purpose Registers:
            X[00]:                       0000000000000000
            X[01]:                       0000007a1c0d4e20
        Stack Trace:
            ReturnAddress[00]:           subsdk9[00000000000d3f14]
            ReturnAddress[01]:           subsdk9[00000000000c1a08]
            ReturnAddress[02]:           main[0000000002a1b3c4]
        Stack Dump:
            [00000000] 00 01 02 03 04 05 06 07
        """;

    private static string OtherTitleReport(string titleId) => $"""
        Atmosphere Crash Report (v1.7.1):
        Result:                          2000-0101 (0x14a01)
        Process Info:
            Program Name:                Application
            Program ID:                  {titleId.ToLowerInvariant()}
        Exception Info:
            Type:                        Data Abort (0x101)
            Address:                     0000007a1c0d4e20
        Stack Trace:
            ReturnAddress[00]:           main[0000000002a1b3c4]
        """;
}
