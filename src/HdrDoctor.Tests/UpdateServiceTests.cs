using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

/// <summary>
/// The asset names and tag parsing are the contract between the updater and
/// <c>.github/workflows/release.yml</c>. A mismatch there is silent — the updater just
/// never finds anything — so it is pinned here rather than discovered after a release.
/// </summary>
public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3-beta.1", "1.2.3")]
    [InlineData("v1.2.3+build7", "1.2.3")]
    [InlineData("v2.0", "2.0")]
    public void Release_tags_read_as_versions(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateService.ParseTag(tag));

    [Theory]
    [InlineData("nightly")]
    [InlineData("")]
    [InlineData("v")]
    public void Tags_that_name_no_version_are_refused(string tag) =>
        Assert.Null(UpdateService.ParseTag(tag));

    [Theory]
    [InlineData("HdrDoctor.App", "linux-x64", "hdr-doctor-linux-x64")]
    [InlineData("HdrDoctor.App", "win-x64", "hdr-doctor-win-x64.exe")]
    [InlineData("HdrDoctor.App", "osx-arm64", "hdr-doctor-osx-arm64")]
    public void Each_build_asks_for_its_own_asset(string assembly, string rid, string expected) =>
        Assert.Equal(expected, UpdateService.AssetNameFor(assembly, rid, appImage: false));

    [Fact]
    public void An_AppImage_replaces_itself_rather_than_the_binary_inside_it() =>
        Assert.Equal(
            "HDR-Doctor-x86_64.AppImage",
            UpdateService.AssetNameFor("HdrDoctor.App", "linux-x64", appImage: true));

    [Fact]
    public void The_cli_updates_through_the_AppImage_that_carries_it() =>
        Assert.Equal(
            "HDR-Doctor-x86_64.AppImage",
            UpdateService.AssetNameFor("HdrDoctor.Cli", "linux-x64", appImage: true));

    [Theory]
    [InlineData("linux-x64")]
    [InlineData("win-x64")]
    public void A_standalone_cli_has_no_asset_rather_than_the_apps(string rid) =>
        Assert.Null(UpdateService.AssetNameFor("HdrDoctor.Cli", rid, appImage: false));

    [Fact]
    public void Checksums_are_read_from_sha256sum_output()
    {
        var sums = string.Join('\n',
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  hdr-doctor-cli-linux-x64",
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08  hdr-doctor-linux-x64",
            "");

        Assert.Equal(
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            UpdateService.HashFor(sums, "hdr-doctor-linux-x64"));
    }

    [Fact]
    public void Binary_mode_checksum_lines_are_read_too() =>
        Assert.Equal(
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            UpdateService.HashFor(
                "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08 *HDR-Doctor-x86_64.AppImage\n",
                "HDR-Doctor-x86_64.AppImage"));

    [Fact]
    public void An_asset_with_no_published_checksum_has_no_hash()
    {
        // ApplyAsync turns this into a refusal to install, which is the point:
        // an unverified binary swap is worse than no update.
        var sums = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08  hdr-doctor-win-x64.exe\n";

        Assert.Null(UpdateService.HashFor(sums, "hdr-doctor-linux-x64"));
    }

    [Fact]
    public void The_running_platform_names_an_asset_the_workflow_publishes()
    {
        string[] published =
        [
            "hdr-doctor-linux-x64", "hdr-doctor-win-x64.exe",
            "hdr-doctor-osx-x64", "hdr-doctor-osx-arm64",
        ];

        Assert.Contains(
            UpdateService.AssetNameFor("HdrDoctor.App", UpdateService.CurrentRid(), appImage: false),
            published);
    }
}
