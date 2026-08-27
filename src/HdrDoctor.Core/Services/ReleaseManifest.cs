using System.Text.Json;
using System.Text.Json.Serialization;

namespace HdrDoctor.Core.Services;

/// <summary>
/// The per-file MD5 manifest published alongside each HDR release
/// (<c>content_hashes.json</c>, produced by <c>scripts/hash_package.py</c>).
/// </summary>
/// <remarks>
/// Manifest paths are SD-relative with a leading slash and, on Windows-built
/// releases, sometimes backslashes — e.g. <c>/ultimate/mods/hdr/config.json</c>.
/// They are normalized on load to the same form the rest of the app uses:
/// forward slashes, no leading slash.
/// </remarks>
public sealed class ReleaseManifest
{
    private readonly Dictionary<string, string> _hashesByPath;

    private ReleaseManifest(string tag, Dictionary<string, string> hashesByPath)
    {
        Tag = tag;
        _hashesByPath = hashesByPath;
    }

    public string Tag { get; }

    public int FileCount => _hashesByPath.Count;

    /// <summary>Every path the release is expected to contain, normalized.</summary>
    public IReadOnlyCollection<string> Paths => _hashesByPath.Keys;

    public bool TryGetHash(string relativePath, out string hash) =>
        _hashesByPath.TryGetValue(Normalize(relativePath), out hash!);

    public bool Contains(string relativePath) => _hashesByPath.ContainsKey(Normalize(relativePath));

    /// <summary>
    /// True when the release lists at least one file inside <paramref name="folder"/>.
    /// </summary>
    /// <remarks>
    /// This is what separates "the release does not ship that file" from "this
    /// manifest does not describe that part of the install at all". A caller that
    /// wants to treat the manifest as the authority on a folder's contents has to ask
    /// this first, or an older manifest that only covered ultimate/mods would read as
    /// a release that ships no plugins.
    /// </remarks>
    public bool DescribesFolder(string folder)
    {
        var prefix = Normalize(folder).TrimEnd('/') + "/";
        return _hashesByPath.Keys.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static ReleaseManifest Parse(string json, string tag)
    {
        var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(json)
                      ?? throw new InvalidDataException("content_hashes.json was empty or not an array.");

        if (entries.Count == 0)
        {
            throw new InvalidDataException("content_hashes.json contained no entries.");
        }

        var map = new Dictionary<string, string>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path) || string.IsNullOrWhiteSpace(entry.Hash))
            {
                continue;
            }

            map[Normalize(entry.Path)] = entry.Hash.Trim().ToLowerInvariant();
        }

        return new ReleaseManifest(tag, map);
    }

    /// <summary>Forward slashes, no leading slash — the app's canonical relative-path form.</summary>
    public static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private sealed record ManifestEntry(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("hash")] string Hash);
}
