using System.Text.Json;
using System.Text.Json.Serialization;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Core.Profiles;

public sealed record ProfileSettings
{
    public List<InstallProfile> Profiles { get; init; } = [];

    public string? LastUsedProfileId { get; init; }

    public bool IgnoreMusicFiles { get; init; }
}

/// <summary>Loads and saves the user's install profiles.</summary>
public sealed class ProfileStore(IAppPaths paths)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string SettingsPath => Path.Combine(paths.ConfigHome, "hdr-doctor", "profiles.json");

    public ProfileSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new ProfileSettings();
            }

            return JsonSerializer.Deserialize<ProfileSettings>(File.ReadAllText(SettingsPath), Json)
                   ?? new ProfileSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ProfileSettings();
        }
    }

    public void Save(ProfileSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Json));
    }

    /// <summary>
    /// Builds first-time priofiles
    /// </summary>
    public static IReadOnlyList<InstallProfile> SuggestProfiles(IAppPaths paths) =>
        [.. new EmulatorLocator(paths).FindInstalled().Select(InstallProfile.ForEmulator)];

    /// <summary>
    /// Guesses whether a folder belongs to a Switch or an emulator
    /// </summary>
    public static InstallPlatform SuggestPlatform(string path, IAppPaths paths)
    {
        if (new EmulatorLocator(paths).IdentifyByPath(path) is not null)
        {
            return InstallPlatform.Emulator;
        }

        // A real card carries the Atmosphere bootloader payload and the homebrew menu
        // folder. An emulator's sdmc normally has neither.
        var switchMarkers = new[] { "bootloader", "switch", Path.Combine("Nintendo", "Contents") };
        return switchMarkers.Any(marker => Directory.Exists(Path.Combine(path, marker)))
            ? InstallPlatform.Switch
            : InstallPlatform.Emulator;
    }
}
