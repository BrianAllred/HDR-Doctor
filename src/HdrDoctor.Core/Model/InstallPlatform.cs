namespace HdrDoctor.Core.Model;

/// <summary>What the scanned folder belongs to. Gates which checks run.</summary>
public enum InstallPlatform
{
    /// <summary>A real Nintendo Switch SD card.</summary>
    Switch,

    /// <summary>An emulator's virtual SD ("sdmc") directory.</summary>
    Emulator,
}

/// <summary>Which platforms a check applies to.</summary>
public enum PlatformScope
{
    Any,
    SwitchOnly,
    EmulatorOnly,
}

public static class PlatformScopeExtensions
{
    public static bool AppliesTo(this PlatformScope scope, InstallPlatform platform) => scope switch
    {
        PlatformScope.Any => true,
        PlatformScope.SwitchOnly => platform == InstallPlatform.Switch,
        PlatformScope.EmulatorOnly => platform == InstallPlatform.Emulator,
        _ => true,
    };
}
