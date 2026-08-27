using HdrDoctor.Core;
using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

public class SkylinePluginsCheckTests
{
    [Fact]
    public async Task Healthy_install_reports_no_problems()
    {
        using var sd = new SdFixture().WithHealthyInstall();

        var findings = await sd.RunAsync(new SkylinePluginsCheck());

        Assert.Empty(findings.Problems());
    }

    [Fact]
    public async Task Missing_required_plugin_is_critical_when_the_mod_checks_for_it_too()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        DeletePlugin(sd, "libarcropolis.nro");

        var findings = await sd.RunAsync(new SkylinePluginsCheck());

        Assert.Equal(Severity.Critical, findings.Single("libarcropolis.nro is missing").Severity);
    }

    [Fact]
    public async Task A_plugin_the_release_does_not_ship_is_not_reported_as_missing()
    {
        // The required-plugin list is a snapshot of what HDR ships today. A future
        // release that drops one of them is still a valid install, and the manifest
        // published with that release is what says so.
        using var sd = new SdFixture().WithHealthyInstall();
        DeletePlugin(sd, "libstage_alts.nro");

        var manifest = ManifestListingPlugins(
            [.. HdrPaths.RequiredPlugins
                .Select(p => p.FileName)
                .Where(n => n != "libstage_alts.nro")]);

        var findings = await sd.RunAsync(new SkylinePluginsCheck(), manifest: manifest);

        Assert.False(findings.Has("libstage_alts.nro is missing"));
        Assert.Empty(findings.Problems());
    }

    [Fact]
    public async Task A_plugin_the_release_does_ship_is_still_reported_when_it_is_missing()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        DeletePlugin(sd, "libarcropolis.nro");

        var manifest = ManifestListingPlugins([.. HdrPaths.RequiredPlugins.Select(p => p.FileName)]);

        var findings = await sd.RunAsync(new SkylinePluginsCheck(), manifest: manifest);

        Assert.Equal(Severity.Critical, findings.Single("libarcropolis.nro is missing").Severity);
    }

    [Fact]
    public async Task A_manifest_that_says_nothing_about_plugins_leaves_the_built_in_list_in_charge()
    {
        // Reading a manifest that only covers ultimate/mods as "this release ships no
        // plugins" would silence every finding in this check at once.
        using var sd = new SdFixture().WithHealthyInstall();
        DeletePlugin(sd, "libarcropolis.nro");

        var manifest = ReleaseManifest.Parse(
            """[{"path": "/ultimate/mods/hdr/plugin.nro", "hash": "abc123"}]""",
            "v9.9.9");

        var findings = await sd.RunAsync(new SkylinePluginsCheck(), manifest: manifest);

        Assert.Equal(Severity.Critical, findings.Single("libarcropolis.nro is missing").Severity);
    }

    [Fact]
    public async Task Stale_libhdr_is_critical_and_offers_a_fix()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile($"{HdrPaths.PluginsDir}/libhdr.nro", "stale");

        var findings = await sd.RunAsync(new SkylinePluginsCheck());

        var finding = findings.Single("libhdr.nro conflicts");
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.True(finding.CanBeFixed);
    }

    [Fact]
    public async Task Orphaned_development_nro_is_an_error()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile(HdrPaths.DevelopmentNro, "dev");

        var findings = await sd.RunAsync(new SkylinePluginsCheck());

        var finding = findings.Single("Orphaned development.nro");
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.True(finding.CanBeFixed);
    }

    [Fact]
    public async Task Development_nro_paired_with_hdr_dev_is_only_a_warning()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile(HdrPaths.DevelopmentNro, "dev")
            .WithDirectory(HdrPaths.HdrDevDir);

        var findings = await sd.RunAsync(new SkylinePluginsCheck());

        Assert.False(findings.Has("Orphaned"));
        Assert.Equal(Severity.Warning, findings.Single("development build is installed").Severity);
    }

    [Fact]
    public async Task Param_config_is_disabled_rather_than_deleted_on_switch()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile($"{HdrPaths.PluginsDir}/libparam_config.nro", "conflict");

        var findings = await sd.RunAsync(new SkylinePluginsCheck(), InstallPlatform.Switch);

        var finding = findings.Single("libparam_config.nro conflicts");
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.False(finding.Remediation!.IsDestructive);
        Assert.Contains("disabled_plugins", finding.Remediation.Description);
    }

    [Fact]
    public async Task Param_config_must_be_deleted_on_emulator()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall(InstallPlatform.Emulator)
            .WithFile($"{HdrPaths.PluginsDir}/libparam_config.nro", "conflict");

        var findings = await sd.RunAsync(new SkylinePluginsCheck(), InstallPlatform.Emulator);

        var finding = findings.Single("libparam_config.nro conflicts");
        Assert.True(finding.Remediation!.IsDestructive);
    }

    [Fact]
    public async Task Local_latency_slider_must_be_deleted_on_emulator()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall(InstallPlatform.Emulator)
            .WithFile($"{HdrPaths.PluginsDir}/liblocal_latency_slider.nro", "conflict");

        var findings = await sd.RunAsync(new SkylinePluginsCheck(), InstallPlatform.Emulator);

        var finding = findings.Single("liblocal_latency_slider.nro is deprecated");
        Assert.True(finding.Remediation!.IsDestructive);
    }

    [Fact]
    public async Task Param_config_already_in_disabled_plugins_is_accepted()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile($"{HdrPaths.DisabledPluginsDir}/libparam_config.nro", "disabled");

        var findings = await sd.RunAsync(new SkylinePluginsCheck(), InstallPlatform.Switch);

        Assert.Empty(findings.Problems());
        Assert.True(findings.Has("present but disabled"));
    }

    [Fact]
    public async Task Launcher_nro_is_fine_on_switch()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile($"{HdrPaths.PluginsDir}/{HdrPaths.LauncherNro}", "launcher");

        var onSwitch = await sd.RunAsync(new SkylinePluginsCheck(), InstallPlatform.Switch);
        Assert.Empty(onSwitch.Problems());
    }

    [Fact]
    public async Task Unknown_plugins_are_only_mentioned_in_passing()
    {
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFile($"{HdrPaths.PluginsDir}/libsomething_else.nro", "other");

        var findings = await sd.RunAsync(new SkylinePluginsCheck());

        Assert.Equal(Severity.Info, findings.Single("non-HDR plugin").Severity);
    }

    private static void DeletePlugin(SdFixture sd, string fileName) =>
        File.Delete(Path.Combine(
            sd.Root,
            $"{HdrPaths.PluginsDir}/{fileName}".Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// A manifest that describes the plugins folder, i.e. one where the release
    /// itself decides which plugins have to be present. Hashes are irrelevant here —
    /// this check only ever asks whether a path is listed.
    /// </summary>
    private static ReleaseManifest ManifestListingPlugins(string[] fileNames)
    {
        var entries = fileNames.Select(name =>
            $$"""{"path": "/{{HdrPaths.PluginsDir}}/{{name}}", "hash": "abc123"}""");

        return ReleaseManifest.Parse($"[{string.Join(",", entries)}]", "v9.9.9");
    }
}
