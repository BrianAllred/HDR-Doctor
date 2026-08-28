using System.Text.Json;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Core.Checks;

/// <summary>
/// Checks the emulator settings HDR depends on.
/// </summary>
/// <remarks>
/// Skyline mods detect if they're running on a real Switch or under an emulator by looking
/// at where the game's code was loaded in memory, for example HDR's <c>is_on_ryujinx()</c>
/// where HDR compares the text base against two known emulator addresses. Enabling RNG
/// seed makes yuzu-family emulators stop randomizing that address. If it's off, the emulator
/// emulates the Switch's Address Space Layout Randomization, the check fails, and HDR runs 
/// Switch-only code. The rest are common performance and stability settings that can
/// cause issues.
/// </remarks>
public sealed class EmulatorConfigCheck : ICheck
{
    private const string System = "System";
    private const string Renderer = "Renderer";
    private const string Core = "Core";

    public string Id => "emulator-config";

    public CheckCategory Category => CheckCategory.EmulatorConfig;

    public PlatformScope Scope => PlatformScope.EmulatorOnly;

    public string DisplayName => "Emulator configuration";

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct)
    {
        if (ctx.Emulator is null)
        {
            return
            [
                new Finding(
                    Id,
                    Severity.Info,
                    "Emulator settings were not checked",
                    "This profile does not have an emulator associated with it.",
                    "Edit the profile and point it at your emulator's config file to have these settings checked. "
                    + "Several of them are common causes of HDR not working under an "
                    + "emulator even when every file is correct.",
                    [])
            ];
        }

        return ctx.Emulator.Family switch
        {
            EmulatorFamily.Ryujinx => await CheckRyujinxAsync(ctx.Emulator, ct).ConfigureAwait(false),
            _ => await CheckYuzuFamilyAsync(ctx.Emulator, ct).ConfigureAwait(false),
        };
    }

    // ---- yuzu family --------------------------------------------------------

    private async Task<IReadOnlyList<Finding>> CheckYuzuFamilyAsync(
        EmulatorInstallation emulator,
        CancellationToken ct)
    {
        var findings = new List<Finding>();

        var settings = await YuzuSettings
            .LoadAsync(emulator.GlobalConfigPath, emulator.PerGameConfigPath, ct)
            .ConfigureAwait(false);

        if (!settings.HasAnyConfig)
        {
            findings.Add(new Finding(
                Id,
                Severity.Warning,
                $"{emulator.ProductName} config file not found",
                $"Looked for {emulator.GlobalConfigPath}.",
                "Without the config file none of the emulator settings can be checked. If your emulator stores its "
                + "settings somewhere else, edit the profile and point it at the right file.",
                [emulator.GlobalConfigPath ?? "(not set)"]));

            return findings;
        }

        CheckRngSeed(settings, findings);
        CheckBool(settings, findings, Renderer, "use_asynchronous_shaders", expected: false,
            title: "Asynchronous shader compilation",
            problem: "Async shader compilation is enabled.",
            explanation: "This trades correctness for smoother frame pacing by drawing before shaders are ready. With "
                         + "HDR it can cause visual glitches and, on some drivers, crashes. Turn it off.");

        CheckBool(settings, findings, Renderer, "force_max_clock", expected: false,
            title: "Force maximum clocks",
            problem: "Force maximum clocks is enabled.",
            explanation: "On Nvidia hardware this setting is known to cause instability rather than help performance. "
                         + "If you are on an Nvidia GPU, turn it off. On other hardware it is less of a concern.");

        CheckVsync(settings, findings);
        CheckGraphicsBackend(settings, findings);
        CheckAsyncPresentation(settings, findings);
        CheckGpuAccuracy(settings, findings);
        CheckMemoryLayout(settings, findings);

        return findings;
    }

    /// <summary>
    /// Reports a boolean setting that should hold a particular value.
    /// </summary>
    private void CheckBool(
        YuzuSettings settings,
        List<Finding> findings,
        string section,
        string key,
        bool expected,
        string title,
        string problem,
        string explanation)
    {
        var setting = settings.Resolve(section, key);
        var actual = setting.AsBool();

        if (actual is null)
        {
            return;
        }

        if (actual == expected)
        {
            findings.Add(new Finding(
                Id,
                Severity.Ok,
                $"{title} is {(expected ? "on" : "off")}",
                $"From {setting.OriginDescription}.",
                string.Empty,
                [$"Graphics > {title}"]));

            return;
        }

        findings.Add(new Finding(
            Id,
            Severity.Warning,
            title,
            $"{problem} From {setting.OriginDescription}.",
            explanation,
            [$"Graphics > {title}"]));
    }

    private void CheckRngSeed(YuzuSettings settings, List<Finding> findings)
    {
        var enabled = settings.Resolve(System, "rng_seed_enabled");
        var seed = settings.Resolve(System, "rng_seed");

        const string why =
            "Mods detect whether they are running on a real Switch or an emulator by checking where the game's code "
            + "was loaded in memory. Turning on a fixed RNG seed is what stops the emulator randomizing that address. "
            + "With it off, HDR thinks it is running on a real Switch and runs the wrong code. Enable RNG seed and leave the "
            + "value at 0.";

        if (enabled.AsBool() != true)
        {
            findings.Add(new Finding(
                Id,
                Severity.Critical,
                "RNG seed is not enabled",
                enabled.Exists
                    ? $"rng_seed_enabled is {enabled.Value}, from {enabled.OriginDescription}."
                    : "rng_seed_enabled was not found in the config.",
                why,
                ["System > RNG seed"]));

            return;
        }

        var value = seed.AsInt();
        if (value is not null and not 0)
        {
            findings.Add(new Finding(
                Id,
                Severity.Critical,
                $"RNG seed is set to {value}, not 0",
                $"rng_seed is {seed.Value}, from {seed.OriginDescription}.",
                "A non-zero value shifts the emulated address space, so HDR still thinks it's on a real Switch. Set the seed to 0.",
                ["System > RNG seed"]));

            return;
        }

        findings.Add(new Finding(
            Id,
            Severity.Ok,
            "RNG seed is enabled and set to 0",
            $"From {enabled.OriginDescription}.",
            string.Empty,
            ["System > RNG seed"]));
    }

    private void CheckVsync(YuzuSettings settings, List<Finding> findings)
    {
        var vsync = settings.Resolve(Renderer, "use_vsync");
        var mode = vsync.AsInt();

        if (mode is null or 0)
        {
            findings.Add(new Finding(
                Id,
                Severity.Ok,
                "VSync is off",
                vsync.Exists ? $"From {vsync.OriginDescription}." : string.Empty,
                string.Empty,
                ["Graphics > VSync"]));

            return;
        }

        findings.Add(new Finding(
            Id,
            Severity.Warning,
            "VSync is enabled",
            $"use_vsync is {vsync.Value}, from {vsync.OriginDescription}.",
            "VSync adds a frame or more of input lag, which is very noticeable. Set it to Immediate (off).",
            ["Graphics > VSync"]));
    }

    private void CheckGraphicsBackend(YuzuSettings settings, List<Finding> findings)
    {
        var backend = settings.Resolve(Renderer, "backend");
        var value = backend.AsInt();

        if (value == 1)
        {
            findings.Add(new Finding(
                Id,
                Severity.Ok,
                "Graphics backend is Vulkan",
                $"From {backend.OriginDescription}.",
                string.Empty,
                ["Graphics > API"]));

            return;
        }

        var name = value switch { 0 => "OpenGL", 2 => "Null", _ => backend.Value ?? "unknown" };

        findings.Add(new Finding(
            Id,
            Severity.Warning,
            $"Graphics backend is {name}, not Vulkan",
            $"backend is {backend.Value}, from {backend.OriginDescription}.",
            "Vulkan is what is most tested. The OpenGL path is much less tested and produces "
            + "graphical and performance issues that are difficult to reproduce. Switch the API to Vulkan.",
            ["Graphics > API"]));
    }

    private void CheckAsyncPresentation(YuzuSettings settings, List<Finding> findings)
    {
        var setting = settings.Resolve(Renderer, "async_presentation");

        if (setting.AsBool() != true)
        {
            return;
        }

        findings.Add(new Finding(
            Id,
            Severity.Info,
            "Asynchronous presentation is enabled",
            $"async_presentation is {setting.Value}, from {setting.OriginDescription}.",
            "This is a known crash risk on some systems. It is not guaranteed to be your problem, but if you are getting crashes that "
            + "are hard to reproduce, this is one of the first things to check.",
            ["Graphics > Async presentation"]));
    }

    private void CheckGpuAccuracy(YuzuSettings settings, List<Finding> findings)
    {
        var setting = settings.Resolve(Renderer, "gpu_accuracy");

        if (!setting.Exists)
        {
            return;
        }

        // The label and enum ordering differ between forks (yuzu called this "GPU
        // Accuracy", eden calls it "GPU Mode")
        var value = setting.AsInt();
        var isBalanced = value == 1;

        findings.Add(new Finding(
            Id,
            isBalanced ? Severity.Ok : Severity.Info,
            isBalanced
                ? "GPU mode is set to the balanced middle option"
                : $"GPU mode is set to option {setting.Value}",
            $"gpu_accuracy is {setting.Value}, from {setting.OriginDescription}.",
            isBalanced
                ? string.Empty
                : "HDR runs best on the balanced middle setting. The faster option causes visual glitches and the "
                  + "most accurate one costs performance for no benefit. Note that emulator forks name and order "
                  + "these options differently, so check the label in your own settings.",
            ["Graphics > GPU mode"]));
    }

    private void CheckMemoryLayout(YuzuSettings settings, List<Finding> findings)
    {
        var setting = settings.Resolve(Core, "memory_layout_mode");

        if (!setting.Exists)
        {
            return;
        }

        var name = setting.AsInt() switch
        {
            0 => "4 GB",
            1 => "6 GB",
            2 => "8 GB",
            _ => setting.Value ?? "unknown",
        };

        findings.Add(new Finding(
            Id,
            Severity.Info,
            $"Memory layout is set to {name}",
            $"From {setting.OriginDescription}.",
            "There is no single right answer here. Raising this to 8 GB often reduces crashing with HDR, but on some "
            + "setups it makes things worse. If you are crashing, try 8 GB; if that does not help or makes it worse, "
            + "set it back to 4 GB.",
            ["System > Memory layout"]));
    }

    // ---- Ryujinx ------------------------------------------------------------

    private async Task<IReadOnlyList<Finding>> CheckRyujinxAsync(
        EmulatorInstallation emulator,
        CancellationToken ct)
    {
        var findings = new List<Finding>();

        if (emulator.GlobalConfigPath is null || !File.Exists(emulator.GlobalConfigPath))
        {
            findings.Add(new Finding(
                Id,
                Severity.Warning,
                "Ryujinx config file not found",
                $"Looked for {emulator.GlobalConfigPath}.",
                "Without Config.json the emulator settings cannot be checked. Edit the profile and point it at the "
                + "right file if Ryujinx keeps its config somewhere else.",
                [emulator.GlobalConfigPath ?? "(not set)"]));

            return findings;
        }

        JsonElement root;
        try
        {
            await using var stream = File.OpenRead(emulator.GlobalConfigPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            root = document.RootElement.Clone();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            findings.Add(new Finding(
                Id,
                Severity.Warning,
                "Ryujinx config file could not be read",
                e.Message,
                "The file exists but is not valid JSON. If Ryujinx is running, close it and scan again — it rewrites "
                + "this file on exit.",
                [emulator.GlobalConfigPath]));

            return findings;
        }

        CheckPptc(root, findings, emulator.GlobalConfigPath);
        ReportRyujinxInfo(root, findings, emulator.GlobalConfigPath);

        return findings;
    }

    private void CheckPptc(JsonElement root, List<Finding> findings, string configPath)
    {
        var ptc = GetBool(root, "enable_ptc");
        var lowPowerPtc = GetBool(root, "enable_low_power_ptc");

        const string why =
            "PPTC caches translated game code between runs. Skyline works by rewriting that code in memory as the game "
            + "loads, so a cache built before those rewrites gets replayed over the top of them. The result is crashes "
            + "and behavior that changes between launches for no visible reason. Turn PPTC off. And if it was on, "
            + "delete the existing cache so the stale one is not reused.";

        if (ptc == true || lowPowerPtc == true)
        {
            var which = ptc == true && lowPowerPtc == true
                ? "PPTC and low-power PPTC are both enabled"
                : ptc == true
                    ? "PPTC is enabled"
                    : "Low-power PPTC is enabled";

            findings.Add(new Finding(
                Id,
                Severity.Error,
                which,
                $"In {configPath}.",
                why,
                ["System > Enable PPTC"]));

            return;
        }

        findings.Add(new Finding(
            Id,
            Severity.Ok,
            "PPTC is disabled",
            string.Empty,
            string.Empty,
            ["System > Enable PPTC"]));
    }

    private void ReportRyujinxInfo(JsonElement root, List<Finding> findings, string configPath)
    {
        var details = new List<string>();

        foreach (var key in new[] { "graphics_backend", "memory_manager_mode", "use_hypervisor", "vsync_mode" })
        {
            if (root.TryGetProperty(key, out var value))
            {
                details.Add($"{key}: {value}");
            }
        }

        if (details.Count == 0)
        {
            return;
        }

        findings.Add(new Finding(
            Id,
            Severity.Info,
            "Ryujinx graphics and memory settings",
            string.Join(", ", details),
            "Recorded here so it is in the report if you need to share it. Vulkan is the most tested backend.",
            [configPath]));
    }

    private static bool? GetBool(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
