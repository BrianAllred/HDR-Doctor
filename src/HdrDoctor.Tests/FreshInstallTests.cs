using System.IO.Compression;
using HdrDoctor.Core;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

public class FreshInstallTests
{
    [Fact]
    public async Task The_preview_reports_exactly_what_would_be_deleted()
    {
        // The confirmation dialog quotes these numbers, so they had better be right —
        // this is the last thing a user sees before an irreversible delete.
        using var sd = new SdFixture().WithHealthyInstall();
        sd.WithFileOfSize($"{HdrPaths.HdrDir}/a.bin", 1000);
        sd.WithFileOfSize($"{HdrPaths.HdrDir}/nested/b.bin", 2000);

        await using var source = sd.Source();
        var preview = await FreshInstallService
            .PreviewAsync(source, CancellationToken.None);

        var hdr = preview.Single(p => p.Folder == HdrPaths.HdrDir);

        // plugin.nro + ui/hdr_version.txt from the fixture, plus the two above.
        Assert.Equal(4, hdr.FileCount);
        Assert.True(hdr.TotalBytes >= 3000);
    }

    [Fact]
    public async Task Folders_that_are_not_there_are_left_out_of_the_preview()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        Directory.Delete(
            Path.Combine(sd.Root, HdrPaths.HdrStagesDir.Replace('/', Path.DirectorySeparatorChar)),
            recursive: true);

        await using var source = sd.Source();
        var preview = await FreshInstallService
            .PreviewAsync(source, CancellationToken.None);

        Assert.DoesNotContain(preview, p => p.Folder == HdrPaths.HdrStagesDir);
    }

    [Fact]
    public async Task Extracting_a_package_writes_its_files_into_the_install()
    {
        using var sd = new SdFixture();
        using var zip = new TempZip(
            ("ultimate/mods/hdr/plugin.nro", "plugin"),
            ("ultimate/mods/hdr/ui/hdr_version.txt", "v9.9.9-prerelease"));

        await using var source = sd.Source();
        await FreshInstallService.ExtractPackageAsync(zip.Path, source, null, CancellationToken.None);

        Assert.Equal("plugin", await source.ReadAllTextAsync("ultimate/mods/hdr/plugin.nro", CancellationToken.None));
        Assert.Equal(
            "v9.9.9-prerelease",
            await source.ReadAllTextAsync(HdrPaths.HdrVersionFile, CancellationToken.None));
    }

    [Fact]
    public async Task A_zip_entry_that_climbs_out_of_the_install_is_refused()
    {
        // Zip contents are untrusted input. A crafted entry must not be able to write
        // outside the folder the user pointed us at.
        using var sd = new SdFixture();
        using var zip = new TempZip(
            ("../escaped.txt", "should not be written"),
            ("ultimate/mods/hdr/plugin.nro", "plugin"));

        await using var source = sd.Source();
        await FreshInstallService.ExtractPackageAsync(zip.Path, source, null, CancellationToken.None);

        var parent = Directory.GetParent(sd.Root)!.FullName;
        Assert.False(File.Exists(Path.Combine(parent, "escaped.txt")));

        // The safe entry alongside it still lands.
        Assert.True(await source.FileExistsAsync("ultimate/mods/hdr/plugin.nro", CancellationToken.None));
    }

    private sealed class TempZip : IDisposable
    {
        public TempZip(params (string Path, string Contents)[] files)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hdr-pkg-{Guid.NewGuid():n}.zip");

            using var stream = new FileStream(Path, FileMode.Create);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

            foreach (var (path, contents) in files)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(contents);
            }
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
