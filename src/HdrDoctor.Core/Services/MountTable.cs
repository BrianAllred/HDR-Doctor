using System.Text.RegularExpressions;

namespace HdrDoctor.Core.Services;

/// <summary>
/// Which mount a path lives on, from the Linux mount table (/proc/mounts).
/// </summary>
/// <remarks>
/// <see cref="DriveInfo"/> does this lookup itself on Linux, but without decoding the
/// table's octal escapes. A card mounted at "/run/media/user/SWITCH SD" is listed as
/// "SWITCH\040SD", never matches, and DriveInfo reports the mount above it instead —
/// tmpfs, for /run. Mount points with spaces are the norm for removable media, since
/// udisks names them after the volume label.
/// </remarks>
internal static partial class MountTable
{
    internal sealed record Mount(string Device, string MountPoint, string FsType);

    /// <summary>The mount the path sits under, or null when the table can't be read.</summary>
    internal static Mount? Find(string path)
    {
        try
        {
            return Find(path, File.ReadLines("/proc/mounts"));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The longest mount point the path sits under, given the table's lines rather
    /// than reading them, so the matching can be tested.
    /// </summary>
    internal static Mount? Find(string path, IEnumerable<string> lines)
    {
        Mount? best = null;

        foreach (string line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;

            var mountPoint = Unescape(parts[1]);

            if (IsUnder(path, mountPoint) && mountPoint.Length > (best?.MountPoint.Length ?? -1))
            {
                best = new Mount(Unescape(parts[0]), mountPoint, parts[2]);
            }
        }

        return best;
    }

    /// <summary>
    /// Whether a path sits inside a mount point.
    /// </summary>
    /// <remarks>
    /// A plain StartsWith puts /mnt/switchcard under a mount at /mnt/switch, which
    /// then wins the longest-match and answers for a filesystem the path is not on.
    /// The boundary has to be a separator or the whole string.
    /// </remarks>
    internal static bool IsUnder(string path, string mountPoint) =>
        path.Equals(mountPoint, StringComparison.Ordinal) ||
        path.StartsWith(
            mountPoint.EndsWith('/') ? mountPoint : mountPoint + "/",
            StringComparison.Ordinal);

    /// <summary>
    /// The kernel writes space, tab, newline and backslash in a mount field as
    /// three-digit octal escapes (\040, \011, \012, \134).
    /// </summary>
    private static string Unescape(string field) =>
        OctalEscape().Replace(field, m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString());

    [GeneratedRegex(@"\\([0-7]{3})")]
    private static partial Regex OctalEscape();
}
