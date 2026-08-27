using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Core.Checks;

/// <summary>
/// Reads whatever Skyline log it can find and explains what is in it.
/// </summary>
public sealed class SkylineLogCheck(IAppPaths paths) : ICheck
{
    public string Id => "skyline-log";

    public CheckCategory Category => CheckCategory.SkylineLog;

    public PlatformScope Scope => PlatformScope.Any;

    public string DisplayName => "Crash/Skyline log";

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct)
    {
        var path = ctx.Source is LocalDirectorySource local
            ? SkylineLogParser.FindLog(local.RootDescription, ctx.Emulator, paths)
            : null;

        if (path is null || !File.Exists(path))
        {
            return
            [
                new Finding(
                    Id,
                    Severity.Info,
                    "No log file found",
                    "Nothing was found in the usual places.",
                    "Skyline only writes a log when logging is switched on, and under an emulator its output goes into "
                    + "the emulator's own log instead. If you have a log from when the problem happened, you can point "
                    + "this tool at it directly and it will be read.",
                    [])
            ];
        }

        ctx.Report("Reading crash log", Path.GetFileName(path));
        return await SkylineLogParser.ParseAsync(path, ct).ConfigureAwait(false);
    }
}
