using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Core.Checks;

/// <summary>
/// Reads the newest Atmosphere crash report Smash left on a Switch's SD card.
/// </summary>
/// <remarks>
/// On hardware, Atmosphere writes a report to <c>sd:/atmosphere/crash_reports</c>
/// every time a title crashes.
///
/// Switch only. Emulators do not write these.
/// </remarks>
public sealed class CrashReportCheck : ICheck
{
    public string Id => CrashReportParser.CheckId;

    public CheckCategory Category => CheckCategory.CrashReport;

    public PlatformScope Scope => PlatformScope.SwitchOnly;

    public string DisplayName => "Crash reports";

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct)
    {
        var entries = await ctx.Source
            .ListAsync(HdrPaths.CrashReportsDir, recursive: false, ct)
            .ConfigureAwait(false);

        var reports = CrashReportParser.Describe(entries);
        var smash = reports.Where(r => r.IsFor(HdrPaths.SmashTitleId)).ToList();

        if (smash.Count == 0)
        {
            return [NothingFromSmash(reports.Count)];
        }

        var newest = smash[0];

        if (!CrashReportParser.IsReadableSize(newest))
        {
            return
            [
                new Finding(
                    Id,
                    Severity.Info,
                    "A crash report was too large to read",
                    $"{newest.RelativePath} is {newest.Size / 1024 / 1024} MB.",
                    "Atmosphere's crash reports are normally very small, so this one is either not a "
                    + "crash report or was written by something else. It was left alone rather than read into memory.",
                    [newest.RelativePath])
            ];
        }

        ctx.Report("Reading crash report", newest.FileName);
        var text = await ctx.Source.ReadAllTextAsync(newest.RelativePath, ct).ConfigureAwait(false);

        var isNewestOnCard = reports.Count > 0 && reports[0] == newest;

        return
        [
            CrashReportParser.Explain(
                newest,
                text,
                Severity.Error,
                isNewestOnCard ? "Smash crashed" : "Smash crashed earlier on",
                (isNewestOnCard
                    ? "This is the newest crash report from Super Smash Bros. Ultimate."
                    : "This is the newest crash report from Super Smash Bros. Ultimate. "
                    + "Something else on the console has crashed since.")
                + " " + Census(reports.Count, smash.Count))
        ];
    }

    private Finding NothingFromSmash(int total) =>
        Finding.Ok(
            Id,
            "No crash reports from Smash",
            total == 0
                ? $"Nothing in {HdrPaths.CrashReportsDir}."
                : $"{total} crash report{(total == 1 ? "" : "s")} in {HdrPaths.CrashReportsDir}, "
                + $"none from Smash. {(total == 1 ? "It was" : "They were")} not read.");

    private static string Census(int total, int fromSmash) => total == fromSmash
        ? total == 1
            ? "It is the only crash report on the card."
            : $"All {total} crash reports on the card are Smash's."
        : $"There are {total} crash reports on the card, {fromSmash} of them from Smash.";
}
