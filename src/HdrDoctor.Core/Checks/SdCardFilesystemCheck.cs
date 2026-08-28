using HdrDoctor.Core.Model;

namespace HdrDoctor.Core.Checks;

/// <summary>
/// Checks that a console's SD card is formatted as FAT32 rather than exFAT.
/// </summary>
/// <remarks>
/// Nintendo's exFAT driver is notoriously buggy,
/// so HDR requires a FAT32 card. 
/// 
/// This check is only run when the scan is done over a local folder,
/// because FTP does not expose the card's filesystem type.
/// </remarks>
public sealed class SdCardFilesystemCheck(string? localRoot, Func<string, string?>? probe = null) : ICheck
{
    /// <summary>
    /// What each host calls FAT: Windows reports "FAT32", Linux "vfat" and macOS
    /// "msdos". 
    /// </summary>
    private static readonly HashSet<string> FatFormats =
        new(StringComparer.OrdinalIgnoreCase) { "vfat", "msdos", "fat", "fat32" };

    private readonly Func<string, string?> _probe = probe ?? DescribeFilesystem;

    public string Id => "sd-card.filesystem";

    public CheckCategory Category => CheckCategory.SdCard;

    public PlatformScope Scope => PlatformScope.SwitchOnly;

    public string DisplayName => "SD card formatting";

    public Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Finding>>([Inspect()]);

    private Finding Inspect()
    {
        if (localRoot is null)
        {
            return new Finding(
                Id,
                Severity.Info,
                "SD card formatting was not checked",
                "This console was scanned over FTP.",
                "Can't check filesystem type over FTP. To run this check, connect the console to a computer and scan the SD card as a local folder instead. "
                + "The filesystem should be FAT32.",
                []);
        }

        var format = _probe(localRoot);

        if (string.IsNullOrWhiteSpace(format))
        {
            return new Finding(
                Id,
                Severity.Info,
                "SD card formatting was not checked",
                $"The system could not say what {localRoot} is formatted as.",
                "This is usually a drive that was unmounted or went offline mid-scan. Nothing is known to be "
                + "wrong; the card simply was not checked. Make sure it's FAT32.",
                []);
        }

        if (format.Equals("exfat", StringComparison.OrdinalIgnoreCase))
        {
            return new Finding(
                Id,
                Severity.Critical,
                "SD card is formatted as exFAT, not FAT32",
                $"{localRoot} is on an exFAT volume.",
                "Mods require the SD card to be formatted as FAT32. Copy everything on it to your "
                + "computer first, format the card as FAT32 using Hekate or your PC, then copy all the files back. "
                + "Then run Archive Bit Fix in Hekate.",
                []);
        }

        if (FatFormats.Contains(format))
        {
            return Finding.Ok(Id, "SD card is formatted as FAT32", $"{localRoot} is on a {format} volume.");
        }

        return new Finding(
            Id,
            Severity.Info,
            $"This is not a Switch SD card, so its formatting was not checked ({format})",
            $"{localRoot} is on a {format} volume.",
            "A Switch reads FAT32 and exFAT cards and nothing else, so this folder is probably a copy on your computer "
            + "rather than the card itself. When copying to a Switch, make sure the card is formatted as FAT32.",
            []);
    }

    /// <summary>
    /// What the folder's volume is formatted as, or null when the host cannot say.
    /// </summary>
    /// <remarks>
    /// <see cref="DriveInfo"/> resolves a path to its containing mount on Unix and to
    /// its drive root on Windows, so no mount table has to be read here.
    /// </remarks>
    private static string? DescribeFilesystem(string path)
    {
        try
        {
            return new DriveInfo(path).DriveFormat;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
