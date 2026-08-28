using System.Text.Json;
using System.Text.Json.Nodes;
using HdrDoctor.Core.Checks;

namespace HdrDoctor.Core.Services;

/// <summary>One setting the writer would change.</summary>
/// <param name="DisplayName">The setting's name in the emulator's UI.</param>
/// <param name="Summary">What it will be set to.</param>
/// <param name="Caveat">
/// Why this setting might or might not need to be set.
/// </param>
/// <param name="Current">What it is set to now, for the dialog.</param>
public sealed record SettingChange(
    string DisplayName,
    string Summary,
    string? Caveat,
    string? Current,
    string FilePath,
    string Section,
    string Key,
    string Value)
{
    public bool IsOptional => Caveat is not null;
}

/// <param name="Changes">
/// Settings that are not already the right value.
/// </param>
/// <param name="PptcCaches">
/// Ryujinx PPTC cache directories to delete.
/// </param>
public sealed record SettingsPlan(IReadOnlyList<SettingChange> Changes, IReadOnlyList<string> PptcCaches)
{
    public bool IsEmpty => Changes.Count == 0 && PptcCaches.Count == 0;
}

/// <summary>
/// Writes the optimal emulator settings.
/// </summary>
public static class EmulatorSettingsWriter
{
    private const string SystemSection = "System";
    private const string Renderer = "Renderer";
    private const string Core = "Core";

    /// <summary>
    /// What gets written, in the order the dialog lists it: the settings with a known
    /// right answer first, then the three that are a judgement call.
    /// </summary>
    /// <remarks>
    /// <c>gpu_accuracy</c> is written as 1 for every yuzu fork. The enum is named and
    /// ordered differently between them, but <see cref="EmulatorConfigCheck"/> already
    /// tests for 1 regardless of fork, so detection and fix agree — a fork that
    /// reordered it would report wrongly today too.
    /// </remarks>
    private static readonly SettingChange[] YuzuTargets =
    [
        new("System > RNG seed", "enabled", null, null, "", SystemSection, "rng_seed_enabled", "true"),
        new("System > RNG seed value", "0", null, null, "", SystemSection, "rng_seed", "0"),
        new("Graphics > API", "Vulkan", null, null, "", Renderer, "backend", "1"),
        new("Graphics > VSync", "Immediate (off)", null, null, "", Renderer, "use_vsync", "0"),
        new("Graphics > Async shader compilation", "off", null, null, "", Renderer, "use_asynchronous_shaders", "false"),

        new("Graphics > Async presentation", "on",
            "While this is a known crash risk on some systems, it reduces stuttering when compiling shaders, so optimal value is on. "
            + "If you experience crashes, turn this off first.", null, "", Renderer, "async_presentation", "true"),
        new("System > Memory layout", "8 GB",
            "There is no single right answer. 8 GB often reduces crashing, but on some setups it makes things worse — "
            + "set it back to 4 GB if that happens.",
            null, "", Core, "memory_layout_mode", "2"),
        new("Graphics > GPU mode", "balanced",
            "Forks name and order this option differently, so check the label in your own settings afterwards.",
            null, "", Renderer, "gpu_accuracy", "1"),
        new("Graphics > Force maximum clocks", "off",
            "Only known to cause trouble on Nvidia hardware. On other GPUs it is less of a concern either way.",
            null, "", Renderer, "force_max_clock", "false"),
    ];

    /// <summary>Ryujinx settings, keyed by their property in Config.json.</summary>
    private static readonly SettingChange[] RyujinxTargets =
    [
        new("System > Enable PPTC", "off", null, null, "", "", "enable_ptc", "false"),
        new("System > Enable low-power PPTC", "off", null, null, "", "", "enable_low_power_ptc", "false"),
    ];

    public static Task<SettingsPlan> PlanAsync(EmulatorInstallation emulator, CancellationToken ct) =>
        emulator.Family == EmulatorFamily.Ryujinx
            ? PlanRyujinxAsync(emulator, ct)
            : PlanYuzuAsync(emulator, ct);

    /// <summary>
    /// Applies the changes the user kept. Each config file is copied to
    /// <c>.bak</c> once before it is touched.
    /// </summary>
    public static async Task ApplyAsync(
        EmulatorInstallation emulator,
        SettingsPlan plan,
        IProgress<Model.ScanProgress>? progress,
        CancellationToken ct)
    {
        foreach (var file in plan.Changes.Select(c => c.FilePath).Distinct())
        {
            File.Copy(file, file + ".bak", overwrite: true);
        }

        if (emulator.Family == EmulatorFamily.Ryujinx)
        {
            await ApplyRyujinxAsync(plan, progress, ct).ConfigureAwait(false);
        }
        else
        {
            foreach (var change in plan.Changes)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new Model.ScanProgress("Writing emulator settings", change.DisplayName, null));

                await IniFile.SetValueAsync(change.FilePath, change.Section, change.Key, change.Value, ct)
                    .ConfigureAwait(false);
            }
        }

        foreach (var cache in plan.PptcCaches)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new Model.ScanProgress("Deleting the PPTC cache", cache, null));

            if (Directory.Exists(cache))
            {
                Directory.Delete(cache, recursive: true);
            }
        }
    }

    // ---- yuzu family --------------------------------------------------------

    private static async Task<SettingsPlan> PlanYuzuAsync(EmulatorInstallation emulator, CancellationToken ct)
    {
        if (emulator.GlobalConfigPath is null || !File.Exists(emulator.GlobalConfigPath))
        {
            return new SettingsPlan([], []);
        }

        var settings = await YuzuSettings
            .LoadAsync(emulator.GlobalConfigPath, emulator.PerGameConfigPath, ct)
            .ConfigureAwait(false);

        var changes = new List<SettingChange>();

        foreach (var target in YuzuTargets)
        {
            var current = settings.Resolve(target.Section, target.Key);

            if (Matches(current, target.Value))
            {
                continue;
            }

            var file = current.Origin == SettingOrigin.PerGame && emulator.PerGameConfigPath is not null
                ? emulator.PerGameConfigPath
                : emulator.GlobalConfigPath;

            changes.Add(target with { FilePath = file, Current = Describe(current) });
        }

        return new SettingsPlan(changes, []);
    }

    /// <summary>
    /// True when the setting is already what would be set.
    /// </summary>
    private static bool Matches(ResolvedSetting current, string target)
    {
        if (!current.Exists)
        {
            return false;
        }

        return target is "true" or "false"
            ? current.AsBool() == (target == "true")
            : current.AsInt() is { } value && value.ToString() == target;
    }

    private static string? Describe(ResolvedSetting current) =>
        current.Exists ? $"{current.Value} ({current.OriginDescription})" : "not set";

    // ---- Ryujinx ------------------------------------------------------------

    private static async Task<SettingsPlan> PlanRyujinxAsync(EmulatorInstallation emulator, CancellationToken ct)
    {
        if (emulator.GlobalConfigPath is null || !File.Exists(emulator.GlobalConfigPath))
        {
            return new SettingsPlan([], []);
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(await File.ReadAllTextAsync(emulator.GlobalConfigPath, ct).ConfigureAwait(false));
        }
        catch (JsonException)
        {
            return new SettingsPlan([], []);
        }

        if (root is not JsonObject config)
        {
            return new SettingsPlan([], []);
        }

        var changes = new List<SettingChange>();

        foreach (var target in RyujinxTargets)
        {
            if (config[target.Key] is not JsonValue value || value.GetValueKind() != JsonValueKind.True)
            {
                continue;
            }

            changes.Add(target with { FilePath = emulator.GlobalConfigPath, Current = "on" });
        }

        return new SettingsPlan(changes, changes.Count == 0 ? [] : FindPptcCaches(emulator.GlobalConfigPath));
    }

    /// <summary>
    /// Every PPTC cache directory for Smash
    /// </summary>
    private static IReadOnlyList<string> FindPptcCaches(string configPath)
    {
        var games = Path.Combine(Path.GetDirectoryName(configPath) ?? string.Empty, "games");

        if (!Directory.Exists(games))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateDirectories(games)
                .Where(d => string.Equals(
                    Path.GetFileName(d),
                    HdrPaths.SmashTitleId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(d => Path.Combine(d, "cache", "cpu"))
                .Where(Directory.Exists)
        ];
    }

    private static async Task ApplyRyujinxAsync(
        SettingsPlan plan,
        IProgress<Model.ScanProgress>? progress,
        CancellationToken ct)
    {
        if (plan.Changes.Count == 0)
        {
            return;
        }

        var path = plan.Changes[0].FilePath;
        var config = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false))!;

        foreach (var change in plan.Changes)
        {
            progress?.Report(new Model.ScanProgress("Writing emulator settings", change.DisplayName, null));
            config[change.Key] = change.Value == "true";
        }

        await File.WriteAllTextAsync(
            path,
            config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
    }
}
