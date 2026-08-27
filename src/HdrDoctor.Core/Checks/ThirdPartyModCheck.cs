using HdrDoctor.Core.Model;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Core.Checks;

/// <summary>
/// Classifies the other mods installed alongside HDR by how much trouble they cause.
/// </summary>
/// <remarks>
/// Not every extra mod is a problem.
/// Three levels, based on what the folder actually contains:
///
/// A folder with its own <c>plugin.nro</c> is a code mod. These are almost never compatible with HDR.
///
/// A folder that replaces fighter parameters, motion, or scripts changes how the game
/// plays without running code. Probably won't crash, but usually not wifi-safe and
/// can cause desyncs online.
///
/// A folder that only replaces models, textures, UI art, or sound is cosmetic and
/// almost always fine.
/// </remarks>
public sealed class ThirdPartyModCheck : ICheck
{
    public string Id => "third-party-mods";

    public CheckCategory Category => CheckCategory.HdrModFolders;

    public PlatformScope Scope => PlatformScope.Any;

    public string DisplayName => "Other installed mods";

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var mods = await ctx.Source.ListAsync(HdrPaths.ModsDir, recursive: false, ct).ConfigureAwait(false);

        var codeMods = new List<string>();
        var gameplayMods = new List<string>();
        var cosmeticMods = new List<string>();
        var evidence = new List<string>();

        foreach (var mod in mods.Where(e => e.IsDirectory))
        {
            ct.ThrowIfCancellationRequested();

            if (HdrPaths.HdrModFolderNames.Contains(mod.Name))
            {
                continue;
            }

            ctx.Report("Inspecting other mods", mod.Name);

            var classification = await ClassifyAsync(ctx, mod, ct).ConfigureAwait(false);

            switch (classification.Kind)
            {
                case ModKind.Code:
                    codeMods.Add(mod.Name);
                    break;
                case ModKind.Gameplay:
                    gameplayMods.Add(mod.Name);
                    evidence.AddRange(classification.Evidence.Select(e => $"{mod.Name}: {e}"));
                    break;
                default:
                    cosmeticMods.Add(mod.Name);
                    break;
            }
        }

        var removalAdvice = ctx.Platform == InstallPlatform.Switch
            ? "On a Switch you can either delete the folder or turn the mod off in ARCropolis's mod manager — "
              + "disabling it is enough."
            : "On an emulator there is no mod manager to turn it off with, so the folder has to be removed.";

        if (codeMods.Count > 0)
        {
            findings.Add(new Finding(
                Id,
                Severity.Error,
                codeMods.Count == 1
                    ? $"Custom moveset mod installed: {codeMods[0]}"
                    : $"{codeMods.Count} custom moveset mods installed",
                string.Join(", ", codeMods),
                "Each of these ships its own plugin.nro, which means it runs code inside the game just like HDR does. "
                + $"{removalAdvice}",
                [.. codeMods.Select(m => $"{HdrPaths.ModsDir}/{m}")]));
        }

        if (gameplayMods.Count > 0)
        {
            findings.Add(new Finding(
                Id,
                Severity.Warning,
                gameplayMods.Count == 1
                    ? $"Gameplay-affecting mod installed: {gameplayMods[0]}"
                    : $"{gameplayMods.Count} gameplay-affecting mods installed",
                string.Join(", ", gameplayMods),
                "These replace fighter parameters or scripts, which can change how characters behave. They won't "
                + "usually crash the game, but you might desync against anyone who does not have exactly the same "
                + $"files. {removalAdvice}\n\nWhat led to this verdict:\n"
                + string.Join("\n", evidence.Take(10).Select(e => "  " + e)),
                [.. gameplayMods.Select(m => $"{HdrPaths.ModsDir}/{m}")]));
        }

        if (cosmeticMods.Count > 0)
        {
            findings.Add(new Finding(
                Id,
                Severity.Info,
                cosmeticMods.Count == 1
                    ? $"One cosmetic mod installed: {cosmeticMods[0]}"
                    : $"{cosmeticMods.Count} cosmetic mods installed",
                string.Join(", ", cosmeticMods),
                "These only replace models, textures, UI art, or sound. They are normally safe to keep, including "
                + "online. If you are troubleshooting a crash, it's still worth turning them off to rule them out.",
                [.. cosmeticMods.Select(m => $"{HdrPaths.ModsDir}/{m}")]));
        }

        return findings;
    }

    private enum ModKind
    {
        Cosmetic,
        Gameplay,
        Code,
    }

    /// <param name="Kind">How disruptive the mod is.</param>
    /// <param name="Evidence">
    /// The files that led to the verdict. Naming them lets the user judge for
    /// themselves rather than taking a one-word label on trust.
    /// </param>
    private sealed record Classification(ModKind Kind, IReadOnlyList<string> Evidence);

    private static async Task<Classification> ClassifyAsync(ScanContext ctx, SourceEntry mod, CancellationToken ct)
    {
        // A plugin.nro means the mod runs its own code. Nothing else needs checking.
        var pluginPath = $"{mod.RelativePath}/plugin.nro";
        if (await ctx.Source.FileExistsAsync(pluginPath, ct).ConfigureAwait(false))
        {
            return new Classification(ModKind.Code, [pluginPath]);
        }

        var entries = await ctx.Source.ListAsync(mod.RelativePath, recursive: true, ct).ConfigureAwait(false);
        var prefix = mod.RelativePath + "/";
        var evidence = new List<string>();

        foreach (var entry in entries.Where(e => !e.IsDirectory))
        {
            var relative = entry.RelativePath.StartsWith(prefix, StringComparison.Ordinal)
                ? entry.RelativePath[prefix.Length..]
                : entry.RelativePath;

            if (IsGameplayFile(relative))
            {
                evidence.Add(relative);
                if (evidence.Count >= 5)
                {
                    break;
                }
            }
        }

        return evidence.Count > 0
            ? new Classification(ModKind.Gameplay, evidence)
            : new Classification(ModKind.Cosmetic, []);
    }

    /// <summary>
    /// True for files that change how the game plays, as opposed to how it looks.
    /// </summary>
    /// <remarks>
    /// The distinction is finer than "is it under fighter/". Skin packs routinely ship
    /// per-costume physics (<c>motion/.../swing.prc</c>, <c>update.prc</c>) that drive
    /// cloth and hair on an alternate model — those are cosmetic, and calling them a
    /// desync risk would make this tool cry wolf on the single most common kind of mod
    /// people install. What genuinely matters is parameter files and fighter scripts.
    /// </remarks>
    private static bool IsGameplayFile(string relativePath)
    {
        var fileName = relativePath[(relativePath.LastIndexOf('/') + 1)..];

        if (relativePath.StartsWith("fighter/", StringComparison.OrdinalIgnoreCase))
        {
            var withoutFighter = relativePath["fighter/".Length..];
            var slash = withoutFighter.IndexOf('/');
            if (slash >= 0)
            {
                var subPath = withoutFighter[(slash + 1)..];

                if (subPath.StartsWith("param/", StringComparison.OrdinalIgnoreCase) ||
                    subPath.StartsWith("script/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Under motion/, only the shared motion list changes behavior. The
                // per-slot physics files next to it are costume dressing.
                if (subPath.StartsWith("motion/", StringComparison.OrdinalIgnoreCase))
                {
                    return fileName.StartsWith("motion_list", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        if (relativePath.StartsWith("param/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Parameter patches outside the UI change gameplay values.
        return (relativePath.EndsWith(".prcxml", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".prc", StringComparison.OrdinalIgnoreCase))
               && !relativePath.StartsWith("ui/", StringComparison.OrdinalIgnoreCase)
               && !relativePath.Contains("/motion/", StringComparison.OrdinalIgnoreCase);
    }
}
