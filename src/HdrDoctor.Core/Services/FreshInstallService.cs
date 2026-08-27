using System.IO.Compression;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Core.Services;

/// <param name="Folder">Install-relative folder that will be deleted.</param>
/// <param name="FileCount">How many files it holds.</param>
/// <param name="TotalBytes">How much data it holds.</param>
public sealed record DeletionPreview(string Folder, int FileCount, long TotalBytes);

/// <summary>
/// Wipes HDR's own mod folders and reinstalls the current release over the top.
/// </summary>
/// <remarks>
/// The escape hatch for an install that is too tangled to repair file by file.
///
/// Only the three folders HDR owns are removed — <c>hdr</c>, <c>hdr-assets</c> and
/// <c>hdr-stages</c>. Skyline, the plugins folder and the user's other mods are left
/// alone.
/// </remarks>
public sealed class FreshInstallService(HttpClient http)
{
    /// <summary>What a fresh install would delete. Call this before asking the user.</summary>
    public static async Task<IReadOnlyList<DeletionPreview>> PreviewAsync(
        IInstallSource source,
        CancellationToken ct)
    {
        var previews = new List<DeletionPreview>();

        foreach (var folder in HdrPaths.HdrOwnedFolders)
        {
            if (!await source.DirectoryExistsAsync(folder, ct).ConfigureAwait(false))
            {
                continue;
            }

            var files = (await source.ListAsync(folder, recursive: true, ct).ConfigureAwait(false))
                .Where(e => !e.IsDirectory)
                .ToList();

            previews.Add(new DeletionPreview(folder, files.Count, files.Sum(f => f.Size)));
        }

        return previews;
    }

    /// <summary>
    /// Deletes HDR's mod folders and extracts the latest release package over the
    /// install root.
    /// </summary>
    /// <param name="channel">
    /// Which release channel to install.
    /// </param>
    public async Task RunAsync(
        IMutableInstallSource source,
        ReleaseChannel channel,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        // Download first.
        progress?.Report(new ScanProgress("Downloading HDR", "switch-package.zip", null));

        var archive = Path.Combine(Path.GetTempPath(), $"hdr-package-{Guid.NewGuid():n}.zip");

        try
        {
            await DownloadAsync(ReleaseManifestClient.LatestPackageUrl(channel), archive, progress, ct)
                .ConfigureAwait(false);

            foreach (var folder in HdrPaths.HdrOwnedFolders)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new ScanProgress("Removing old files", folder, null));
                await source.DeleteDirectoryAsync(folder, ct).ConfigureAwait(false);
            }

            await ExtractPackageAsync(archive, source, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(archive))
            {
                File.Delete(archive);
            }
        }
    }

    private async Task DownloadAsync(
        string url,
        string destination,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        using var response = await http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;

        await using var download = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);

        var buffer = new byte[128 * 1024];
        long written = 0;
        int read;

        while ((read = await download.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            written += read;

            progress?.Report(new ScanProgress(
                "Downloading HDR",
                $"{written / 1024 / 1024} MB",
                total is > 0 ? (double)written / total.Value : null));
        }
    }

    /// <summary>
    /// Extracts an HDR package zip over an install.
    /// </summary>
    public static async Task ExtractPackageAsync(
        string archivePath,
        IMutableInstallSource source,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var index = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            index++;

            var relative = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (relative.Split('/').Any(segment => segment == ".."))
            {
                continue;
            }

            progress?.Report(new ScanProgress("Extracting", relative, (double)index / entries.Count));

            await using var content = entry.Open();
            await source.WriteAsync(relative, content, ct).ConfigureAwait(false);
        }
    }
}
