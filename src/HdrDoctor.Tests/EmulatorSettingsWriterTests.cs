using System.Text.Json;
using System.Text.Json.Nodes;
using HdrDoctor.Core;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

public class IniWriteTests
{
    private const string Global = """
        [Renderer]
        # a comment the writer must not eat
        backend\default=true
        backend=1
        use_vsync\default=true
        use_vsync=1

        [Data%20Storage]
        sdmc_directory=/home/user/.local/share/eden/sdmc
        """;

    [Fact]
    public void Writing_a_value_also_clears_the_default_flag()
    {
        var result = IniFile.SetValue(Global, "Renderer", "use_vsync", "0");

        Assert.Contains("use_vsync=0", result);
        Assert.Contains("use_vsync\\default=false", result);

        // Leaving \default=true lets the emulator's next save put its own default back.
        Assert.DoesNotContain("use_vsync\\default=true", result);
    }

    [Fact]
    public void Unrelated_lines_survive_untouched()
    {
        var result = IniFile.SetValue(Global, "Renderer", "use_vsync", "0");

        Assert.Contains("# a comment the writer must not eat", result);
        Assert.Contains("backend=1", result);
        Assert.Contains("backend\\default=true", result);
        Assert.Contains("sdmc_directory=/home/user/.local/share/eden/sdmc", result);
    }

    [Fact]
    public void A_percent_encoded_section_name_is_matched()
    {
        var result = IniFile.SetValue(Global, "Data Storage", "sdmc_directory", "/tmp/sd");

        Assert.Contains("sdmc_directory=/tmp/sd", result);
        Assert.Equal("/tmp/sd", IniFile.Parse(result).GetRaw("Data Storage", "sdmc_directory"));
    }

    /// <summary>
    /// The case that matters: a config with no rng_seed_enabled at all is what the
    /// scan calls Critical, so the fix has to be able to add the key.
    /// </summary>
    [Fact]
    public void A_key_missing_from_an_existing_section_is_inserted()
    {
        const string content = """
            [System]
            language_index=1
            """;

        var result = IniFile.SetValue(content, "System", "rng_seed_enabled", "true");
        var parsed = IniFile.Parse(result);

        Assert.Equal("true", parsed.GetRaw("System", "rng_seed_enabled"));
        Assert.Equal("false", parsed.GetRaw("System", "rng_seed_enabled\\default"));
        Assert.Equal("1", parsed.GetRaw("System", "language_index"));
    }

    [Fact]
    public void A_missing_section_is_appended()
    {
        var result = IniFile.SetValue(Global, "System", "rng_seed_enabled", "true");
        var parsed = IniFile.Parse(result);

        Assert.Equal("true", parsed.GetRaw("System", "rng_seed_enabled"));
        Assert.Equal("1", parsed.GetRaw("Renderer", "backend"));
    }

    [Fact]
    public void A_crlf_file_stays_crlf()
    {
        var result = IniFile.SetValue(Global.ReplaceLineEndings("\r\n"), "Renderer", "use_vsync", "0");

        Assert.Contains("use_vsync=0\r\n", result);

        // Every newline should still be a pair; a lone LF means the file got mixed endings.
        Assert.DoesNotContain("\n", result.Replace("\r\n", string.Empty));
    }
}

public class EmulatorSettingsWriterTests
{
    private const string HealthyGlobal = """
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
        async_presentation=true
        gpu_accuracy=1

        [Core]
        memory_layout_mode=2
        """;

    [Fact]
    public async Task An_already_optimal_config_plans_nothing()
    {
        using var files = new TempEmulator(HealthyGlobal);

        var plan = await EmulatorSettingsWriter.PlanAsync(files.AsYuzu(), CancellationToken.None);

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public async Task A_wrong_global_value_is_planned_against_the_global_file()
    {
        using var files = new TempEmulator(HealthyGlobal.Replace("use_vsync=0", "use_vsync=2"));

        var plan = await EmulatorSettingsWriter.PlanAsync(files.AsYuzu(), CancellationToken.None);

        var change = Assert.Single(plan.Changes);
        Assert.Equal("Graphics > VSync", change.DisplayName);
        Assert.Equal(files.Global, change.FilePath);
        Assert.Contains("global", change.Current);
    }

    /// <summary>
    /// The failure YuzuSettings exists to expose: fixing the global would be silently
    /// beaten by the per-game override, so the fix has to land in the file that wins.
    /// </summary>
    [Fact]
    public async Task A_per_game_override_is_planned_against_the_per_game_file()
    {
        using var files = new TempEmulator(HealthyGlobal, """
            [Renderer]
            async_presentation\use_global=false
            async_presentation\default=false
            async_presentation=false
            """);

        var plan = await EmulatorSettingsWriter.PlanAsync(files.AsYuzu(), CancellationToken.None);

        var change = Assert.Single(plan.Changes);
        Assert.Equal("Graphics > Async presentation", change.DisplayName);
        Assert.Equal(files.PerGame, change.FilePath);
    }

    [Fact]
    public async Task A_per_game_entry_that_defers_leaves_the_global_in_charge()
    {
        using var files = new TempEmulator(HealthyGlobal.Replace("use_vsync=0", "use_vsync=2"), """
            [Renderer]
            use_vsync\use_global=true
            """);

        var plan = await EmulatorSettingsWriter.PlanAsync(files.AsYuzu(), CancellationToken.None);

        Assert.Equal(files.Global, Assert.Single(plan.Changes).FilePath);
    }

    /// <summary>
    /// The optional ones are exactly the settings the scan declines to prescribe, so
    /// the dialog can offer them ticked without contradicting the finding text.
    /// </summary>
    [Fact]
    public async Task The_judgement_calls_are_the_only_optional_ones()
    {
        using var files = new TempEmulator("[System]\nlanguage_index=1");

        var plan = await EmulatorSettingsWriter.PlanAsync(files.AsYuzu(), CancellationToken.None);

        Assert.Equal(
            [
                "Graphics > Async presentation",
                "System > Memory layout",
                "Graphics > GPU mode",
                "Graphics > Force maximum clocks",
            ],
            plan.Changes.Where(c => c.IsOptional).Select(c => c.DisplayName));
    }

    [Fact]
    public async Task Applying_a_plan_makes_the_settings_resolve_correctly()
    {
        using var files = new TempEmulator("[System]\nlanguage_index=1");

        var emulator = files.AsYuzu();
        var plan = await EmulatorSettingsWriter.PlanAsync(emulator, CancellationToken.None);

        await EmulatorSettingsWriter.ApplyAsync(emulator, plan, null, CancellationToken.None);

        // Re-planning against the file we just wrote is the check that it took.
        var after = await EmulatorSettingsWriter.PlanAsync(emulator, CancellationToken.None);
        Assert.True(after.IsEmpty);

        var settings = await YuzuSettings.LoadAsync(files.Global, files.PerGame, CancellationToken.None);
        Assert.True(settings.Resolve("System", "rng_seed_enabled").AsBool());
        Assert.Equal(0, settings.Resolve("System", "rng_seed").AsInt());
        Assert.Equal(1, settings.Resolve("Renderer", "backend").AsInt());
        Assert.Equal("1", IniFile.Parse(File.ReadAllText(files.Global!)).GetRaw("System", "language_index"));
    }

    [Fact]
    public async Task Applying_backs_up_the_file_it_touches()
    {
        using var files = new TempEmulator(HealthyGlobal.Replace("use_vsync=0", "use_vsync=2"));

        var emulator = files.AsYuzu();
        var plan = await EmulatorSettingsWriter.PlanAsync(emulator, CancellationToken.None);
        await EmulatorSettingsWriter.ApplyAsync(emulator, plan, null, CancellationToken.None);

        Assert.Contains("use_vsync=2", await File.ReadAllTextAsync(files.Global + ".bak"));
    }

    // ---- Ryujinx ------------------------------------------------------------

    [Fact]
    public async Task Ryujinx_plans_only_the_pptc_keys_that_are_on()
    {
        using var files = new TempEmulator(ryujinx: """
            {"version": 59, "enable_ptc": true, "enable_low_power_ptc": false, "graphics_backend": "Vulkan"}
            """);

        var plan = await EmulatorSettingsWriter.PlanAsync(files.AsRyujinx(), CancellationToken.None);

        Assert.Equal("System > Enable PPTC", Assert.Single(plan.Changes).DisplayName);
    }

    [Fact]
    public async Task Ryujinx_settings_it_has_no_opinion_about_survive_the_write()
    {
        using var files = new TempEmulator(ryujinx: """
            {"version": 59, "enable_ptc": true, "graphics_backend": "Vulkan", "custom_thing": [1, 2, 3]}
            """);

        var emulator = files.AsRyujinx();
        var plan = await EmulatorSettingsWriter.PlanAsync(emulator, CancellationToken.None);
        await EmulatorSettingsWriter.ApplyAsync(emulator, plan, null, CancellationToken.None);

        var config = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(files.Ryujinx!))!;

        Assert.False(config["enable_ptc"]!.GetValue<bool>());
        Assert.Equal(59, config["version"]!.GetValue<int>());
        Assert.Equal("Vulkan", config["graphics_backend"]!.GetValue<string>());
        Assert.Equal(3, config["custom_thing"]!.AsArray().Count);
    }

    /// <summary>
    /// Ryujinx's own games folder carries both spellings of the title id on a real
    /// install, and the cpu cache is not always under the one you would guess.
    /// </summary>
    [Fact]
    public async Task Both_casings_of_the_pptc_cache_are_deleted()
    {
        using var files = new TempEmulator(ryujinx: """{"enable_ptc": true}""");

        var lower = files.AddPptcCache(HdrPaths.SmashTitleId.ToLowerInvariant());
        var upper = files.AddPptcCache(HdrPaths.SmashTitleId.ToUpperInvariant());
        var other = files.AddPptcCache("0100000000001009");

        var emulator = files.AsRyujinx();
        var plan = await EmulatorSettingsWriter.PlanAsync(emulator, CancellationToken.None);

        Assert.Equal(2, plan.PptcCaches.Count);

        await EmulatorSettingsWriter.ApplyAsync(emulator, plan, null, CancellationToken.None);

        Assert.False(Directory.Exists(lower));
        Assert.False(Directory.Exists(upper));
        Assert.True(Directory.Exists(other));
    }

    [Fact]
    public async Task Pptc_caches_are_left_alone_when_pptc_is_already_off()
    {
        using var files = new TempEmulator(ryujinx: """{"enable_ptc": false}""");
        files.AddPptcCache(HdrPaths.SmashTitleId);

        var plan = await EmulatorSettingsWriter.PlanAsync(files.AsRyujinx(), CancellationToken.None);

        Assert.True(plan.IsEmpty);
    }

    private sealed class TempEmulator : IDisposable
    {
        private readonly string _directory;

        public TempEmulator(string? global = null, string? perGame = null, string? ryujinx = null)
        {
            _directory = Path.Combine(Path.GetTempPath(), "hdr-write-tests", Guid.NewGuid().ToString("n"));
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

        public string AddPptcCache(string titleId)
        {
            var cache = Path.Combine(_directory, "games", titleId, "cache", "cpu");
            Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, "0.cache"), "stale");
            return cache;
        }

        public EmulatorInstallation AsYuzu() =>
            new(EmulatorFamily.YuzuFamily, "eden", _directory, Global, PerGame);

        public EmulatorInstallation AsRyujinx() =>
            new(EmulatorFamily.Ryujinx, "Ryujinx", _directory, Ryujinx, null);

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
