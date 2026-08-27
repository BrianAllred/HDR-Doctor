using HdrDoctor.Core;
using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Tests;

public class PathResolutionTests
{
    [Fact]
    public async Task Lowercase_title_id_on_disk_resolves_from_the_canonical_uppercase_path()
    {
        // HDR's own Rust code looks for the lowercase spelling while the packager
        // writes uppercase. Both must find the same install.
        using var sd = new SdFixture()
            .WithFile("atmosphere/contents/01006a800016e000/romfs/skyline/plugins/libarcropolis.nro", "x");

        await using var source = sd.Source();

        Assert.True(await source.FileExistsAsync(
            $"{HdrPaths.PluginsDir}/libarcropolis.nro",
            CancellationToken.None));
    }

    [Fact]
    public void Resolving_a_missing_path_still_names_where_it_was_expected()
    {
        using var sd = new SdFixture();
        var resolver = new CaseInsensitivePathResolver(sd.Root);

        var resolved = resolver.Resolve("ultimate/mods/hdr/plugin.nro");

        Assert.EndsWith(Path.Combine("ultimate", "mods", "hdr", "plugin.nro"), resolved);
    }

    [Fact]
    public async Task A_folder_that_is_not_an_sd_root_is_reported_before_anything_else()
    {
        using var sd = new SdFixture().WithDirectory("some/unrelated/folder");

        var findings = await sd.RunAsync(new InstallLayoutCheck());

        var finding = findings.Single("does not look like an SD card root");
        Assert.Equal(Severity.Critical, finding.Severity);
    }

    [Fact]
    public async Task Listing_returns_paths_relative_to_the_install_root()
    {
        using var sd = new SdFixture().WithFile("ultimate/mods/hdr/plugin.nro", "x");

        await using var source = sd.Source();
        var entries = await source.ListAsync("ultimate/mods", recursive: true, CancellationToken.None);

        Assert.Contains(entries, e => e.RelativePath == "ultimate/mods/hdr/plugin.nro");
    }
}
