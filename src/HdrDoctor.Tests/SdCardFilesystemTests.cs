using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;

namespace HdrDoctor.Tests;

public class SdCardFilesystemTests
{
    private static async Task<IReadOnlyList<Finding>> RunAsync(
        string? localRoot,
        string? format,
        InstallPlatform platform = InstallPlatform.Switch)
    {
        using var sd = new SdFixture().WithHealthyInstall(platform);
        return await sd.RunAsync(new SdCardFilesystemCheck(localRoot, _ => format), platform);
    }

    [Theory]
    [InlineData("vfat")]    // Linux
    [InlineData("FAT32")]   // Windows
    [InlineData("msdos")]   // macOS
    public async Task Fat32_card_passes(string format)
    {
        var findings = await RunAsync("/mnt/sd", format);

        Assert.Equal(Severity.Ok, findings.Single("FAT32").Severity);
    }

    [Theory]
    [InlineData("exfat")]
    [InlineData("exFAT")]
    public async Task ExFat_card_is_critical(string format)
    {
        var findings = await RunAsync("/mnt/sd", format);

        Assert.Equal(Severity.Critical, findings.Single("exFAT").Severity);
    }

    /// <summary>
    /// "exfat" contains "fat", so a substring match would pass the one format this
    /// check exists to catch.
    /// </summary>
    [Fact]
    public async Task ExFat_is_not_mistaken_for_fat()
    {
        var findings = await RunAsync("/mnt/sd", "exfat");

        Assert.DoesNotContain(findings, f => f.Severity == Severity.Ok);
    }

    /// <summary>
    /// A Switch profile pointing at a folder on the PC: the card it will be copied to
    /// is the one that has to be FAT32, and this scan has not seen it.
    /// </summary>
    [Fact]
    public async Task Folder_on_a_pc_volume_is_reported_as_not_a_card()
    {
        var findings = await RunAsync("/home/brian/staging", "btrfs");

        Assert.Equal(Severity.Info, findings.Single("not a Switch SD card").Severity);
    }

    /// <summary>
    /// Null root is how ScanService says "FTP, or an FTP mount" — nothing local
    /// describes the console's card. Silence there would read as a card that passed.
    /// </summary>
    [Fact]
    public async Task Ftp_reports_that_nothing_was_checked()
    {
        var findings = await RunAsync(localRoot: null, format: null);

        var finding = findings.Single("was not checked");
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Contains("FTP", finding.Detail);
    }

    [Fact]
    public async Task Unreadable_drive_reports_that_nothing_was_checked()
    {
        var findings = await RunAsync("/mnt/sd", format: null);

        Assert.Equal(Severity.Info, findings.Single("was not checked").Severity);
    }

    [Fact]
    public async Task Findings_are_filed_under_the_sd_card_category()
    {
        var findings = await RunAsync("/mnt/sd", "exfat");

        Assert.Equal(CheckCategory.SdCard, findings.Single("exFAT").Category);
    }
}
