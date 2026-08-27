using System.Text.Json.Serialization;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Core.Profiles;

/// <summary>
/// How a profile reaches its installation.
/// </summary>
public enum ProfileKind
{
    /// <summary>
    /// A folder on this machine, or a folder on a card reader attached to this machine. Read/write.
    /// </summary>
    Local,

    /// <summary>
    /// A Switch running an FTP server. Read-only.
    /// </summary>
    Ftp,
}

/// <summary>
/// A saved installation the user can scan.
/// </summary>
/// <remarks>
/// Profiles exist in case a user has multiple emulators and/or card readers.
/// A profile pointing at a card reader with no card in it is 
/// <see cref="IsAvailable"/> = false rather than an error.
/// </remarks>
public sealed record InstallProfile
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public ProfileKind Kind { get; init; } = ProfileKind.Local;

    /// <summary>Install root for <see cref="ProfileKind.Local"/> profiles.</summary>
    public string? Path { get; init; }

    /// <summary>
    /// Which platform's rules to apply. Suggested from the folder's shape when the
    /// profile is created, then owned by the user — several checks depend on it and
    /// guessing wrong silently is worse than asking.
    /// </summary>
    public InstallPlatform Platform { get; init; } = InstallPlatform.Emulator;

    // ---- FTP ----------------------------------------------------------------

    public string? Host { get; init; }

    public int Port { get; init; } = 5000;

    public string? Username { get; init; }

    /// <summary>
    /// Stored only when the user opts in, and in plain text — the UI says so. Null
    /// means prompt each session.
    /// </summary>
    public string? Password { get; init; }

    // ---- Emulator -----------------------------------------------------------

    public EmulatorFamily? EmulatorFamily { get; init; }

    public string? EmulatorName { get; init; }

    public string? EmulatorConfigPath { get; init; }

    public string? EmulatorPerGameConfigPath { get; init; }

    /// <summary>False when a local profile's folder is not currently there.</summary>
    [JsonIgnore]
    public bool IsAvailable => Kind switch
    {
        ProfileKind.Local => !string.IsNullOrWhiteSpace(Path) && Directory.Exists(Path),
        ProfileKind.Ftp => !string.IsNullOrWhiteSpace(Host),
        _ => false,
    };

    /// <summary>Why the profile cannot be scanned right now, or null when it can.</summary>
    [JsonIgnore]
    public string? UnavailableReason
    {
        get
        {
            if (IsAvailable)
            {
                return null;
            }

            return Kind switch
            {
                ProfileKind.Local when string.IsNullOrWhiteSpace(Path) => "No folder set.",
                ProfileKind.Local => "Folder not found — if this is a card reader, the card may not be inserted.",
                ProfileKind.Ftp => "No host set.",
                _ => "Not configured.",
            };
        }
    }

    /// <summary>
    /// True when this profile's folder is really an FTP mount wearing a local path —
    /// curlftpfs, a mapped FTP drive. Detected by <see cref="FtpMountDetector"/> when
    /// the profile is created.
    /// </summary>
    /// <remarks>
    /// Reading one is fine. Writing to one is the risk <see cref="Sources.FtpSource"/>
    /// exists to refuse: the same flaky link and the same live console, only with the
    /// FTP hidden behind a path that looks local. So the profile stays readable and
    /// <see cref="SupportsFixes"/> stays false.
    /// </remarks>
    public bool IsFtpMount { get; init; }

    /// <summary>Fixes are only ever possible against a local profile.</summary>
    [JsonIgnore]
    public bool SupportsFixes => Kind == ProfileKind.Local && !IsFtpMount;

    [JsonIgnore]
    public EmulatorInstallation? Emulator =>
        EmulatorFamily is null || Path is null
            ? null
            : new EmulatorInstallation(
                EmulatorFamily.Value,
                EmulatorName ?? EmulatorFamily.Value.ToString(),
                Path,
                EmulatorConfigPath,
                EmulatorPerGameConfigPath);

    public static InstallProfile ForLocal(string name, string path, InstallPlatform platform) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = name,
        Kind = ProfileKind.Local,
        Path = path,
        Platform = platform,
    };

    public static InstallProfile ForEmulator(EmulatorInstallation emulator) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = emulator.ProductName,
        Kind = ProfileKind.Local,
        Path = emulator.SdmcPath,
        Platform = InstallPlatform.Emulator,
        EmulatorFamily = emulator.Family,
        EmulatorName = emulator.ProductName,
        EmulatorConfigPath = emulator.GlobalConfigPath,
        EmulatorPerGameConfigPath = emulator.PerGameConfigPath,
    };

    public static InstallProfile ForFtp(string name, string host, int port, string? username, string? password) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = name,
        Kind = ProfileKind.Ftp,
        Host = host,
        Port = port,
        Username = username,
        Password = password,

        // Nothing runs an FTP server for an emulator's sdmc folder — reaching an
        // install this way means a console. Overridable, like every other platform
        // guess, but the record's Emulator default is the wrong one here.
        Platform = InstallPlatform.Switch,
    };
}
