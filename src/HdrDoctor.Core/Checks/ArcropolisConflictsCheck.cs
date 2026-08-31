using System.Text.Json;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Remediations;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Core.Checks;

/// <summary>
/// Reads the conflict map ARCropolis writes when two or more mods provide the same file,
/// and offers to resolve each conflict by deleting the overlap out of the mods that are
/// not HDR's.
/// </summary>
/// <remarks>
/// The file is <c>sd:/ultimate/arcropolis/conflicts.json</c>, a flat
/// <c>{ "path/inside/the/mod": ["sd:/ultimate/mods/winner", "sd:/ultimate/mods/loser"] }</c>
/// map. A value can name more than two roots — see <c>ARCropolis/src/fs/discover.rs:189</c>,
/// which pushes onto an existing entry.
/// 
/// ARCropolis never deletes this file, so it can describe mods that are no longer on the card.
/// This is checked for and returns an info finding with the option to delete the file.
/// 
/// The winning mod ARCropolis chooses is not necessarily the one we want, so the resolution
/// is broken into two cases:
/// 
/// 1. An HDR folder is involved. The fix is to delete the conflict out of the other mods.
///    This is done automatically. If it really isn't the right choice, the user should know.
/// 2. No HDR folder is involved, so the user is asked which mod to keep, and the other
///    mods' conflicting files are deleted.
/// </remarks>
public sealed class ArcropolisConflictsCheck : ICheck
{
    public const string CheckId = "arcropolis.conflicts";

    public string Id => CheckId;

    public CheckCategory Category => CheckCategory.HdrModFolders;

    public PlatformScope Scope => PlatformScope.Any;

    public string DisplayName => "ARCropolis mod conflicts";

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct)
    {
        var json = await ctx.Source.ReadAllTextAsync(HdrPaths.ConflictsFile, ct).ConfigureAwait(false);

        if (json is null)
        {
            return [Finding.Ok(CheckId, "No ARCropolis conflict file", "ARCropolis has not reported any mod conflicts.")];
        }

        Dictionary<string, List<string>>? map;

        try
        {
            map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
        }
        catch (JsonException e)
        {
            return [UnreadableFile(e.Message)];
        }

        if (map is null || map.Count == 0)
        {
            return [Finding.Ok(CheckId, "No ARCropolis mod conflicts", "The conflict file is present but empty.")];
        }

        var findings = new List<Finding>();
        var stale = new List<StaleConflict>();

        // One recursive walk per mod folder, reused by every group that names it. A mod
        // like hdr-stages.dev turns up in several groups and is tens of thousands of files.
        var listings = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in GroupByMods(map, ct))
        {
            ct.ThrowIfCancellationRequested();
            ctx.Report(DisplayName, string.Join(" / ", group.Mods));

            var live = new List<string>();
            foreach (var mod in group.Mods)
            {
                if (await ctx.Source.DirectoryExistsAsync(ModPath(mod), ct).ConfigureAwait(false))
                {
                    live.Add(mod);
                }
            }

            // One mod left cannot conflict with anything.
            if (live.Count < 2)
            {
                stale.Add(new StaleConflict(
                    group.Files.Count,
                    [.. group.Mods.Except(live, StringComparer.OrdinalIgnoreCase)]));

                continue;
            }

            var finding = await DescribeAsync(ctx, group, live, listings, ct).ConfigureAwait(false);

            if (finding is null)
            {
                // The mods are still here but the files the map names are not.
                stale.Add(new StaleConflict(group.Files.Count, []));
                continue;
            }

            findings.Add(finding);
        }

        if (stale.Count > 0)
        {
            findings.Add(DescribeStale(stale, anyLive: findings.Count > 0));
        }

        if (findings.Count == 0)
        {
            // Every entry named a single mod, so the file never described a conflict.
            // Saying nothing here would read as "not checked" rather than "nothing wrong".
            return
            [
                Finding.Ok(
                    CheckId,
                    "No ARCropolis mod conflicts",
                    "The conflict file describes no conflict between two installed mods."),
            ];
        }

        return findings;
    }

    private static string ModPath(string modFolderName) => $"{HdrPaths.ModsDir}/{modFolderName}";

    /// <param name="Mods">Every mod in the conflict</param>
    /// <param name="Files">The conflicting paths, relative to each mod's own folder.</param>
    private sealed record ConflictGroup(IReadOnlyList<string> Mods, IReadOnlyList<string> Files);

    /// <param name="Files">
    /// How many conflicts this accounted for.
    /// </param>
    /// <param name="Gone">
    /// Only the mods that are no longer on the card. Empty when the mods are all still
    /// installed and it is the files they conflicted over that have been removed.
    /// </param>
    private sealed record StaleConflict(int Files, IReadOnlyList<string> Gone);

    /// <summary>
    /// Groups the mods that provide the same file together, so that a single finding can be
    /// made for each set of mods that conflict on the same files.
    /// </summary>
    private static IReadOnlyList<ConflictGroup> GroupByMods(
        Dictionary<string, List<string>> map,
        CancellationToken ct)
    {
        // Case-insensitive because the same mod can be spelled differently.
        var order = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (file, roots) in map)
        {
            ct.ThrowIfCancellationRequested();

            var mods = roots
                .Select(r => r.TrimEnd('/'))
                .Select(r => r[(r.LastIndexOf('/') + 1)..])
                .Where(m => m.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (mods.Count < 2)
            {
                continue;
            }

            var key = string.Join('\0', mods.OrderBy(m => m, StringComparer.OrdinalIgnoreCase));

            if (!files.TryGetValue(key, out var list))
            {
                files[key] = list = [];
                order[key] = mods;
            }

            list.Add(file);
        }

        return [.. files.Select(kv => new ConflictGroup(order[kv.Key], kv.Value))];
    }

    /// <returns>
    /// Null when the mods are all still installed but none of the files
    /// are still on the card, which makes it a leftover entry rather than a conflict.
    /// </returns>
    private static async Task<Finding?> DescribeAsync(
        ScanContext ctx,
        ConflictGroup group,
        List<string> live,
        Dictionary<string, IReadOnlySet<string>> listings,
        CancellationToken ct)
    {
        var protectedMods = live.Where(HdrPaths.ProtectedModFolderNames.Contains).ToList();
        var deletableMods = live.Where(m => !HdrPaths.ProtectedModFolderNames.Contains(m)).ToList();

        var count = group.Files.Count;
        var overlap = $"{count} file{(count == 1 ? "" : "s")}";
        var names = string.Join(", ", live);
        var paths = live.Select(ModPath).ToList();

        var sample = string.Join("\n", group.Files.Take(5).Select(f => "  " + f))
                     + (count > 5 ? $"\n  ... and {count - 5} more" : string.Empty);

        // Every mod involved is HDR.
        if (deletableMods.Count == 0)
        {
            return new Finding(
                CheckId,
                Severity.Warning,
                $"HDR's own mod folders conflict on {overlap}",
                $"{names} each provide the same {overlap}.",
                "These folders belong to HDR. Manually delete "
                + $"the one you don't want and/or reinstall HDR.\n\nConflicting files:\n{sample}",
                paths);
        }

        // Make sure we only offer fixes for mutable installs.
        List<ModConflictOption>? options = null;

        if (ctx.Source is IMutableInstallSource)
        {
            options = [];

            foreach (var mod in deletableMods)
            {
                options.Add(await PlanAsync(ctx, mod, group.Files, listings, ct).ConfigureAwait(false));
            }

            // A previous cleanup already removed every file this group could give up.
            // Offering the fix anyway would delete nothing and reappear on every rescan.
            if (options.TrueForAll(o => o.Files.Count == 0))
            {
                return null;
            }
        }

        // HDR's folders always win. Otherwise, default to the mod ARCropolis picked, and the user can decide later.
        var keep = protectedMods.Count > 0 ? protectedMods[0] : group.Mods.First(live.Contains);

        var verdict = protectedMods.Count > 0
            ? Shadowing(protectedMods, deletableMods, overlap, sample)
            : ThirdParty(live, keep, overlap, sample);

        return new Finding(
            CheckId,
            verdict.Severity,
            verdict.Title,
            $"{names} each provide the same {overlap}.",
            verdict.Explanation,
            paths,
            options is null ? null : new ModConflictRemediation(keep, options));
    }

    /// <summary>
    /// Conflict details.
    /// </summary>
    private sealed record ConflictVerdict(Severity Severity, string Title, string Explanation);

    /// <summary>"A and B", "A, B and C".</summary>
    private static string AndList(IReadOnlyList<string> names) => names.Count < 2
        ? string.Concat(names)
        : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";

    /// <summary>
    /// An HDR folder is on one side, need to delete the conflict from the other mods.
    /// </summary>
    private static ConflictVerdict Shadowing(
        IReadOnlyList<string> protectedMods,
        List<string> deletableMods,
        string overlap,
        string sample)
    {
        var others = string.Join(", ", deletableMods);
        var hdr = string.Join(", ", protectedMods);
        var many = deletableMods.Count > 1;
        var s = many ? "" : "s";

        return new ConflictVerdict(
            Severity.Error,
            $"{others} conflict{s} with {overlap} of HDR's",
            $"{others} provide{s} {(many ? "their" : "its")} own copy of {overlap} that {hdr} also provides, so only "
            + "one of them is what the game actually runs. Which one wins is ARCropolis's discovery order, not a "
            + "setting, and it can change without you changing anything. This is a common cause of characters or "
            + "stages behaving differently to everyone else's install, and of desyncs online.\n\n"
            + $"The fix deletes the conflict out of {others} and leaves HDR's copy in place. Anything {others} "
            + $"provide{s} that HDR does not is kept.\n\nConflicting files:\n{sample}");
    }

    /// <summary>
    /// No HDR folder is involved, so ask the user.
    /// </summary>
    private static ConflictVerdict ThirdParty(
        IReadOnlyList<string> live,
        string keep,
        string overlap,
        string sample)
    {
        var losers = string.Join(
            ", ",
            live.Where(m => !string.Equals(m, keep, StringComparison.OrdinalIgnoreCase)));

        return new ConflictVerdict(
            Severity.Warning,
            $"{AndList(live)} provide the same {overlap}",
            "None of these are HDR's, so which one wins is your "
            + $"choice. ARCropolis currently loads {keep} and ignores {losers}, so keeping {keep} changes nothing "
            + "about what the game runs and only removes the duplicate. That is what the fix does by default, but "
            + $"you can pick a different one when you apply it.\n\nConflicting files:\n{sample}");
    }

    /// <summary>
    /// Plan a conflict resolution.
    /// </summary>
    private static async Task<ModConflictOption> PlanAsync(
        ScanContext ctx,
        string mod,
        IReadOnlyList<string> conflictingFiles,
        Dictionary<string, IReadOnlySet<string>> listings,
        CancellationToken ct)
    {
        var modPath = ModPath(mod);
        var prefix = modPath + "/";

        if (!listings.TryGetValue(modPath, out var onDisk))
        {
            var walked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in await ctx.Source.ListAsync(modPath, recursive: true, ct).ConfigureAwait(false))
            {
                if (!entry.IsDirectory)
                {
                    walked.Add(entry.RelativePath);
                }
            }

            listings[modPath] = onDisk = walked;
        }

        // The map can name files a previous cleanup already removed, so make sure
        // we only list files on disk.
        var files = conflictingFiles
            .Select(f => prefix + f)
            .Where(onDisk.Contains)
            .ToList();

        var doomed = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

        var wholeFolder = files.Count > 0
                          && !onDisk.Any(f => !doomed.Contains(f) && IsLoadable(f[prefix.Length..]));

        return new ModConflictOption(mod, modPath, files, wholeFolder);
    }

    /// <summary>
    /// Whether ARCropolis would load this file at all.
    /// </summary>
    private static bool IsLoadable(string pathInsideMod)
    {
        if (!pathInsideMod.Contains('/'))
        {
            return false;
        }

        return !pathInsideMod[(pathInsideMod.LastIndexOf('/') + 1)..].StartsWith('.');
    }

    private static Finding UnreadableFile(string error) => new(
        CheckId,
        Severity.Warning,
        "ARCropolis's conflict file could not be read",
        error,
        "ARCropolis writes this file when two mods provide the same file. It seems to be corrupted, so whether any mods "
        + "currently conflict could not be determined. Deleting it is safe because ARCropolis writes a fresh one the next "
        + "time it finds a conflict.",
        [HdrPaths.ConflictsFile],
        new DeleteFileRemediation(
            HdrPaths.ConflictsFile,
            $"Delete {HdrPaths.ConflictsFile}, which cannot be parsed. ARCropolis rewrites it the next time it finds "
            + "a conflict."));

    private static Finding DescribeStale(IReadOnlyList<StaleConflict> stale, bool anyLive)
    {
        var gone = stale
            .SelectMany(g => g.Gone)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var files = stale.Sum(g => g.Files);

        var remediation = anyLive
            ? null
            : new DeleteFileRemediation(
                HdrPaths.ConflictsFile,
                $"Delete {HdrPaths.ConflictsFile}. Every conflict it describes has already been resolved. "
                + "ARCropolis writes a fresh file the next time it finds a real one.");

        var detail = gone.Count > 0
            ? $"{files} recorded conflict{(files == 1 ? " names" : "s name")} mods that are no longer installed: "
              + string.Join(", ", gone)
            : files == 1
                ? "1 recorded conflict no longer names a file that is on the card."
                : $"{files} recorded conflicts no longer name files that are on the card.";

        return new Finding(
            CheckId,
            Severity.Info,
            anyLive
                ? "Part of ARCropolis's conflict file is out of date"
                : "ARCropolis's conflict file is out of date",
            detail,
            "ARCropolis writes this file when it finds a conflict and never removes it afterwards, so it goes on "
            + "describing conflicts long after they have been resolved. These entries no longer match what is on the "
            + "card and have been disregarded."
            + (anyLive
                ? " The rest of the file still describes real conflicts, reported above."
                : " Nothing left in the file describes a conflict that still exists."),
            [HdrPaths.ConflictsFile],
            remediation);
    }
}
