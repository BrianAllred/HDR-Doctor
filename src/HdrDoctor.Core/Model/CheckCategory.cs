namespace HdrDoctor.Core.Model;

/// <summary>
/// The troubleshooting areas findings are grouped under in the report. These
/// map one-to-one onto the original troubleshooting checklist.
/// </summary>
public enum CheckCategory
{
    SkylinePlugins,
    HidModule,
    HdrModFolders,
    FileVerification,
    StageAlts,
    EmulatorConfig,
    LauncherConfig,
    SkylineLog,
    CrashReport,
    InstallLayout,
    SdCard,
}

public static class CheckCategoryExtensions
{
    public static string DisplayName(this CheckCategory category) => category switch
    {
        CheckCategory.SkylinePlugins => "Skyline plugins",
        CheckCategory.HidModule => "HID module",
        CheckCategory.HdrModFolders => "HDR mod folders",
        CheckCategory.FileVerification => "File verification",
        CheckCategory.StageAlts => "Stage alts",
        CheckCategory.EmulatorConfig => "Emulator configuration",
        CheckCategory.LauncherConfig => "Launcher configuration",
        CheckCategory.SkylineLog => "Crash/Skyline log",
        CheckCategory.CrashReport => "Crash reports",
        CheckCategory.InstallLayout => "Install layout",
        CheckCategory.SdCard => "SD card",
        _ => category.ToString(),
    };
}
