using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;

namespace HdrDoctor.Tests;

public class LauncherConfigTests
{
    [Fact]
    public async Task A_launcher_pointed_at_this_folder_passes()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        using var paths = new FakeAppPaths();

        var emulator = Path.Combine(paths.Home, "emulator.AppImage");
        File.WriteAllText(emulator, "");
        paths.WriteLauncherConfig(emulator, sd.Root);

        var findings = await sd.RunAsync(new LauncherConfigCheck(paths));

        Assert.Empty(findings.Problems());
    }

    [Fact]
    public async Task A_launcher_installing_somewhere_else_is_warned_about()
    {
        // The "I updated but nothing changed" case: the launcher reports success while
        // writing into a folder the emulator never reads.
        using var sd = new SdFixture().WithHealthyInstall();
        using var elsewhere = new SdFixture().WithHealthyInstall();
        using var paths = new FakeAppPaths();

        var emulator = Path.Combine(paths.Home, "emulator.AppImage");
        File.WriteAllText(emulator, "");
        paths.WriteLauncherConfig(emulator, elsewhere.Root);

        var findings = await sd.RunAsync(new LauncherConfigCheck(paths));

        var finding = findings.Single("different folder than the one just scanned");
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains(elsewhere.Root, finding.Detail);
    }

    [Fact]
    public async Task A_missing_emulator_executable_is_an_error()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        using var paths = new FakeAppPaths();

        paths.WriteLauncherConfig(Path.Combine(paths.Home, "gone.AppImage"), sd.Root);

        var findings = await sd.RunAsync(new LauncherConfigCheck(paths));

        Assert.Equal(Severity.Error, findings.Single("emulator that no longer exists").Severity);
    }

    [Fact]
    public async Task An_empty_sdcard_path_is_an_error()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        using var paths = new FakeAppPaths();

        var emulator = Path.Combine(paths.Home, "emulator.AppImage");
        File.WriteAllText(emulator, "");
        paths.WriteLauncherConfig(emulator, null);

        var findings = await sd.RunAsync(new LauncherConfigCheck(paths));

        Assert.Equal(Severity.Error, findings.Single("no SD folder set").Severity);
    }

    [Fact]
    public async Task A_corrupt_config_is_reported_rather_than_crashing_the_scan()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        using var paths = new FakeAppPaths();

        var directory = Path.Combine(paths.ConfigHome, "hdr-launcher");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "launcher-config.json"), "{ not json");

        var findings = await sd.RunAsync(new LauncherConfigCheck(paths));

        Assert.Equal(Severity.Error, findings.Single("unreadable").Severity);
    }

    [Fact]
    public async Task No_launcher_config_is_merely_informational()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        using var paths = new FakeAppPaths();

        var findings = await sd.RunAsync(new LauncherConfigCheck(paths));

        Assert.Equal(Severity.Info, findings.Single("not found").Severity);
    }

    [Fact]
    public async Task The_launcher_check_never_runs_against_a_switch()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        using var paths = new FakeAppPaths();

        var check = new LauncherConfigCheck(paths);

        Assert.Equal(PlatformScope.EmulatorOnly, check.Scope);
        Assert.False(check.Scope.AppliesTo(InstallPlatform.Switch));
    }
}
