using System.Collections.Concurrent;

namespace HdrDoctor.Core.Sources;

/// <summary>
/// Resolves install-relative paths against a real directory tree, falling back to a
/// case-insensitive match when an exact one does not exist.
/// </summary>
/// <remarks>
/// Directory listings are cached: a full scan hits the same parent directories
/// thousands of times, and an SD card is slow.
/// </remarks>
public sealed class CaseInsensitivePathResolver(string root)
{
    private readonly string _root = Path.TrimEndingDirectorySeparator(root);

    // Resolved absolute directory -> (lowercased child name -> real child name).
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _listingCache = new(StringComparer.Ordinal);

    public string Root => _root;

    /// <summary>Drops cached directory listings. Call after mutating the tree.</summary>
    public void Invalidate() => _listingCache.Clear();

    /// <summary>
    /// Maps an install-relative path to an absolute one. Always returns a path, even
    /// when nothing exists there — the unresolvable tail is appended so the
    /// caller can report a sensible "expected here" location.
    /// </summary>
    public string Resolve(string relativePath)
    {
        var segments = SplitSegments(relativePath);
        var current = _root;

        for (var i = 0; i < segments.Length; i++)
        {
            var exact = Path.Combine(current, segments[i]);
            if (Path.Exists(exact))
            {
                current = exact;
                continue;
            }

            var match = FindCaseInsensitiveChild(current, segments[i]);
            if (match is null)
            {
                // Nothing here. Append the rest verbatim so the path still names
                // where the file was expected.
                var tail = string.Join(Path.DirectorySeparatorChar, segments[i..]);
                return Path.Combine(current, tail);
            }

            current = Path.Combine(current, match);
        }

        return current;
    }

    /// <summary>Install-relative, forward-slash path for an absolute path under the root.</summary>
    public string ToRelative(string absolutePath)
    {
        var relative = Path.GetRelativePath(_root, absolutePath);
        return relative == "." ? string.Empty : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string[] SplitSegments(string relativePath) =>
        relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private string? FindCaseInsensitiveChild(string parent, string name) =>
        GetListing(parent).TryGetValue(name.ToLowerInvariant(), out var real) ? real : null;

    private Dictionary<string, string> GetListing(string directory) =>
        _listingCache.GetOrAdd(directory, static dir =>
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Directory.Exists(dir))
            {
                return map;
            }

            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                {
                    var name = Path.GetFileName(entry);
                    map.TryAdd(name.ToLowerInvariant(), name);
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
            }

            return map;
        });
}
