using HdrDoctor.Core;
using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Remediations;

namespace HdrDoctor.Tests;

/// <summary>
/// Covers the conflict map ARCropolis writes to
/// <c>ultimate/arcropolis/conflicts.json</c>. The cases that matter are the two the
/// file's shape invites getting wrong: it outlives the conflict it describes, and the
/// mod it names first is the one it kept, which on a real card is routinely a dev
/// folder shadowing HDR's own.
/// </summary>
public sealed class ArcropolisConflictsTests
{
    private const string SkinPack = "tx skin pack season 6";

    /// <summary>Writes a conflicts.json in ARCropolis's own shape.</summary>
    private static SdFixture WithConflicts(SdFixture sd, params (string File, string[] Mods)[] conflicts)
    {
        var entries = conflicts.Select(c =>
        {
            var roots = string.Join(",", c.Mods.Select(m => $"\"sd:/{HdrPaths.ModsDir}/{m}\""));
            return $"\"{c.File}\":[{roots}]";
        });

        return sd.WithFile(HdrPaths.ConflictsFile, $"{{{string.Join(",", entries)}}}");
    }

    private static Task<IReadOnlyList<Finding>> RunAsync(SdFixture sd) =>
        sd.RunAsync(new ArcropolisConflictsCheck());

    [Fact]
    public async Task No_conflict_file_is_ok()
    {
        using var sd = new SdFixture().WithHealthyInstall();

        Assert.Empty((await RunAsync(sd)).Problems());
    }

    [Fact]
    public async Task Conflicts_naming_removed_mods_are_not_reported_as_conflicts()
    {
        // The real shape of a stale file: ARCropolis kept hdr-stages.dev over
        // hdr-stages months ago, and .dev has since been deleted. Nothing conflicts
        // any more, and reporting it would be reporting a folder that isn't there.
        using var sd = new SdFixture().WithHealthyInstall();
        WithConflicts(sd, ("stage/battlefield/normal/model/x/model.numshb", ["hdr-stages.dev", "hdr-stages"]));

        var findings = await RunAsync(sd);
        var stale = findings.Single("out of date");

        Assert.Equal(Severity.Info, stale.Severity);
        Assert.Contains("hdr-stages.dev", stale.Detail);
        Assert.DoesNotContain(findings, f => f.Severity is Severity.Error or Severity.Warning);
    }

    [Fact]
    public async Task A_wholly_stale_file_can_be_deleted()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        WithConflicts(sd, ("fighter/roy/motion/body/c00/a.nuanmb", ["hdr-assets.dev", "hdr-assets"]));

        var fix = (await RunAsync(sd)).Single("out of date").Remediation;

        Assert.NotNull(fix);

        await using var source = sd.Source();
        await fix.ApplyAsync(source, CancellationToken.None);

        Assert.False(await source.FileExistsAsync(HdrPaths.ConflictsFile, CancellationToken.None));
    }

    [Fact]
    public async Task A_file_with_live_conflicts_left_in_it_is_not_offered_for_deletion()
    {
        // Deleting the file here would throw away the description of the live conflict
        // along with the stale one.
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile($"{HdrPaths.ModsDir}/{SkinPack}/fighter/mario/model/body/c00/model.numatb", "theirs");
        sd.WithFile($"{HdrPaths.HdrStagesDir}/stage/battlefield/normal/model/x/model.numshb", "hdr");

        WithConflicts(
            sd,
            ("fighter/mario/model/body/c00/model.numatb", [SkinPack, "hdr-stages"]),
            ("stage/battlefield/normal/model/x/model.numshb", ["hdr-stages.dev", "hdr-stages"]));

        var findings = await RunAsync(sd);

        Assert.Null(findings.Single("out of date").Remediation);
        Assert.True(findings.Has("HDR"));
    }

    [Fact]
    public async Task A_mod_shadowing_HDR_is_an_error_and_HDR_is_never_the_one_deleted()
    {
        // ARCropolis kept the skin pack, so HDR's file is the one being ignored.
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile($"{HdrPaths.ModsDir}/{SkinPack}/fighter/mario/model/body/c00/model.numatb", "theirs");
        sd.WithFile($"{HdrPaths.ModsDir}/{SkinPack}/fighter/mario/model/body/c01/model.numatb", "unique");
        sd.WithFile($"{HdrPaths.HdrAssetsDir}/fighter/mario/model/body/c00/model.numatb", "hdr");

        WithConflicts(sd, ("fighter/mario/model/body/c00/model.numatb", [SkinPack, "hdr-assets"]));

        var finding = (await RunAsync(sd)).Single("conflicts with");

        Assert.Equal(Severity.Error, finding.Severity);

        var fix = Assert.IsType<ModConflictRemediation>(finding.Remediation);
        Assert.Equal("hdr-assets", fix.Keep);

        // No choice to offer: HDR settled it.
        Assert.Empty(fix.Choices);

        await using var source = sd.Source();
        await fix.ApplyAsync(source, CancellationToken.None);

        Assert.True(await source.FileExistsAsync(
            $"{HdrPaths.HdrAssetsDir}/fighter/mario/model/body/c00/model.numatb", CancellationToken.None));
        Assert.False(await source.FileExistsAsync(
            $"{HdrPaths.ModsDir}/{SkinPack}/fighter/mario/model/body/c00/model.numatb", CancellationToken.None));

        // What the skin pack alone provided is not collateral.
        Assert.True(await source.FileExistsAsync(
            $"{HdrPaths.ModsDir}/{SkinPack}/fighter/mario/model/body/c01/model.numatb", CancellationToken.None));
    }

    [Fact]
    public async Task A_mod_HDR_currently_beats_is_still_an_error()
    {
        // ARCropolis kept HDR this time. Its order is discovery order and can change on
        // its own, so the conflict is reported the same either way.
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile($"{HdrPaths.ModsDir}/{SkinPack}/fighter/mario/model/body/c00/model.numatb", "theirs");
        sd.WithFile($"{HdrPaths.ModsDir}/{SkinPack}/fighter/mario/model/body/c01/model.numatb", "unique");
        sd.WithFile($"{HdrPaths.HdrAssetsDir}/fighter/mario/model/body/c00/model.numatb", "hdr");

        WithConflicts(sd, ("fighter/mario/model/body/c00/model.numatb", ["hdr-assets", SkinPack]));

        var finding = (await RunAsync(sd)).Single("conflicts with");

        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Equal("hdr-assets", Assert.IsType<ModConflictRemediation>(finding.Remediation).Keep);
    }

    [Fact]
    public async Task A_dev_folder_shadowing_the_release_install_is_deleted_out_of()
    {
        // The case every real conflicts.json is full of. hdr-stages.dev is HDR's code
        // but not a protected folder, so the release install wins even though
        // ARCropolis kept the dev copy.
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile($"{HdrPaths.ModsDir}/hdr-stages.dev/stage/battlefield/normal/model/x/model.numshb", "dev");
        sd.WithFile($"{HdrPaths.HdrStagesDir}/stage/battlefield/normal/model/x/model.numshb", "release");

        WithConflicts(sd, ("stage/battlefield/normal/model/x/model.numshb", ["hdr-stages.dev", "hdr-stages"]));

        var fix = Assert.IsType<ModConflictRemediation>(
            (await RunAsync(sd)).Single("conflicts with").Remediation);

        Assert.Equal("hdr-stages", fix.Keep);
        Assert.Equal("hdr-stages.dev", Assert.Single(fix.Losers).ModFolderName);
    }

    [Fact]
    public async Task A_mod_left_with_nothing_loadable_is_deleted_whole()
    {
        // Everything the mod would have loaded is a conflict. What is left is a
        // top-level info.toml and preview.webp, which ARCropolis ignores outright
        // (discover.rs is_root), so keeping the folder keeps nothing.
        using var sd = new SdFixture().WithHealthyInstall();
        var mod = $"{HdrPaths.ModsDir}/{SkinPack}";

        sd.WithFile($"{mod}/info.toml", "name = 'x'");
        sd.WithFile($"{mod}/preview.webp", "webp");
        sd.WithFile($"{mod}/fighter/mario/model/body/c00/model.numatb", "theirs");
        sd.WithFile($"{HdrPaths.HdrAssetsDir}/fighter/mario/model/body/c00/model.numatb", "hdr");

        WithConflicts(sd, ("fighter/mario/model/body/c00/model.numatb", [SkinPack, "hdr-assets"]));

        var fix = Assert.IsType<ModConflictRemediation>(
            (await RunAsync(sd)).Single("conflicts with").Remediation);

        Assert.True(Assert.Single(fix.Losers).DeleteWholeFolder);

        await using var source = sd.Source();
        await fix.ApplyAsync(source, CancellationToken.None);

        Assert.False(await source.DirectoryExistsAsync(mod, CancellationToken.None));
        Assert.True(await source.DirectoryExistsAsync(HdrPaths.HdrAssetsDir, CancellationToken.None));
    }

    [Fact]
    public async Task Two_third_party_mods_default_to_ARCropolis_s_winner_and_offer_the_other()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile($"{HdrPaths.ModsDir}/Music A/stream;/sound/bgm/x.nus3audio", "a");
        sd.WithFile($"{HdrPaths.ModsDir}/Music A/stream;/sound/bgm/only-a.nus3audio", "a");
        sd.WithFile($"{HdrPaths.ModsDir}/Music B/stream;/sound/bgm/x.nus3audio", "b");
        sd.WithFile($"{HdrPaths.ModsDir}/Music B/stream;/sound/bgm/only-b.nus3audio", "b");

        WithConflicts(sd, ("stream;/sound/bgm/x.nus3audio", ["Music A", "Music B"]));

        var fix = Assert.IsType<ModConflictRemediation>(
            (await RunAsync(sd)).Single("provide the same").Remediation);

        Assert.Equal("Music A", fix.Keep);
        Assert.Equal(["Music A", "Music B"], fix.Choices.OrderBy(c => c));

        // Choosing the other way round deletes out of the other mod.
        var flipped = fix.With("Music B");
        Assert.Equal("Music A", Assert.Single(flipped.Losers).ModFolderName);

        await using var source = sd.Source();
        await flipped.ApplyAsync(source, CancellationToken.None);

        Assert.False(await source.FileExistsAsync(
            $"{HdrPaths.ModsDir}/Music A/stream;/sound/bgm/x.nus3audio", CancellationToken.None));
        Assert.True(await source.FileExistsAsync(
            $"{HdrPaths.ModsDir}/Music A/stream;/sound/bgm/only-a.nus3audio", CancellationToken.None));
        Assert.True(await source.FileExistsAsync(
            $"{HdrPaths.ModsDir}/Music B/stream;/sound/bgm/x.nus3audio", CancellationToken.None));
    }

    [Fact]
    public async Task Two_HDR_folders_conflicting_with_each_other_offers_no_fix()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile($"{HdrPaths.HdrDir}/fighter/mario/model/body/c00/model.numatb", "one");
        sd.WithFile($"{HdrPaths.HdrAssetsDir}/fighter/mario/model/body/c00/model.numatb", "two");

        WithConflicts(sd, ("fighter/mario/model/body/c00/model.numatb", ["hdr", "hdr-assets"]));

        var finding = (await RunAsync(sd)).Single("HDR's own mod folders conflict");

        Assert.Null(finding.Remediation);
    }

    [Fact]
    public async Task Files_the_map_names_but_the_card_no_longer_has_are_not_offered_for_deletion()
    {
        // A half-finished manual cleanup: the map still lists both files, only one is
        // there. Offering to delete a file that is gone would overstate the fix.
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile($"{HdrPaths.ModsDir}/{SkinPack}/fighter/mario/model/body/c00/model.numatb", "theirs");
        sd.WithFile($"{HdrPaths.HdrAssetsDir}/fighter/mario/model/body/c00/model.numatb", "hdr");
        sd.WithFile($"{HdrPaths.HdrAssetsDir}/fighter/mario/model/body/c01/model.numatb", "hdr");

        WithConflicts(
            sd,
            ("fighter/mario/model/body/c00/model.numatb", [SkinPack, "hdr-assets"]),
            ("fighter/mario/model/body/c01/model.numatb", [SkinPack, "hdr-assets"]));

        var fix = Assert.IsType<ModConflictRemediation>(
            (await RunAsync(sd)).Single("conflicts with").Remediation);

        Assert.Equal(
            [$"{HdrPaths.ModsDir}/{SkinPack}/fighter/mario/model/body/c00/model.numatb"],
            Assert.Single(fix.Losers).Files);
    }

    [Fact]
    public async Task An_unparseable_conflict_file_is_reported_rather_than_thrown()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile(HdrPaths.ConflictsFile, "{ this is not json");

        var finding = (await RunAsync(sd)).Single("could not be read");

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.NotNull(finding.Remediation);
    }

    [Fact]
    public async Task A_mod_left_holding_only_a_patch_file_is_not_deleted_whole()
    {
        // ARCropolis hands the patch extensions to launchpad.collecting(), which is what
        // makes them merge instead of compete — they are still loaded. A mod whose only
        // survivor is a .prcxml is a working mod, so the folder stays.
        using var sd = new SdFixture().WithHealthyInstall();
        var mod = $"{HdrPaths.ModsDir}/{SkinPack}";

        sd.WithFile($"{mod}/info.toml", "name = 'x'");
        sd.WithFile($"{mod}/fighter/mario/model/body/c00/model.numatb", "theirs");
        sd.WithFile($"{mod}/fighter/mario/param/vl.prcxml", "patch");
        sd.WithFile($"{HdrPaths.HdrAssetsDir}/fighter/mario/model/body/c00/model.numatb", "hdr");

        WithConflicts(sd, ("fighter/mario/model/body/c00/model.numatb", [SkinPack, "hdr-assets"]));

        var fix = Assert.IsType<ModConflictRemediation>(
            (await RunAsync(sd)).Single("conflicts with").Remediation);

        Assert.False(Assert.Single(fix.Losers).DeleteWholeFolder);

        await using var source = sd.Source();
        await fix.ApplyAsync(source, CancellationToken.None);

        Assert.True(await source.FileExistsAsync($"{mod}/fighter/mario/param/vl.prcxml", CancellationToken.None));
        Assert.False(await source.FileExistsAsync(
            $"{mod}/fighter/mario/model/body/c00/model.numatb", CancellationToken.None));
    }

    [Fact]
    public async Task Two_HDR_folders_conflicting_lists_the_files_they_conflict_on()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile($"{HdrPaths.HdrDir}/fighter/mario/model/body/c00/model.numatb", "one");
        sd.WithFile($"{HdrPaths.HdrAssetsDir}/fighter/mario/model/body/c00/model.numatb", "two");

        WithConflicts(sd, ("fighter/mario/model/body/c00/model.numatb", ["hdr", "hdr-assets"]));

        var finding = (await RunAsync(sd)).Single("HDR's own mod folders conflict");

        Assert.Contains("fighter/mario/model/body/c00/model.numatb", finding.Explanation);
        Assert.DoesNotContain("{sample}", finding.Explanation);
    }

    [Fact]
    public async Task A_conflict_whose_files_are_all_gone_is_not_offered_as_a_fix()
    {
        // Both mods are still installed, but the file they conflicted over has already
        // been cleaned out of the one that could give it up. A fix here deletes nothing
        // and would come back on every rescan.
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile($"{HdrPaths.ModsDir}/{SkinPack}/fighter/mario/model/body/c01/model.numatb", "unrelated");
        sd.WithFile($"{HdrPaths.HdrAssetsDir}/fighter/mario/model/body/c00/model.numatb", "hdr");

        WithConflicts(sd, ("fighter/mario/model/body/c00/model.numatb", [SkinPack, "hdr-assets"]));

        var findings = await RunAsync(sd);

        Assert.DoesNotContain(findings, f => f.Severity is Severity.Error);
        Assert.Equal(Severity.Info, findings.Single("out of date").Severity);
    }

    [Fact]
    public async Task A_file_recorded_against_a_single_mod_still_leaves_the_check_visible()
    {
        // One root cannot conflict with anything, so there is nothing to report — but
        // returning no finding at all reads as "not checked" rather than "nothing wrong".
        using var sd = new SdFixture().WithHealthyInstall();
        WithConflicts(sd, ("fighter/mario/model/body/c00/model.numatb", ["hdr-assets"]));

        var findings = await RunAsync(sd);

        Assert.Empty(findings.Problems());
        Assert.Equal(Severity.Ok, findings.Single("No ARCropolis mod conflicts").Severity);
    }

    [Fact]
    public async Task Mod_names_containing_spaces_do_not_collapse_into_one_conflict()
    {
        // Joining the names with a space to key the group makes {"mario skin", "roy voice"}
        // and {"mario", "skin roy voice"} the same key, which reports one conflict naming
        // the wrong folders and plans deletions against a mod that never provided the file.
        using var sd = new SdFixture().WithHealthyInstall();

        foreach (var mod in (string[])["mario skin", "roy voice"])
        {
            sd.WithFile($"{HdrPaths.ModsDir}/{mod}/fighter/mario/model/body/c00/model.numatb", mod);
        }

        foreach (var mod in (string[])["mario", "skin roy voice"])
        {
            sd.WithFile($"{HdrPaths.ModsDir}/{mod}/fighter/mario/model/body/c01/model.numatb", mod);
        }

        // Two conflicts over two different files, between two pairs of mods whose names
        // differ only in where the spaces fall.
        sd.WithFile(
            HdrPaths.ConflictsFile,
            $$"""
              {
                "fighter/mario/model/body/c00/model.numatb":
                  ["sd:/{{HdrPaths.ModsDir}}/mario skin","sd:/{{HdrPaths.ModsDir}}/roy voice"],
                "fighter/mario/model/body/c01/model.numatb":
                  ["sd:/{{HdrPaths.ModsDir}}/mario","sd:/{{HdrPaths.ModsDir}}/skin roy voice"]
              }
              """);

        var findings = await RunAsync(sd);

        Assert.Equal(2, findings.Count(f => f.Title.Contains("provide the same")));
    }

    [Fact]
    public async Task Three_mods_conflicting_is_not_described_as_two()
    {
        using var sd = new SdFixture().WithHealthyInstall();

        foreach (var mod in (string[])["Music A", "Music B", "Music C"])
        {
            sd.WithFile($"{HdrPaths.ModsDir}/{mod}/stream;/sound/bgm/x.nus3audio", mod);
        }

        WithConflicts(sd, ("stream;/sound/bgm/x.nus3audio", ["Music A", "Music B", "Music C"]));

        var finding = (await RunAsync(sd)).Single("provide the same");

        Assert.Equal("Music A, Music B and Music C provide the same 1 file", finding.Title);
        Assert.Contains("None of these are HDR's", finding.Explanation);
    }

    [Fact]
    public async Task Two_mods_shadowing_HDR_are_described_in_the_plural()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile($"{HdrPaths.ModsDir}/{SkinPack}/fighter/mario/model/body/c00/model.numatb", "a");
        sd.WithFile($"{HdrPaths.ModsDir}/other pack/fighter/mario/model/body/c00/model.numatb", "b");
        sd.WithFile($"{HdrPaths.HdrAssetsDir}/fighter/mario/model/body/c00/model.numatb", "hdr");

        WithConflicts(
            sd,
            ("fighter/mario/model/body/c00/model.numatb", [SkinPack, "other pack", "hdr-assets"]));

        var finding = (await RunAsync(sd)).Single("conflict with");

        Assert.Contains($"{SkinPack}, other pack conflict with 1 file of HDR's", finding.Title);
        Assert.Contains("provide their own copy", finding.Explanation);
    }

    [Fact]
    public void A_keep_spelled_with_different_casing_still_names_the_other_mod_as_the_loser()
    {
        // Every other comparison in this feature is case-insensitive. An ordinal one here
        // makes Choices empty and puts the kept mod in Losers, deleting out of the winner.
        List<ModConflictOption> options =
        [
            new("Music A", $"{HdrPaths.ModsDir}/Music A", ["a"], false),
            new("Music B", $"{HdrPaths.ModsDir}/Music B", ["b"], false),
        ];

        var fix = new ModConflictRemediation("music b", options);

        Assert.Equal("Music A", Assert.Single(fix.Losers).ModFolderName);
        Assert.Equal(2, fix.Choices.Count);
    }
}
