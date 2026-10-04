using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HdrDoctor.Core.Services;

public static class FtpMountDetector
{
    public static bool IsFtpMount(string userPath)
    {
        if (string.IsNullOrWhiteSpace(userPath)) return false;

        if (userPath.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
            userPath.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string fullPath = Path.GetFullPath(userPath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return IsFtpMountLinux(fullPath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return IsFtpMountMac(fullPath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return IsFtpMountWindows(fullPath);

        return false;
    }

    private static bool IsFtpMountLinux(string targetPath)
    {
        try
        {
            // Reading /proc/mounts won't block on dead network drives like DriveInfo does
            return IsFtpMountLinux(targetPath, File.ReadLines("/proc/mounts"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The /proc/mounts half of the Linux check, given the lines rather than reading
    /// them, so the mount matching can be tested.
    /// </summary>
    internal static bool IsFtpMountLinux(string targetPath, IEnumerable<string> mountLines)
    {
        if (MountTable.Find(targetPath, mountLines) is not { } mount) return false;

        // Check FUSE identifiers (e.g., "fuse.curlftpfs", "curlftpfs", or "gvfsd-ftp")
        return mount.FsType.Contains("ftp", StringComparison.OrdinalIgnoreCase) ||
               mount.Device.Contains("ftp", StringComparison.OrdinalIgnoreCase) ||
               IsSchemeInPathFtp(targetPath, mount.MountPoint, mount.Device);
    }

    /// <summary>
    /// The desktop FUSE helpers name none of their mounts after the protocol: one
    /// mount point serves every scheme they speak, and the scheme lives in the first
    /// directory underneath it. So the device and type say nothing and the path is the
    /// only evidence.
    ///
    ///   gvfs      gvfsd-fuse /run/user/1000/gvfs           ftp:host=192.168.0.228,port=5000
    ///   kio-fuse  kio-fuse   /run/user/1000/kio-fuse-AHmERP  ftp/192.168.0.228:5000
    /// </summary>
    /// <remarks>
    /// Matched by device rather than by "any fuse mount" on purpose: an ordinary folder
    /// named "ftp" sits under some mount point on every machine, and / is a mount point.
    ///
    /// Deliberately not matching "sftp": that is a different protocol this app cannot
    /// speak, and the caller uses the answer to decide whether an install is FTP.
    /// </remarks>
    private static bool IsSchemeInPathFtp(string targetPath, string mountPoint, string device)
    {
        if (!device.StartsWith("gvfsd", StringComparison.OrdinalIgnoreCase) &&
            !device.StartsWith("kio-fuse", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // "ftp" as kio-fuse spells it, "ftp:host=..." as gvfs does.
        var scheme = FirstSegmentUnder(targetPath, mountPoint).Split(':')[0];

        return scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase) ||
               scheme.Equals("ftps", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The first path component below a mount point, or empty.</summary>
    private static string FirstSegmentUnder(string targetPath, string mountPoint)
    {
        var rest = targetPath[mountPoint.Length..].TrimStart('/');
        var slash = rest.IndexOf('/');
        return slash < 0 ? rest : rest[..slash];
    }

    private static bool IsFtpMountMac(string targetPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "mount",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null) return false;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            int longestMatch = -1;
            bool isFtp = false;

            // Output looks like: "curlftpfs#ftp://host on /Volumes/ftp (osxfusefs, ...)"
            using var reader = new StringReader(output);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                int onIndex = line.IndexOf(" on ");
                if (onIndex == -1) continue;

                string device = line.Substring(0, onIndex).Trim();
                int metaIndex = line.IndexOf(" (", onIndex);
                if (metaIndex == -1) continue;

                string mountPoint = line.Substring(onIndex + 4, metaIndex - (onIndex + 4)).Trim();
                string metaData = line.Substring(metaIndex); // (ftpfs, nodev, ...)

                if (MountTable.IsUnder(targetPath, mountPoint) && mountPoint.Length > longestMatch)
                {
                    longestMatch = mountPoint.Length;
                    isFtp = metaData.Contains("ftp", StringComparison.OrdinalIgnoreCase) ||
                            device.Contains("ftp", StringComparison.OrdinalIgnoreCase);
                }
            }
            return isFtp;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFtpMountWindows(string targetPath)
    {
        try
        {
            string? root = Path.GetPathRoot(targetPath);
            if (string.IsNullOrEmpty(root)) return false;

            // Strip trailing slash for WNetGetConnection (e.g. "Z:\ -> Z:")
            string driveLetter = root.TrimEnd('\\', '/');

            // 1. Check if the native Windows network redirector exposes an FTP URI
            int capacity = 260;
            StringBuilder remoteName = new(capacity);
            int result = WNetGetConnection(driveLetter, remoteName, ref capacity);

            if (result == 0) // NO_ERROR
            {
                string networkPath = remoteName.ToString();
                if (networkPath.StartsWith("ftp:", StringComparison.OrdinalIgnoreCase) ||
                    networkPath.StartsWith("ftps:", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // 2. Fallback: Third-party drivers (like WinFsp, CloudMounter, RaiDrive)
            var drive = new DriveInfo(root);
            if (drive.IsReady)
            {
                // Third-party drivers often inject "FTP" into the DriveFormat
                // Example: "WinFsp.FTP"
                if (drive.DriveFormat.Contains("FTP", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetGetConnection(
        string localName,
        StringBuilder remoteName,
        ref int length);
}