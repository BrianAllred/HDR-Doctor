using HdrDoctor.Core.Model;
using HdrDoctor.Core.Remediations;

namespace HdrDoctor.Core.Checks;

/// <summary>
/// Validates the contents of <c>romfs/skyline/plugins</c>, plus Skyline itself.
/// </summary>
/// <remarks>
/// The required-plugin list and its failure messages mirror HDR's own boot-time
/// self-check (<c>src/lib.rs quick_validate_install()</c>).
///
/// Anything unrecognized is reported at Info level.
/// </remarks>
public sealed class SkylinePluginsCheck : ICheck
{
    public string Id => "plugins";

    public CheckCategory Category => CheckCategory.SkylinePlugins;

    public PlatformScope Scope => PlatformScope.Any;

    public string DisplayName => "Skyline plugins";

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct)
    {
        var findings = new List<Finding>();

        await CheckSkylineItselfAsync(ctx, findings, ct).ConfigureAwait(false);

        var present = await ListPluginsAsync(ctx, HdrPaths.PluginsDir, ct).ConfigureAwait(false);
        var disabled = await ListPluginsAsync(ctx, HdrPaths.DisabledPluginsDir, ct).ConfigureAwait(false);

        if (!await ctx.Source.DirectoryExistsAsync(HdrPaths.PluginsDir, ct).ConfigureAwait(false))
        {
            findings.Add(new Finding(
                Id,
                Severity.Critical,
                "Skyline plugins folder is missing",
                $"{HdrPaths.PluginsDir} does not exist.",
                "Nothing that makes HDR work is installed. The game will boot as vanilla Smash, if it boots at all. "
                + "Install the full HDR package.",
                [HdrPaths.PluginsDir]));

            return findings;
        }

        CheckRequired(ctx, present, findings);
        CheckConflicting(ctx, present, disabled, findings);
        await CheckDevelopmentNroAsync(ctx, findings, ct).ConfigureAwait(false);
        CheckUnrecognized(present, findings);

        return findings;
    }

    private async Task CheckSkylineItselfAsync(ScanContext ctx, List<Finding> findings, CancellationToken ct)
    {
        foreach (var path in new[] { HdrPaths.SkylineSubsdk, HdrPaths.SkylineNpdm })
        {
            if (await ctx.Source.FileExistsAsync(path, ct).ConfigureAwait(false))
            {
                continue;
            }

            findings.Add(new Finding(
                Id,
                Severity.Critical,
                $"Skyline is not installed ({Path.GetFileName(path)} missing)",
                $"{path} was not found.",
                "Skyline is the framework every HDR plugin uses. Without these two files the game runs "
                + "completely vanilla. Install the full HDR package.",
                [path]));
        }
    }

    /// <summary>
    /// Reports the plugins HDR needs and does not have.
    /// </summary>
    /// <remarks>
    /// <see cref="HdrPaths.RequiredPlugins"/> is a snapshot of what HDR ships today,
    /// so on its own it would report a release that has since dropped a plugin as
    /// broken. When the release's own manifest describes the plugins folder it is the
    /// better authority and decides which of the list to insist on; the list stays the
    /// fallback for an offline scan or a private build, where insisting on all of them
    /// is still much better than checking none.
    /// </remarks>
    private void CheckRequired(
        ScanContext ctx,
        IReadOnlyDictionary<string, string> present,
        List<Finding> findings)
    {
        var releaseContents = ctx.Manifest?.DescribesFolder(HdrPaths.PluginsDir) == true
            ? ctx.Manifest
            : null;

        foreach (var required in HdrPaths.RequiredPlugins)
        {
            if (present.ContainsKey(required.FileName))
            {
                findings.Add(new Finding(
                    Id,
                    Severity.Ok,
                    $"{required.FileName} present",
                    string.Empty,
                    string.Empty,
                    [$"{HdrPaths.PluginsDir}/{required.FileName}"]));

                continue;
            }

            if (releaseContents is not null &&
                !releaseContents.Contains($"{HdrPaths.PluginsDir}/{required.FileName}"))
            {
                continue;
            }

            findings.Add(new Finding(
                Id,
                required.CheckedByModItself ? Severity.Critical : Severity.Error,
                $"{required.FileName} is missing",
                $"{HdrPaths.PluginsDir}/{required.FileName} was not found.",
                required.Explanation,
                [$"{HdrPaths.PluginsDir}/{required.FileName}"]));
        }

        if (ctx.Platform == InstallPlatform.Switch && !present.ContainsKey(HdrPaths.LauncherNro))
        {
            findings.Add(new Finding(
                Id,
                Severity.Critical,
                "HDR launcher is missing",
                $"{HdrPaths.PluginsDir}/{HdrPaths.LauncherNro} was not found.",
                "Without the launcher you cannot update HDR or change its settings. Install the full HDR package.",
                [$"{HdrPaths.PluginsDir}/{HdrPaths.LauncherNro}"]));
        }
    }

    private void CheckConflicting(
        ScanContext ctx,
        IReadOnlyDictionary<string, string> present,
        IReadOnlyDictionary<string, string> disabled,
        List<Finding> findings)
    {
        foreach (var conflict in HdrPaths.ConflictingPlugins)
        {
            if (present.TryGetValue(conflict.FileName, out var realName))
            {
                var activePath = $"{HdrPaths.PluginsDir}/{realName}";

                // On Switch the plugin can be retired into disabled_plugins rather than
                // deleted. Emulators have no such mechanism, so it has to go entirely.
                IRemediation remediation = ctx.Platform == InstallPlatform.Switch
                    ? new DisablePluginRemediation(realName)
                    : new DeleteFileRemediation(activePath, $"Delete {activePath}.");

                var howToFix = ctx.Platform == InstallPlatform.Switch
                    ? "On a Switch you can either delete it or move it into the disabled_plugins folder — either stops it loading."
                    : "On an emulator there is no disabled_plugins mechanism, so the file has to be removed.";

                findings.Add(new Finding(
                    Id,
                    conflict.Severity,
                    $"{realName} conflicts with HDR",
                    $"{activePath} is active.",
                    $"{conflict.Explanation} {howToFix}",
                    [activePath],
                    remediation));

                continue;
            }

            if (disabled.TryGetValue(conflict.FileName, out var disabledName))
            {
                findings.Add(new Finding(
                    Id,
                    Severity.Ok,
                    $"{disabledName} is present but disabled",
                    $"Found in {HdrPaths.DisabledPluginsDir}, so it will not load.",
                    string.Empty,
                    [$"{HdrPaths.DisabledPluginsDir}/{disabledName}"]));
            }
        }
    }

    private async Task CheckDevelopmentNroAsync(ScanContext ctx, List<Finding> findings, CancellationToken ct)
    {
        if (!await ctx.Source.FileExistsAsync(HdrPaths.DevelopmentNro, ct).ConfigureAwait(false))
        {
            return;
        }

        if (await ctx.Source.DirectoryExistsAsync(HdrPaths.HdrDevDir, ct).ConfigureAwait(false))
        {
            findings.Add(new Finding(
                Id,
                Severity.Warning,
                "A development build is installed",
                $"{HdrPaths.DevelopmentNro} is paired with {HdrPaths.HdrDevDir}.",
                "This is a developer build, not an official release. It is a valid setup, for development and testing,"
                + " but it is not supported for normal play. If you are not a developer, remove this and install the full HDR package.",
                [HdrPaths.DevelopmentNro, HdrPaths.HdrDevDir]));

            return;
        }

        findings.Add(new Finding(
            Id,
            Severity.Error,
            "Orphaned development.nro",
            $"{HdrPaths.DevelopmentNro} exists, but there is no {HdrPaths.HdrDevDir} folder to go with it.",
            "A development.nro replaces part of HDR's code, and it's only valid alongside the hdr-dev mod "
            + "folder it was built with. It will load stale/incorrect code on top of your real install. HDR itself "
            + "flags this at boot and offers to delete it.",
            [HdrPaths.DevelopmentNro],
            new DeleteFileRemediation(HdrPaths.DevelopmentNro, $"Delete {HdrPaths.DevelopmentNro}.")));
    }

    private void CheckUnrecognized(IReadOnlyDictionary<string, string> present, List<Finding> findings)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in HdrPaths.RequiredPlugins)
        {
            known.Add(plugin.FileName);
        }

        foreach (var plugin in HdrPaths.ConflictingPlugins)
        {
            known.Add(plugin.FileName);
        }

        known.Add(HdrPaths.LauncherNro);

        var extras = present
            .Where(p => !known.Contains(p.Key))
            .Select(p => p.Value)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (extras.Count == 0)
        {
            return;
        }

        findings.Add(new Finding(
            Id,
            Severity.Info,
            extras.Count == 1
                ? $"One non-HDR plugin is installed: {extras[0]}"
                : $"{extras.Count} non-HDR plugins are installed",
            string.Join(", ", extras),
            "These are not part of HDR. That is not a problem by default, but if you are experiencing a crash or a desync," 
            + " they are worth examining, and worth temporarily removing to see if the problem goes away.",
            [.. extras.Select(n => $"{HdrPaths.PluginsDir}/{n}")]));
    }

    /// <summary>
    /// Lists .nro files in a plugins folder
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, string>> ListPluginsAsync(
        ScanContext ctx,
        string directory,
        CancellationToken ct)
    {
        var entries = await ctx.Source.ListAsync(directory, recursive: false, ct).ConfigureAwait(false);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.Where(e => !e.IsDirectory))
        {
            map.TryAdd(entry.Name, entry.Name);
        }

        return map;
    }
}
