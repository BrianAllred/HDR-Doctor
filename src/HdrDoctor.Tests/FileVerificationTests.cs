using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

/// <summary>
/// The step that compares the install against the release's published hashes. It is
/// never part of a scan (see <see cref="ScanResultTests"/> for what a scan says about
/// it instead), so these run the check directly, as ScanService.VerifyFilesAsync does.
/// </summary>
public class FileVerificationTests
{
    [Fact]
    public async Task An_install_matching_its_release_reports_no_problems()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile("ultimate/mods/hdr/config.json", "{}");

        var findings = await sd.RunAsync(new FileVerificationCheck(), manifest: sd.SnapshotManifest());

        Assert.Empty(findings.Problems());
    }

    [Fact]
    public async Task A_file_the_release_expects_but_is_absent_is_critical()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile("ultimate/mods/hdr/config.json", "{}");

        // Snapshot the release first, then lose a file — the order an interrupted
        // download or a half-finished extraction actually produces.
        var manifest = sd.SnapshotManifest();
        File.Delete(Path.Combine(sd.Root, "ultimate", "mods", "hdr", "config.json"));

        var findings = await sd.RunAsync(new FileVerificationCheck(), manifest: manifest);

        var finding = findings.Single("1 file missing");
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("ultimate/mods/hdr/config.json", finding.Paths);
    }

    [Fact]
    public async Task A_file_with_the_wrong_contents_is_an_error()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile("ultimate/mods/hdr/config.json", "{}");

        var manifest = sd.SnapshotManifest();
        sd.WithFile("ultimate/mods/hdr/config.json", "tampered");

        var findings = await sd.RunAsync(new FileVerificationCheck(), manifest: manifest);

        var finding = findings.Single("do not match the official release");
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains("ultimate/mods/hdr/config.json", finding.Paths);
    }

    [Fact]
    public async Task An_extra_file_inside_an_hdr_folder_is_flagged()
    {
        // Anything in here is loaded as though it were part of HDR, so it changes
        // gameplay without ever showing up as a separate mod.
        using var sd = new SdFixture().WithHealthyInstall();

        var manifest = sd.SnapshotManifest();
        sd.WithFile("ultimate/mods/hdr/fighter/mario/param/hdr.xml", "custom");

        var findings = await sd.RunAsync(new FileVerificationCheck(), manifest: manifest);

        var finding = findings.Single("unexpected file");
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("ultimate/mods/hdr/fighter/mario/param/hdr.xml", finding.Paths);
    }

    [Fact]
    public async Task Files_the_launcher_ignores_are_ignored_here_too()
    {
        // Parity with the launcher matters more than being independently right: two
        // tools disagreeing about a clean install is worse than either being slightly
        // wrong. changelog.toml is written after install and is never in the manifest.
        using var sd = new SdFixture().WithHealthyInstall();

        var manifest = sd.SnapshotManifest();
        sd.WithFile("ultimate/mods/hdr/changelog.toml", "written after install");

        var findings = await sd.RunAsync(new FileVerificationCheck(), manifest: manifest);

        Assert.Empty(findings.Problems());
    }

    [Fact]
    public async Task Music_files_are_only_ignored_when_asked_for()
    {
        using var sd = new SdFixture().WithHealthyInstall();

        var manifest = sd.SnapshotManifest();
        sd.WithFile("ultimate/mods/hdr-stages/ui/param/database/ui_bgm_db.prc", "custom soundtrack");

        await using var source = sd.Source();
        var versions = await HdrVersionReader.ReadAsync(source, CancellationToken.None);

        var flagged = await new FileVerificationCheck().RunAsync(
            Context(source, versions, manifest, ignoreMusic: false), CancellationToken.None);
        Assert.True(flagged.Has("unexpected file"));

        var ignored = await new FileVerificationCheck().RunAsync(
            Context(source, versions, manifest, ignoreMusic: true), CancellationToken.None);
        Assert.False(ignored.Has("unexpected file"));
    }

    private static ScanContext Context(
        Core.Sources.IInstallSource source,
        HdrVersionInfo versions,
        ReleaseManifest manifest,
        bool ignoreMusic) => new()
    {
        Source = source,
        Platform = InstallPlatform.Emulator,
        Versions = versions,
        Manifest = manifest,
        IgnoreMusicFiles = ignoreMusic,
    };
}
