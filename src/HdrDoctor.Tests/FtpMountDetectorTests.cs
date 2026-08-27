using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

/// <summary>
/// Reading /proc/mounts to decide whether a folder is an FTP mount wearing a local
/// path. Getting this wrong in either direction is expensive: a false negative offers
/// fixes that write over a live FTP link, a false positive takes fixes away from a
/// card reader.
/// </summary>
public class FtpMountDetectorTests
{
    private const string Root = "/dev/sda2 / ext4 rw,relatime 0 0";

    private static bool IsFtp(string path, params string[] mounts) =>
        FtpMountDetector.IsFtpMountLinux(path, [Root, .. mounts]);

    [Fact]
    public void A_curlftpfs_mount_is_ftp()
    {
        Assert.True(IsFtp(
            "/mnt/switch/ultimate/mods",
            "curlftpfs#ftp://192.168.1.42:5000 /mnt/switch fuse rw,nosuid 0 0"));
    }

    [Fact]
    public void An_ordinary_folder_is_not()
    {
        Assert.False(IsFtp("/home/someone/emulator/sdmc"));
    }

    /// <summary>
    /// The mount point has to end at a separator. A plain StartsWith puts this path
    /// on the FTP filesystem next door and disables fixes for a folder that is really
    /// on the local disk.
    /// </summary>
    [Fact]
    public void A_sibling_folder_sharing_a_prefix_with_a_mount_point_is_not()
    {
        Assert.False(IsFtp(
            "/mnt/switchcard/ultimate",
            "curlftpfs#ftp://192.168.1.42:5000 /mnt/switch fuse rw,nosuid 0 0"));
    }

    /// <summary>
    /// gvfs names every mount "gvfsd-fuse" on one mount point, so the device and the
    /// filesystem type say nothing at all — the protocol is only in the path.
    /// </summary>
    [Fact]
    public void A_gvfs_ftp_mount_is_ftp()
    {
        Assert.True(IsFtp(
            "/run/user/1000/gvfs/ftp:host=192.168.1.42,port=5000/ultimate",
            "gvfsd-fuse /run/user/1000/gvfs fuse.gvfsd-fuse rw,nosuid,nodev 0 0"));
    }

    /// <summary>
    /// kio-fuse (KDE's Dolphin) does the same thing as gvfs with a different spelling:
    /// the mount is "kio-fuse", the scheme is a plain directory under it. Real line
    /// from a machine where this was reported missing.
    /// </summary>
    [Fact]
    public void A_kio_fuse_ftp_mount_is_ftp()
    {
        Assert.True(IsFtp(
            "/run/user/1000/kio-fuse-AHmERP/ftp/192.168.0.228:5000/ultimate",
            "kio-fuse /run/user/1000/kio-fuse-AHmERP fuse.kio-fuse rw,nosuid,nodev,relatime 0 0"));
    }

    [Fact]
    public void A_kio_fuse_mount_of_something_else_is_not()
    {
        Assert.False(IsFtp(
            "/run/user/1000/kio-fuse-AHmERP/sftp/192.168.0.228/ultimate",
            "kio-fuse /run/user/1000/kio-fuse-AHmERP fuse.kio-fuse rw,nosuid,nodev,relatime 0 0"));
    }

    /// <summary>
    /// The scheme-in-path rule only applies to the helpers that use it. Any machine has
    /// a folder called "ftp" somewhere, and / is a mount point.
    /// </summary>
    [Fact]
    public void An_ordinary_folder_named_ftp_is_not()
    {
        Assert.False(IsFtp("/home/someone/ftp/ultimate"));
    }

    [Fact]
    public void A_gvfs_mount_of_something_else_is_not()
    {
        Assert.False(IsFtp(
            "/run/user/1000/gvfs/sftp:host=192.168.1.42/ultimate",
            "gvfsd-fuse /run/user/1000/gvfs fuse.gvfsd-fuse rw,nosuid,nodev 0 0"));
    }

    /// <summary>The nearest mount point decides, not the first one seen.</summary>
    [Fact]
    public void A_local_folder_nested_inside_an_ftp_mount_point_follows_the_nearer_mount()
    {
        Assert.False(IsFtp(
            "/mnt/switch/local/ultimate",
            "curlftpfs#ftp://192.168.1.42:5000 /mnt/switch fuse rw,nosuid 0 0",
            "/dev/sdb1 /mnt/switch/local ext4 rw,relatime 0 0"));
    }
}
