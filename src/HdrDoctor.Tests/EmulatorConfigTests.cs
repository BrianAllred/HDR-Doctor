using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

public class EmulatorConfigTests
{
    private const string GlobalIni = """
        [System]
        rng_seed_enabled\default=false
        rng_seed_enabled=true
        rng_seed\default=true
        rng_seed=0

        [Renderer]
        backend=1
        use_vsync=0
        force_max_clock=false
        use_asynchronous_shaders=false
        async_presentation=false
        gpu_accuracy=1

        [Core]
        memory_layout_mode=2

        [Data%20Storage]
        sdmc_directory=/home/user/.local/share/eden/sdmc
        """;

    [Fact]
    public void Percent_encoded_section_names_are_decoded()
    {
        var ini = IniFile.Parse(GlobalIni);

        Assert.Equal("/home/user/.local/share/eden/sdmc", ini.GetRaw("Data Storage", "sdmc_directory"));
    }

    [Fact]
    public async Task Per_game_value_wins_when_use_global_is_false()
    {
        using var files = new TempConfigFiles(GlobalIni, """
            [Renderer]
            use_vsync\use_global=false
            use_vsync=2
            """);

        var settings = await YuzuSettings.LoadAsync(files.Global, files.PerGame, CancellationToken.None);
        var vsync = settings.Resolve("Renderer", "use_vsync");

        Assert.Equal(2, vsync.AsInt());
        Assert.Equal(SettingOrigin.PerGame, vsync.Origin);
    }

    [Fact]
    public async Task Global_value_wins_when_the_per_game_entry_defers()
    {
        using var files = new TempConfigFiles(GlobalIni, """
            [Renderer]
            use_vsync\use_global=true
            use_vsync=2
            """);

        var settings = await YuzuSettings.LoadAsync(files.Global, files.PerGame, CancellationToken.None);
        var vsync = settings.Resolve("Renderer", "use_vsync");

        Assert.Equal(0, vsync.AsInt());
        Assert.Equal(SettingOrigin.Global, vsync.Origin);
    }

    [Fact]
    public async Task A_missing_use_global_flag_means_defer_to_global()
    {
        using var files = new TempConfigFiles(GlobalIni, """
            [Renderer]
            use_vsync=2
            """);

        var settings = await YuzuSettings.LoadAsync(files.Global, files.PerGame, CancellationToken.None);

        Assert.Equal(SettingOrigin.Global, settings.Resolve("Renderer", "use_vsync").Origin);
    }

    [Fact]
    public async Task A_correct_yuzu_config_produces_no_problems()
    {
        using var files = new TempConfigFiles(GlobalIni, null);
        using var sd = new SdFixture().WithHealthyInstall();

        var findings = await sd.RunAsync(
            new EmulatorConfigCheck(),
            InstallPlatform.Emulator,
            emulator: files.AsEmulator(sd.Root));

        // The memory-layout note is advisory by design and always present.
        Assert.All(findings.Problems(), f => Assert.Contains("Memory layout", f.Title));
    }

    [Fact]
    public async Task Rng_seed_disabled_is_critical_because_mods_misdetect_the_platform()
    {
        using var files = new TempConfigFiles(
            GlobalIni.Replace("rng_seed_enabled=true", "rng_seed_enabled=false"),
            null);
        using var sd = new SdFixture().WithHealthyInstall();

        var findings = await sd.RunAsync(
            new EmulatorConfigCheck(),
            InstallPlatform.Emulator,
            emulator: files.AsEmulator(sd.Root));

        var finding = findings.Single("RNG seed is not enabled");
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("loaded in memory", finding.Explanation);
    }

    [Fact]
    public async Task A_per_game_override_can_break_an_otherwise_correct_global_config()
    {
        // The whole point of resolving overrides: the global settings look right, and
        // the user is still broken because the per-game profile disagrees.
        using var files = new TempConfigFiles(GlobalIni, """
            [System]
            rng_seed_enabled\use_global=false
            rng_seed_enabled=false
            """);
        using var sd = new SdFixture().WithHealthyInstall();

        var findings = await sd.RunAsync(
            new EmulatorConfigCheck(),
            InstallPlatform.Emulator,
            emulator: files.AsEmulator(sd.Root));

        var finding = findings.Single("RNG seed is not enabled");
        Assert.Contains("per-game settings", finding.Detail);
    }

    [Fact]
    public async Task Vsync_and_opengl_are_flagged()
    {
        using var files = new TempConfigFiles(
            GlobalIni.Replace("use_vsync=0", "use_vsync=2").Replace("backend=1", "backend=0"),
            null);
        using var sd = new SdFixture().WithHealthyInstall();

        var findings = await sd.RunAsync(
            new EmulatorConfigCheck(),
            InstallPlatform.Emulator,
            emulator: files.AsEmulator(sd.Root));

        Assert.Equal(Severity.Warning, findings.Single("VSync is enabled").Severity);
        Assert.Equal(Severity.Warning, findings.Single("backend is OpenGL").Severity);
    }

    [Fact]
    public async Task Ryujinx_pptc_enabled_is_an_error()
    {
        using var files = new TempConfigFiles(null, null, ryujinx: """
            {"enable_ptc": true, "graphics_backend": "Vulkan"}
            """);
        using var sd = new SdFixture().WithHealthyInstall();

        var findings = await sd.RunAsync(
            new EmulatorConfigCheck(),
            InstallPlatform.Emulator,
            emulator: files.AsRyujinx(sd.Root));

        var finding = findings.Single("PPTC is enabled");
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains("rewriting that code in memory", finding.Explanation);
    }

    [Fact]
    public async Task Ryujinx_pptc_disabled_passes()
    {
        using var files = new TempConfigFiles(null, null, ryujinx: """
            {"enable_ptc": false, "enable_low_power_ptc": false, "graphics_backend": "Vulkan"}
            """);
        using var sd = new SdFixture().WithHealthyInstall();

        var findings = await sd.RunAsync(
            new EmulatorConfigCheck(),
            InstallPlatform.Emulator,
            emulator: files.AsRyujinx(sd.Root));

        Assert.DoesNotContain(findings.Problems(), f => f.Title.Contains("PPTC"));
    }

    private sealed class TempConfigFiles : IDisposable
    {
        private readonly string _directory;

        public TempConfigFiles(string? global, string? perGame, string? ryujinx = null)
        {
            _directory = Path.Combine(Path.GetTempPath(), "hdr-cfg-tests", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(_directory);

            if (global is not null)
            {
                Global = Path.Combine(_directory, "qt-config.ini");
                File.WriteAllText(Global, global);
            }

            if (perGame is not null)
            {
                PerGame = Path.Combine(_directory, "per-game.ini");
                File.WriteAllText(PerGame, perGame);
            }

            if (ryujinx is not null)
            {
                Ryujinx = Path.Combine(_directory, "Config.json");
                File.WriteAllText(Ryujinx, ryujinx);
            }
        }

        public string? Global { get; }

        public string? PerGame { get; }

        public string? Ryujinx { get; }

        public EmulatorInstallation AsEmulator(string sdmc) =>
            new(EmulatorFamily.YuzuFamily, "eden", sdmc, Global, PerGame);

        public EmulatorInstallation AsRyujinx(string sdmc) =>
            new(EmulatorFamily.Ryujinx, "Ryujinx", sdmc, Ryujinx, null);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
