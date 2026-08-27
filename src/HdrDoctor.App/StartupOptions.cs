using System;
using System.IO;
using System.Linq;

namespace HdrDoctor.App;

/// <param name="Folder">
/// An install folder given on the command line, if any.
/// </param>
/// <param name="ScanOnStart">
/// Whether to start scanning as soon as the window opens.
/// </param>
/// <param name="VerifyOnStart">
/// Whether to follow that scan with the file verification step.
/// </param>
public sealed record StartupOptions(string? Folder, bool ScanOnStart, bool VerifyOnStart)
{
    public static StartupOptions None { get; } = new(null, false, false);

    /// <summary>
    /// Parses the command line.
    /// </summary>
    /// <remarks>    ///
    ///   hdr-doctor [folder] [--scan] [--verify]
    /// </remarks>
    public static StartupOptions Parse(string[] args)
    {
        var scan = args.Any(a => a is "--scan" or "-s");
        var verify = args.Any(a => a is "--verify" or "-v");

        var folder = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (folder is not null && !Directory.Exists(folder))
        {
            folder = null;
        }

        return new StartupOptions(folder, scan, verify);
    }
}
