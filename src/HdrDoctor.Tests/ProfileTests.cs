using HdrDoctor.Core.Model;
using HdrDoctor.Core.Profiles;

namespace HdrDoctor.Tests;

/// <summary>
/// Which profiles may be written to. A profile that offers a fix it cannot safely
/// apply is worse than one that offers none.
/// </summary>
public class ProfileTests
{
    [Fact]
    public void A_local_folder_supports_fixes()
    {
        var profile = InstallProfile.ForLocal("Card reader", "/media/sd", InstallPlatform.Switch);

        Assert.True(profile.SupportsFixes);
    }

    [Fact]
    public void An_ftp_connection_does_not_support_fixes()
    {
        var profile = InstallProfile.ForFtp("Switch", "192.168.1.42", 5000, null, null);

        Assert.False(profile.SupportsFixes);
        Assert.True(profile.IsAvailable);
    }

    /// <summary>
    /// Nothing serves an emulator's sdmc folder over FTP, so the record's Emulator
    /// default is the wrong guess here — and the platform decides which checks run.
    /// </summary>
    [Fact]
    public void An_ftp_connection_is_a_switch()
    {
        Assert.Equal(
            InstallPlatform.Switch,
            InstallProfile.ForFtp("Switch", "192.168.1.42", 5000, null, null).Platform);
    }

    /// <summary>
    /// curlftpfs and mapped FTP drives look local right up until a write fails
    /// halfway through, which is the one moment it matters.
    /// </summary>
    [Fact]
    public void A_local_folder_that_is_really_an_ftp_mount_does_not_support_fixes()
    {
        var profile = InstallProfile.ForLocal("Mounted Switch", "/mnt/switch", InstallPlatform.Switch)
            with
            { IsFtpMount = true };

        Assert.False(profile.SupportsFixes);
    }
}
