namespace HdrDoctor.Core;

/// <summary>
/// Canonical SD-card paths for an HDR install, install-root-relative and
/// forward-slash separated.
/// </summary>
/// <remarks>
/// Sourced from <c>scripts/full_package.py</c> (the packager that builds
/// switch-package.zip) and <c>src/lib.rs quick_validate_install()</c> (the mod's own
/// boot-time self-check). Casing here matches what the packager writes; every lookup
/// goes through <see cref="Sources.CaseInsensitivePathResolver"/>, so the lowercase
/// spelling the Rust side uses resolves to the same place.
/// </remarks>
public static class HdrPaths
{
    /// <summary>Super Smash Bros. Ultimate's title ID.</summary>
    public const string SmashTitleId = "01006A800016E000";

    /// <summary>The HID system module's title ID. HDR's old HID-HDR mitm shipped here.</summary>
    public const string HidTitleId = "0100000000000013";

    public const string SmashContents = $"atmosphere/contents/{SmashTitleId}";
    public const string HidContents = $"atmosphere/contents/{HidTitleId}";
    public const string HidExefs = $"{HidContents}/exefs";

    /// <summary>Skyline itself: subsdk9 + main.npdm.</summary>
    public const string SkylineExefs = $"{SmashContents}/exefs";
    public const string SkylineSubsdk = $"{SkylineExefs}/subsdk9";
    public const string SkylineNpdm = $"{SkylineExefs}/main.npdm";

    public const string PluginsDir = $"{SmashContents}/romfs/skyline/plugins";
    public const string DisabledPluginsDir = $"{SmashContents}/romfs/skyline/disabled_plugins";

    /// <summary>Smashline's hot-reload path. Only valid alongside an hdr-dev mod folder.</summary>
    public const string SmashlineDir = $"{SmashContents}/romfs/smashline";
    public const string DevelopmentNro = $"{SmashlineDir}/development.nro";

    public const string ModsDir = "ultimate/mods";
    public const string HdrDir = $"{ModsDir}/hdr";
    public const string HdrAssetsDir = $"{ModsDir}/hdr-assets";
    public const string HdrStagesDir = $"{ModsDir}/hdr-stages";
    public const string HdrDevDir = $"{ModsDir}/hdr-dev";
    public const string HdrPrDir = $"{ModsDir}/hdr-pr";

    public const string HdrVersionFile = $"{HdrDir}/ui/hdr_version.txt";
    public const string RomfsVersionFile = $"{HdrAssetsDir}/ui/romfs_version.txt";

    /// <summary>
    /// Read by libstage_alts.nro at startup via an unwrapping read. Missing means a
    /// silent boot panic — see <c>stage-alts-2/src/search.rs:300</c>.
    /// </summary>
    public const string StageAltsHashes = "ultimate/stage-alts/Hashes_all";

    /// <summary>
    /// Where Atmosphere writes a crash report when a process dies on hardware.
    /// Reports are named <c>&lt;timestamp&gt;_&lt;program id&gt;.log</c> with the timestamp
    /// zero-padded to a fixed width, so sorting the names sorts them by time.
    /// The <c>dumps</c> subfolder holds the matching binary dumps, which are not
    /// readable text and are ignored.
    /// </summary>
    public const string CrashReportsDir = "atmosphere/crash_reports";

    /// <summary>The launcher's download scratch space.</summary>
    public const string DownloadsDir = "downloads";

    /// <summary>
    /// The three folders a stock install owns. Hash verification and the reverse
    /// "unexpected files" pass are scoped to these, matching the launcher's verify.
    /// </summary>
    public static readonly IReadOnlyList<string> HdrOwnedFolders =
    [
        HdrDir,
        HdrStagesDir,
        HdrAssetsDir,
    ];

    /// <summary>
    /// Mod folder names that are HDR's own, in any of their build-channel spellings.
    /// Everything else under <see cref="ModsDir"/> is a third-party mod.
    /// </summary>
    public static readonly IReadOnlySet<string> HdrModFolderNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hdr", "hdr-assets", "hdr-stages", "hdr-dev", "hdr-pr", "hdr-private",
        };

    // ---- Plugins -------------------------------------------------------------

    /// <summary>
    /// Plugins the packager installs. The first three are additionally checked by
    /// the mod itself at boot; their absence is a crash, not a degradation.
    /// </summary>
    public static readonly IReadOnlyList<RequiredPlugin> RequiredPlugins =
    [
        new RequiredPlugin("libsmashline_plugin.nro", true,
            "Smashline is what installs every one of HDR's fighter scripts. Without it the game will almost certainly crash on boot."),
        new RequiredPlugin("libarcropolis.nro", true,
            "ARCropolis is the mod loader. Without it HDR's files are never loaded at all — the game will either crash or run as unmodified Smash."),
        new RequiredPlugin("libnro_hook.nro", true,
            "nro-hook is what lets Smashline attach to the game's fighter modules. Without it the game will likely crash on boot."),
        new RequiredPlugin("libstage_alts.nro", false,
            "This plugin provides HDR's alternate stage variants. Without it stage alts are unavailable and stage selection may misbehave."),
        new RequiredPlugin("libstage_config.nro", false,
            "This plugin applies HDR's per-stage settings (hazards, gravity, collisions). Without it stages behave with vanilla settings."),
    ];

    /// <summary>
    /// Plugins that must not be in the active plugins folder. Ported from the
    /// launcher's always-disable list plus HDR's own stale-plugin check.
    /// </summary>
    public static readonly IReadOnlyList<ConflictingPlugin> ConflictingPlugins =
    [
        new ConflictingPlugin("libparam_config.nro", Model.Severity.Critical,
            "param_config conflicts directly with HDR — both try to own the same fighter parameters. It has to go."),
        new ConflictingPlugin("libhdr.nro", Model.Severity.Critical,
            "This is HDR from an older packaging layout, where the plugin lived in the plugins folder. Modern HDR loads from ultimate/mods/hdr/plugin.nro instead, so having both means two copies of HDR fighting over the same hooks."),
        new ConflictingPlugin("libsmashline_hook_development.nro", Model.Severity.Error,
            "A development build of the Smashline hook, left over from an older install. It conflicts with the current libsmashline_plugin.nro."),
        new ConflictingPlugin("libHDR-Launcher.nro", Model.Severity.Error,
            "An old copy of the HDR launcher plugin. The current one is named hdr-launcher.nro; having both loads the launcher twice."),
        new ConflictingPlugin("libnn_hid_hook.nro", Model.Severity.Error,
            "A controller-input hook that is no longer used by HDR and interferes with its input handling."),
        new ConflictingPlugin("libacmd_hook.nro", Model.Severity.Error,
            "An old animation-command hook, superseded by Smashline. Running both corrupts fighter scripts."),
    ];

    /// <summary>
    /// Plugins that technically still work but are no longer supported
    /// and should be replaced.
    /// </summary>
    public static readonly IReadOnlyList<DeprecatedPlugin> DeprecatedPlugins =
    [
        new DeprecatedPlugin("liblocal_latency_slider.nro", Model.Severity.Warning,
            "A deprecated plugin for latency adjustment in Local Wireless on emulator. It conflicts with ssbu-online-deluxe."),
    ];

    /// <summary>The launcher plugin, which isn't required on emulator, but is on console.</summary>
    public const string LauncherNro = "hdr-launcher.nro";
}

/// <param name="FileName">Plugin file name inside the plugins folder.</param>
/// <param name="CheckedByModItself">
/// True when <c>quick_validate_install()</c> also checks for it, meaning its absence
/// produces an in-game dialog on hardware.
/// </param>
/// <param name="Explanation">What breaks without it, in user language.</param>
public sealed record RequiredPlugin(string FileName, bool CheckedByModItself, string Explanation);

/// <param name="FileName">Plugin file name that must not be active.</param>
/// <param name="Severity">How badly it breaks things when present.</param>
/// <param name="Explanation">Why it conflicts.</param>
public sealed record ConflictingPlugin(string FileName, Model.Severity Severity, string Explanation);

/// <param name="FileName">Plugin file name that is deprecated.</param>
/// <param name="Severity">How badly it breaks things when present.</param>
/// <param name="Explanation">Why it is deprecated.</param>
public sealed record DeprecatedPlugin(string FileName, Model.Severity Severity, string Explanation);
