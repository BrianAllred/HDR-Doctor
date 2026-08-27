using HdrDoctor.Core;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

public class VersionAndManifestTests
{
    [Theory]
    [InlineData("v9.9.9-prerelease", ReleaseChannel.PreRelease, "HDR-PreReleases")]
    [InlineData("v7.7.7-beta", ReleaseChannel.Beta, "HDR-Releases")]
    [InlineData("v1.69.420-dev", ReleaseChannel.Unknown, null)]
    [InlineData("v0.1.0-private", ReleaseChannel.Unknown, null)]
    public void Channel_is_derived_from_the_version_suffix(string version, ReleaseChannel expected, string? repo)
    {
        var channel = HdrVersionInfo.ChannelFor(version);

        Assert.Equal(expected, channel);
        Assert.Equal(repo, HdrVersionInfo.RepositoryFor(channel));
    }

    [Fact]
    public void Release_tag_strips_the_channel_suffix()
    {
        var info = new HdrVersionInfo("v9.9.9-prerelease", "v8.8.8", ReleaseChannel.PreRelease, HdrPaths.HdrDir);

        Assert.Equal("v9.9.9", info.ReleaseTag);
    }

    [Fact]
    public async Task Main_mod_folder_is_found_by_contents_not_by_name()
    {
        // CI names this folder hdr-pr for pull-request builds, so matching on "hdr"
        // alone would report a perfectly good install as missing.
        using var sd = new SdFixture()
            .WithFile($"{HdrPaths.HdrPrDir}/plugin.nro", "plugin")
            .WithFile($"{HdrPaths.HdrPrDir}/ui/hdr_version.txt", "v9.9.9-prerelease");

        await using var source = sd.Source();
        var info = await HdrVersionReader.ReadAsync(source, CancellationToken.None);

        Assert.Equal(HdrPaths.HdrPrDir, info.MainModFolder);
        Assert.Equal("v9.9.9-prerelease", info.PluginVersion);
    }

    [Fact]
    public async Task The_official_folder_wins_when_several_builds_are_installed()
    {
        using var sd = new SdFixture()
            .WithFile($"{HdrPaths.HdrDevDir}/plugin.nro", "plugin")
            .WithFile($"{HdrPaths.HdrDevDir}/ui/hdr_version.txt", "v1.69.420-dev")
            .WithFile($"{HdrPaths.HdrDir}/plugin.nro", "plugin")
            .WithFile(HdrPaths.HdrVersionFile, "v9.9.9-prerelease");

        await using var source = sd.Source();
        var info = await HdrVersionReader.ReadAsync(source, CancellationToken.None);

        Assert.Equal(HdrPaths.HdrDir, info.MainModFolder);
    }

    [Fact]
    public void Manifest_paths_are_normalized_to_the_apps_relative_form()
    {
        var manifest = ReleaseManifest.Parse(
            """
            [
              {"path": "/ultimate/mods/hdr/config.json", "hash": "ABCDEF"},
              {"path": "\\ultimate\\mods\\hdr\\plugin.nro", "hash": "123456"}
            ]
            """,
            "v9.9.9");

        Assert.Equal(2, manifest.FileCount);
        Assert.True(manifest.Contains("ultimate/mods/hdr/config.json"));
        Assert.True(manifest.Contains("ultimate/mods/hdr/plugin.nro"));

        Assert.True(manifest.TryGetHash("ultimate/mods/hdr/config.json", out var hash));
        Assert.Equal("abcdef", hash);
    }

    [Fact]
    public void An_empty_manifest_is_rejected_rather_than_treated_as_nothing_to_check()
    {
        // Silently accepting this would report a totally broken install as clean.
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Parse("[]", "v1"));
    }
}
