using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using HdrDoctor.Core.Model;

namespace HdrDoctor.Core.Services;

/// <param name="Version">The version the newest release publishes.</param>
/// <param name="Tag">Its git tag, as GitHub spells it.</param>
/// <param name="AssetName">The asset this build should download — see <see cref="UpdateService.AssetNameFor"/>.</param>
/// <param name="DownloadUrl">Where that asset lives.</param>
/// <param name="ChecksumUrl">The release's SHA256SUMS asset. Without it the update is refused.</param>
/// <param name="Notes">The release body, for showing the user what changed.</param>
public sealed record AppUpdate(
    Version Version,
    string Tag,
    string AssetName,
    string DownloadUrl,
    string ChecksumUrl,
    string? Notes);

/// <summary>
/// Checks GitHub for a newer HDR Doctor and replaces this executable with it.
/// </summary>
public sealed class UpdateService(HttpClient http)
{
    public const string Repository = "BrianAllred/HDR-Doctor";

    private const string ChecksumAsset = "SHA256SUMS";

    /// <summary>The version stamped into this build by <c>Directory.Build.props</c>.</summary>
    public static Version CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// The file that would be replaced: the AppImage when running from one, and the
    /// single-file host otherwise.
    /// </summary>
    /// <remarks>
    /// Inside an AppImage <see cref="Environment.ProcessPath"/> points at the extracted
    /// mount under <c>/tmp</c>, which disappears on exit. <c>$APPIMAGE</c> is the runtime's
    /// pointer back to the real file on disk.
    /// </remarks>
    public static string? ExecutablePath =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage
            ? appImage
            : Environment.ProcessPath;

    /// <summary>
    /// Asks GitHub for the newest release and returns it when it is newer than this build.
    /// </summary>
    /// <returns><see langword="null"/> when this build is current, or when the release
    /// carries nothing this platform can run.</returns>
    public async Task<AppUpdate?> CheckAsync(CancellationToken ct)
    {
        RemoveStaleBackup();

        using var response = await http
            .GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct)
            .ConfigureAwait(false);

        // A repository with no published release answers 404, not a failure worth reporting.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Could not ask GitHub for the newest release (HTTP {(int)response.StatusCode}).");
        }

        await using var json = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(json, cancellationToken: ct).ConfigureAwait(false);

        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() : null;

        if (tag is null || ParseTag(tag) is not { } version || version <= CurrentVersion)
        {
            return null;
        }

        var wanted = AssetNameFor(
            Assembly.GetEntryAssembly()?.GetName().Name,
            CurrentRid(),
            Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 });

        string? assetUrl = null;
        string? checksumUrl = null;

        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;

                if (name == wanted)
                {
                    assetUrl = url;
                }
                else if (name == ChecksumAsset)
                {
                    checksumUrl = url;
                }
            }
        }

        return assetUrl is not null && checksumUrl is not null
            ? new AppUpdate(
                version,
                tag,
                wanted,
                assetUrl,
                checksumUrl,
                root.TryGetProperty("body", out var body) ? body.GetString() : null)
            : null;
    }

    /// <summary>
    /// Downloads the update, checks it against the release's published SHA-256, and
    /// puts it in this executable's place.
    /// </summary>
    /// <returns>The path to restart, now holding the new version.</returns>
    public async Task<string> ApplyAsync(
        AppUpdate update,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var target = ExecutablePath
                     ?? throw new InvalidOperationException(
                         "This build has no executable on disk to replace, so it cannot update itself.");

        var expected = await GetExpectedHashAsync(update, ct).ConfigureAwait(false);

        // Staged beside the target so the swap is a rename within one filesystem
        var staged = target + ".new";
        var backup = target + ".old";

        try
        {
            var actual = await DownloadAsync(update, staged, progress, ct).ConfigureAwait(false);

            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The download did not match the checksum {update.Tag} publishes for {update.AssetName}, " +
                    "so it was discarded rather than installed.");
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    staged,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            progress?.Report(new ScanProgress("Installing the update", Path.GetFileName(target), null));

            File.Move(target, backup, overwrite: true);

            try
            {
                File.Move(staged, target, overwrite: true);
            }
            catch
            {
                File.Move(backup, target, overwrite: true);
                throw;
            }

            TryDelete(backup);
            return target;
        }
        catch (UnauthorizedAccessException e)
        {
            throw new UnauthorizedAccessException(
                $"HDR Doctor cannot write to {target}, so it cannot replace itself. " +
                $"Download {update.Tag} from https://github.com/{Repository}/releases manually, " +
                "or move HDR Doctor somewhere you own.",
                e);
        }
        finally
        {
            TryDelete(staged);
        }
    }

    private async Task<string> GetExpectedHashAsync(AppUpdate update, CancellationToken ct)
    {
        var sums = await http.GetStringAsync(update.ChecksumUrl, ct).ConfigureAwait(false);

        return HashFor(sums, update.AssetName)
               ?? throw new InvalidOperationException(
                   $"Release {update.Tag} does not publish a checksum for {update.AssetName}, " +
                   "so the download could not be verified and was not installed.");
    }

    private async Task<string> DownloadAsync(
        AppUpdate update,
        string destination,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        using var response = await http
            .GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;

        await using var download = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);

        using var sha = SHA256.Create();

        var buffer = new byte[128 * 1024];
        long written = 0;
        int read;

        while ((read = await download.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            sha.TransformBlock(buffer, 0, read, null, 0);
            written += read;

            progress?.Report(new ScanProgress(
                $"Downloading HDR Doctor {update.Tag}",
                $"{written / 1024 / 1024} MB",
                total is > 0 ? (double)written / total.Value : null));
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexStringLower(sha.Hash!);
    }

    /// <summary>Removes the executable a previous update moved aside.</summary>
    /// <remarks>
    /// Deferred to the next run because Windows keeps the renamed image locked for as
    /// long as the process that was started from it is alive.
    /// </remarks>
    private static void RemoveStaleBackup()
    {
        if (ExecutablePath is { } path)
        {
            TryDelete(path + ".old");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Reads a version out of a release tag, ignoring any prerelease suffix.</summary>
    /// <remarks><c>v1.2.3</c>, <c>1.2.3</c> and <c>v1.2.3-beta.1</c> all read as 1.2.3.</remarks>
    public static Version? ParseTag(string tag)
    {
        var text = tag.Trim();
        text = text.StartsWith('v') || text.StartsWith('V') ? text[1..] : text;

        var end = text.AsSpan().IndexOfAny('-', '+');
        if (end >= 0)
        {
            text = text[..end];
        }

        return Version.TryParse(text, out var version) ? version : null;
    }

    /// <summary>
    /// The release asset that replaces this build.
    /// </summary>
    /// <remarks>
    /// The names here are the contract with <c>.github/workflows/release.yml</c>: change
    /// one and the other stops matching, which shows up as "no update available" rather
    /// than as a failure. UpdateServiceTests pins them.
    /// </remarks>
    /// <param name="entryAssemblyName">
    /// <c>HdrDoctor.Cli</c> selects the CLI asset; anything else selects the app.
    /// </param>
    /// <param name="appImage">
    /// True when running from an AppImage, which bundles both and so has one asset of its own.
    /// </param>
    public static string AssetNameFor(string? entryAssemblyName, string rid, bool appImage)
    {
        if (appImage)
        {
            return "HDR-Doctor-x86_64.AppImage";
        }

        var kind = entryAssemblyName == "HdrDoctor.Cli" ? "hdr-doctor-cli" : "hdr-doctor";
        var extension = rid.StartsWith("win", StringComparison.Ordinal) ? ".exe" : "";

        return $"{kind}-{rid}{extension}";
    }

    /// <summary>The runtime identifier this process was published for.</summary>
    /// <remarks>
    /// Read from the process rather than the OS so an x64 build running under Rosetta
    /// updates itself with another x64 build instead of jumping architecture.
    /// </remarks>
    public static string CurrentRid()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            var other => other.ToString().ToLowerInvariant(),
        };

        return $"{os}-{arch}";
    }

    /// <summary>Finds one file's hash in <c>sha256sum</c> output.</summary>
    public static string? HashFor(string sums, string assetName)
    {
        foreach (var line in sums.Split('\n'))
        {
            // "<hash>  <name>", where the name may carry sha256sum's binary-mode '*'.
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);

            if (parts.Length == 2 && parts[1].TrimStart('*') == assetName)
            {
                return parts[0];
            }
        }

        return null;
    }
}
