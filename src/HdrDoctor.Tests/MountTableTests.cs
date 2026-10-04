using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

public class MountTableTests
{
    /// <summary>
    /// Real lines from a card mounted through Hekate's USB mass storage. The kernel
    /// writes the label's space as \040; matching without decoding it lands on /run
    /// and calls a FAT32 card tmpfs, which is what DriveInfo does.
    /// </summary>
    [Fact]
    public void A_mount_point_with_a_space_in_it_is_found()
    {
        var mount = MountTable.Find(
            "/run/media/brian/SWITCH SD/",
            [
                "/dev/sda2 / ext4 rw,relatime 0 0",
                "run /run tmpfs rw,nosuid,nodev,relatime,mode=755 0 0",
                @"/dev/sdd1 /run/media/brian/SWITCH\040SD vfat rw,lazytime,nosuid,nodev,noatime,uid=1000,gid=1000 0 0",
            ]);

        Assert.Equal("vfat", mount?.FsType);
        Assert.Equal("/run/media/brian/SWITCH SD", mount?.MountPoint);
    }
}
