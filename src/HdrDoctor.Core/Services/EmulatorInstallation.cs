namespace HdrDoctor.Core.Services;

/// <summary>Which config format an emulator uses.</summary>
public enum EmulatorFamily
{
    /// <summary>yuzu and its forks (eden, citron, sudachi): qt-config.ini plus per-game overrides.</summary>
    YuzuFamily,

    /// <summary>Ryujinx: a single Config.json.</summary>
    Ryujinx,
}

/// <summary>An emulator installation found on this machine, or configured by the user.</summary>
/// <param name="Family">Which config format to expect.</param>
/// <param name="ProductName">Display name, e.g. "eden".</param>
/// <param name="SdmcPath">The emulator's virtual SD root.</param>
/// <param name="GlobalConfigPath">qt-config.ini or Config.json.</param>
/// <param name="PerGameConfigPath">
/// The per-title override file, yuzu-family only.
/// </param>
public sealed record EmulatorInstallation(
    EmulatorFamily Family,
    string ProductName,
    string SdmcPath,
    string? GlobalConfigPath,
    string? PerGameConfigPath)
{
    public bool HasConfig => GlobalConfigPath is not null && File.Exists(GlobalConfigPath);
}

/// <summary>
/// Finds emulator installations in their default locations, so the user can pick one
/// instead of hunting for a path like <c>~/.local/share/eden/sdmc</c>.
/// </summary>
public sealed class EmulatorLocator(IAppPaths paths)
{
    /// <summary>
    /// yuzu forks that share the same layout. Each keeps its data in
    /// <c>&lt;data&gt;/&lt;name&gt;/sdmc</c> and its config in <c>&lt;config&gt;/&lt;name&gt;/qt-config.ini</c>.
    /// </summary>
    private static readonly string[] YuzuFamilyNames = ["eden", "citron", "sudachi", "yuzu"];

    /// <summary>Every emulator whose SD directory actually exists on this machine.</summary>
    public IReadOnlyList<EmulatorInstallation> FindInstalled() =>
        [.. EnumerateCandidates().Where(e => Directory.Exists(e.SdmcPath))];

    /// <summary>
    /// Works out which emulator a given folder belongs to, by matching it against the
    /// known SD locations. Returns null for a real SD card or an unrecognized path.
    /// </summary>
    public EmulatorInstallation? IdentifyByPath(string sdRoot)
    {
        var normalized = Normalize(sdRoot);

        return EnumerateCandidates()
            .FirstOrDefault(candidate => string.Equals(
                Normalize(candidate.SdmcPath),
                normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<EmulatorInstallation> EnumerateCandidates()
    {
        foreach (var name in YuzuFamilyNames)
        {
            // Windows keeps everything under one roaming folder; Linux and macOS split
            // data from config.
            var dataRoot = paths.IsWindows
                ? Path.Combine(paths.ConfigHome, name)
                : Path.Combine(paths.DataHome, name);

            var configRoot = paths.IsWindows
                ? Path.Combine(paths.ConfigHome, name, "config")
                : Path.Combine(paths.ConfigHome, name);

            yield return new EmulatorInstallation(
                EmulatorFamily.YuzuFamily,
                name,
                Path.Combine(dataRoot, "sdmc"),
                Path.Combine(configRoot, "qt-config.ini"),
                Path.Combine(configRoot, "custom", $"{HdrPaths.SmashTitleId}.ini"));
        }

        var ryujinxRoot = Path.Combine(paths.ConfigHome, "Ryujinx");

        yield return new EmulatorInstallation(
            EmulatorFamily.Ryujinx,
            "Ryujinx",
            Path.Combine(ryujinxRoot, "sdcard"),
            Path.Combine(ryujinxRoot, "Config.json"),
            PerGameConfigPath: null);
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
