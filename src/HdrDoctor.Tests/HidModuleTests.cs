using HdrDoctor.Core;
using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;

namespace HdrDoctor.Tests;

public class HidModuleTests
{
    [Fact]
    public async Task A_populated_hid_module_folder_is_critical()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile($"{HdrPaths.HidExefs}/subsdk0", "hid");

        var findings = await sd.RunAsync(new HidModuleCheck(), InstallPlatform.Switch);

        var finding = findings.Single("old HID module patch is installed");
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.True(finding.CanBeFixed);
    }

    [Fact]
    public async Task An_empty_hid_module_folder_is_only_noted()
    {
        // This is what removing the module leaves behind. Reporting it as broken
        // would send users chasing a non-problem.
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithDirectory(HdrPaths.HidExefs);

        var findings = await sd.RunAsync(new HidModuleCheck(), InstallPlatform.Switch);

        Assert.Equal(Severity.Info, findings.Single("Empty HID module folder").Severity);
    }

    [Fact]
    public async Task No_hid_folder_at_all_passes()
    {
        using var sd = new SdFixture().WithHealthyInstall();

        var findings = await sd.RunAsync(new HidModuleCheck(), InstallPlatform.Switch);

        Assert.Empty(findings.Problems());
    }
}
