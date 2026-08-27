namespace HdrDoctor.Core.Services;

/// <summary>
/// The host's well-known directories. Injected rather than read from
/// <see cref="Environment"/> directly so Windows and macOS path logic can be
/// unit-tested from a Linux dev machine.
/// </summary>
public interface IAppPaths
{
    /// <summary>XDG config home, %APPDATA%, or ~/Library/Application Support.</summary>
    string ConfigHome { get; }

    /// <summary>XDG data home, %LOCALAPPDATA%, or ~/Library/Application Support.</summary>
    string DataHome { get; }

    /// <summary>%LOCALAPPDATA%\Programs on Windows; the platform equivalent elsewhere.</summary>
    string LocalPrograms { get; }

    string Home { get; }

    bool IsWindows { get; }

    bool IsMacOs { get; }
}

/// <summary>The real host directories.</summary>
public sealed class SystemAppPaths : IAppPaths
{
    public static SystemAppPaths Instance { get; } = new();

    public bool IsWindows { get; } = OperatingSystem.IsWindows();

    public bool IsMacOs { get; } = OperatingSystem.IsMacOS();

    public string Home { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string ConfigHome
    {
        get
        {
            if (IsWindows)
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            if (IsMacOs)
            {
                return Path.Combine(Home, "Library", "Application Support");
            }

            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return string.IsNullOrWhiteSpace(xdg) ? Path.Combine(Home, ".config") : xdg;
        }
    }

    public string DataHome
    {
        get
        {
            if (IsWindows)
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }

            if (IsMacOs)
            {
                return Path.Combine(Home, "Library", "Application Support");
            }

            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return string.IsNullOrWhiteSpace(xdg) ? Path.Combine(Home, ".local", "share") : xdg;
        }
    }

    public string LocalPrograms => IsWindows
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        : DataHome;
}
