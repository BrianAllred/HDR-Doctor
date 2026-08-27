using HdrDoctor.Core;
using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;

namespace HdrDoctor.Tests;

/// <summary>
/// The structural half of the mod folder checks — the part cheap enough to run on
/// every scan. Hashing lives in <see cref="FileVerificationTests"/>.
/// </summary>
public class ModFolderTests
{
    [Fact]
    public async Task A_healthy_install_reports_no_problems()
    {
        using var sd = new SdFixture().WithHealthyInstall();

        var findings = await sd.RunAsync(new ModFolderCheck());

        Assert.Empty(findings.Problems());
        Assert.True(findings.Has("HDR v9.9.9-prerelease"));
    }

    [Fact]
    public async Task A_missing_hdr_assets_folder_is_critical()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        Directory.Delete(
            Path.Combine(sd.Root, HdrPaths.HdrAssetsDir.Replace('/', Path.DirectorySeparatorChar)),
            recursive: true);

        var findings = await sd.RunAsync(new ModFolderCheck());

        Assert.Equal(Severity.Critical, findings.Single("hdr-assets is missing").Severity);
    }

    [Fact]
    public async Task Altered_and_extra_files_are_left_to_the_verification_step()
    {
        // The scan must not quietly hash the install: that is the whole point of
        // verification being a step of its own. Handed a manifest it could compare
        // against, this check still says nothing about file contents.
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFile("ultimate/mods/hdr/config.json", "{}");

        var manifest = sd.SnapshotManifest();
        sd.WithFile("ultimate/mods/hdr/config.json", "tampered");
        sd.WithFile("ultimate/mods/hdr/fighter/mario/param/hdr.xml", "custom");

        var findings = await sd.RunAsync(new ModFolderCheck(), manifest: manifest);

        Assert.Empty(findings.Problems());
    }
}
