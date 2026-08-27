using HdrDoctor.Core;
using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

public class RemediationTests
{
    [Fact]
    public async Task Deleting_a_stale_plugin_actually_removes_it_and_clears_the_finding()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile($"{HdrPaths.PluginsDir}/libhdr.nro", "stale");

        var findings = await sd.RunAsync(new SkylinePluginsCheck(), InstallPlatform.Emulator);
        var stale = findings.Single("libhdr.nro conflicts");

        await using var source = sd.Source();
        var outcomes = await RemediationService
            .ApplyAsync(source, [stale], null, CancellationToken.None);

        Assert.True(outcomes.Single().Succeeded);
        Assert.False(File.Exists(Path.Combine(
            sd.Root, $"{HdrPaths.PluginsDir}/libhdr.nro".Replace('/', Path.DirectorySeparatorChar))));

        // Re-running the check is the real proof: the fix has to satisfy the check
        // that produced it, not merely delete a file.
        var after = await sd.RunAsync(new SkylinePluginsCheck(), InstallPlatform.Emulator);
        Assert.False(after.Has("libhdr.nro conflicts"));
    }

    [Fact]
    public async Task Disabling_a_plugin_moves_it_rather_than_destroying_it()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile($"{HdrPaths.PluginsDir}/libparam_config.nro", "conflict");

        var findings = await sd.RunAsync(new SkylinePluginsCheck(), InstallPlatform.Switch);
        var conflict = findings.Single("libparam_config.nro conflicts");

        await using var source = sd.Source();
        await RemediationService.ApplyAsync(source, [conflict], null, CancellationToken.None);

        var active = Path.Combine(sd.Root, $"{HdrPaths.PluginsDir}/libparam_config.nro".Replace('/', Path.DirectorySeparatorChar));
        var disabled = Path.Combine(sd.Root, $"{HdrPaths.DisabledPluginsDir}/libparam_config.nro".Replace('/', Path.DirectorySeparatorChar));

        Assert.False(File.Exists(active));
        Assert.True(File.Exists(disabled));
        Assert.Equal("conflict", await File.ReadAllTextAsync(disabled));
    }

    [Fact]
    public async Task One_failing_fix_does_not_abandon_the_others()
    {
        // A user who ticked several boxes should not silently lose the rest because
        // the first one hit a locked file.
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile(HdrPaths.DevelopmentNro, "dev");

        var findings = await sd.RunAsync(new SkylinePluginsCheck(), InstallPlatform.Emulator);
        var real = findings.Single("Orphaned development.nro");

        var doomed = new Finding(
            "test", Severity.Error, "Always fails", string.Empty, string.Empty,
            [], new ThrowingRemediation());

        await using var source = sd.Source();
        var outcomes = await RemediationService
            .ApplyAsync(source, [doomed, real], null, CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.False(outcomes[0].Succeeded);
        Assert.Equal("nope", outcomes[0].Error);
        Assert.True(outcomes[1].Succeeded);
    }

    [Fact]
    public void The_plan_puts_destructive_steps_first()
    {
        var safe = new Finding("a", Severity.Error, "Safe", "", "", [],
            new Core.Remediations.DisablePluginRemediation("libx.nro"));

        var destructive = new Finding("b", Severity.Error, "Destructive", "", "", [],
            new Core.Remediations.DeleteFileRemediation("x", "Delete x."));

        var plan = RemediationService.DescribePlan([safe, destructive]);

        Assert.StartsWith("Deletes data", plan);
    }

    [Fact]
    public void An_empty_plan_says_so_rather_than_producing_a_blank_dialog()
    {
        Assert.Equal("Nothing selected.", RemediationService.DescribePlan([]));
    }

    private sealed class ThrowingRemediation : IRemediation
    {
        public string Description => "Always fails.";

        public bool IsDestructive => false;

        public Task ApplyAsync(Core.Sources.IMutableInstallSource source, CancellationToken ct) =>
            throw new IOException("nope");
    }
}
