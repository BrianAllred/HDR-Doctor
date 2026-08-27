namespace HdrDoctor.Core.Services;

/// <summary>Where a resolved emulator setting actually came from.</summary>
public enum SettingOrigin
{
    NotFound,
    Global,
    PerGame,
}

/// <param name="Key">The setting's name in the config file.</param>
/// <param name="Value">Its raw value, or null when the setting is absent.</param>
/// <param name="Origin">Which file won.</param>
public sealed record ResolvedSetting(string Key, string? Value, SettingOrigin Origin)
{
    public bool Exists => Origin != SettingOrigin.NotFound && Value is not null;

    public bool? AsBool() => Value?.Trim().ToLowerInvariant() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => null,
    };

    public int? AsInt() => int.TryParse(Value, out var parsed) ? parsed : null;

    public string OriginDescription => Origin switch
    {
        SettingOrigin.PerGame => "the per-game settings for Smash Ultimate",
        SettingOrigin.Global => "the global emulator settings",
        _ => "no config file",
    };
}

/// <summary>
/// Resolves yuzu-family settings across the global config and the per-game override.
/// </summary>
/// <remarks>
/// This is important because game-specific settings can override the global ones, and
/// the per-game config is not always correct.
/// </remarks>
public sealed class YuzuSettings
{
    private readonly IniFile? _global;
    private readonly IniFile? _perGame;

    private YuzuSettings(IniFile? global, IniFile? perGame)
    {
        _global = global;
        _perGame = perGame;
    }

    public bool HasAnyConfig => _global is not null || _perGame is not null;

    public static async Task<YuzuSettings> LoadAsync(
        string? globalPath,
        string? perGamePath,
        CancellationToken ct) =>
        new(
            await IniFile.LoadAsync(globalPath, ct).ConfigureAwait(false),
            await IniFile.LoadAsync(perGamePath, ct).ConfigureAwait(false));

    public ResolvedSetting Resolve(string section, string key)
    {
        if (_perGame is not null && !_perGame.UsesGlobal(section, key))
        {
            var value = _perGame.GetRaw(section, key);
            if (value is not null)
            {
                return new ResolvedSetting(key, value, SettingOrigin.PerGame);
            }
        }

        var global = _global?.GetRaw(section, key);
        return global is not null
            ? new ResolvedSetting(key, global, SettingOrigin.Global)
            : new ResolvedSetting(key, null, SettingOrigin.NotFound);
    }
}
