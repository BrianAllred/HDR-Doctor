using HdrDoctor.Core.Model;

namespace HdrDoctor.Core.Services;

/// <summary>Fetches release artifacts from the HDR GitHub releases.</summary>
public sealed class ReleaseManifestClient(HttpClient http)
{
    private const string Owner = "HDR-Development";

    /// <summary>
    /// Downloads the file manifest for a specific release, mirroring the URL the
    /// launcher uses so the two always agree about what a release contains.
    /// </summary>
    public async Task<ReleaseManifest> GetManifestAsync(
        ReleaseChannel channel,
        string tag,
        CancellationToken ct)
    {
        var repository = HdrVersionInfo.RepositoryFor(channel)
                         ?? throw new InvalidOperationException(
                             "This install's version does not name a release channel, so there is no manifest to compare against.");

        var url = $"https://github.com/{Owner}/{repository}/releases/download/{tag}/content_hashes.json";

        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Could not download the file list for {tag} from {repository} (HTTP {(int)response.StatusCode}).");
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ReleaseManifest.Parse(json, tag);
    }

    /// <summary>The switch-package.zip download URL for the latest release on a channel.</summary>
    public static string LatestPackageUrl(ReleaseChannel channel)
    {
        var repository = HdrVersionInfo.RepositoryFor(channel) ?? "HDR-Releases";
        return $"https://github.com/{Owner}/{repository}/releases/latest/download/switch-package.zip";
    }
}
