using HdrDoctor.Core;
using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;

namespace HdrDoctor.Tests;

public class ThirdPartyModTests
{
    [Fact]
    public async Task A_mod_with_its_own_plugin_is_a_code_mod()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile($"{HdrPaths.ModsDir}/Knuckles Moveset Lite/plugin.nro", "code")
            .WithFile($"{HdrPaths.ModsDir}/Knuckles Moveset Lite/fighter/sonic/model/body/c00/model.numdlb", "m");

        var findings = await sd.RunAsync(new ThirdPartyModCheck());

        var finding = findings.Single("Custom moveset mod");
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains("Knuckles Moveset Lite", finding.Detail);
    }

    [Fact]
    public async Task Per_costume_physics_files_do_not_make_a_skin_pack_a_gameplay_mod()
    {
        // Skin packs ship swing.prc for cloth and hair on alternate models. Treating
        // those as a desync risk would make this warning fire on nearly every mod
        // anyone installs, which is how a troubleshooting tool gets ignored.
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile($"{HdrPaths.ModsDir}/Skins/fighter/link/motion/body/c08/swing.prc", "physics")
            .WithFile($"{HdrPaths.ModsDir}/Skins/fighter/link/motion/body/c08/update.prc", "physics")
            .WithFile($"{HdrPaths.ModsDir}/Skins/fighter/link/model/body/c08/model.numdlb", "model")
            .WithFile($"{HdrPaths.ModsDir}/Skins/ui/replace/chara/chara_1/chara_1_link_08.bntx", "icon");

        var findings = await sd.RunAsync(new ThirdPartyModCheck());

        Assert.Equal(Severity.Info, findings.Single("cosmetic mod").Severity);
        Assert.False(findings.Has("Gameplay-affecting"));
    }

    [Fact]
    public async Task Replacing_shared_fighter_params_is_a_gameplay_mod()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile($"{HdrPaths.ModsDir}/Rebalance/fighter/common/param/effect.prc", "params");

        var findings = await sd.RunAsync(new ThirdPartyModCheck());

        var finding = findings.Single("Gameplay-affecting");
        Assert.Equal(Severity.Warning, finding.Severity);

        // The verdict has to be checkable, not just asserted.
        Assert.Contains("fighter/common/param/effect.prc", finding.Explanation);
    }

    [Fact]
    public async Task Replacing_a_motion_list_is_a_gameplay_mod()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile($"{HdrPaths.ModsDir}/Anim/fighter/link/motion/body/c08/motion_list.bin", "motion");

        var findings = await sd.RunAsync(new ThirdPartyModCheck());

        Assert.True(findings.Has("Gameplay-affecting"));
    }

    [Fact]
    public async Task Hdr_own_folders_are_never_treated_as_third_party()
    {
        using var sd = new SdFixture().WithHealthyInstall();

        var findings = await sd.RunAsync(new ThirdPartyModCheck());

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Removal_advice_matches_the_platform()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile($"{HdrPaths.ModsDir}/Moveset/plugin.nro", "code");

        var onSwitch = await sd.RunAsync(new ThirdPartyModCheck(), InstallPlatform.Switch);
        Assert.Contains("mod manager", onSwitch.Single("Custom moveset").Explanation);

        var onEmulator = await sd.RunAsync(new ThirdPartyModCheck(), InstallPlatform.Emulator);
        Assert.Contains("has to be removed", onEmulator.Single("Custom moveset").Explanation);
    }
}
