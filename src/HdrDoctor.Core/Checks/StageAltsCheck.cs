using HdrDoctor.Core.Model;

namespace HdrDoctor.Core.Checks;

/// <summary>
/// Verifies the stage-alts hash table exists.
/// </summary>
/// <remarks>
/// This file missing leads to a Skyline panic on game boot.
/// </remarks>
public sealed class StageAltsCheck : ICheck
{
    /// <summary>
    /// The real file is roughly 38 MB.
    /// </summary>
    private const long MinimumPlausibleSize = 30 * 1024 * 1024;

    public string Id => "stage-alts.hashes";

    public CheckCategory Category => CheckCategory.StageAlts;

    public PlatformScope Scope => PlatformScope.Any;

    public string DisplayName => "Stage alts";

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var entry = await ctx.Source.StatAsync(HdrPaths.StageAltsHashes, ct).ConfigureAwait(false);

        if (entry is null)
        {
            findings.Add(new Finding(
                Id,
                Severity.Critical,
                "Stage alts hash table is missing",
                $"{HdrPaths.StageAltsHashes} was not found.",
                "The stage-alts plugin reads this file when the game starts and will throw a Skyline panic if it's not present."
                + " Either copy the file from a known good HDR package or reinstall the full HDR package.",
                [HdrPaths.StageAltsHashes]));

            return findings;
        }

        if (entry.Size < MinimumPlausibleSize)
        {
            findings.Add(new Finding(
                Id,
                Severity.Critical,
                "Stage alts hash table looks truncated",
                $"{HdrPaths.StageAltsHashes} is only {Formatting.FormatBytes(entry.Size)}; a complete copy is around 38 MB.",
                "This file is almost certainly a failed or interrupted download. The stage-alts plugin will trigger a Skyline panic "
                + "on boot. Either copy the file from a known good HDR package or reinstall the full HDR package.",
                [HdrPaths.StageAltsHashes]));

            return findings;
        }

        findings.Add(new Finding(
            Id,
            Severity.Ok,
            "Stage alts hash table present",
            $"{HdrPaths.StageAltsHashes} ({Formatting.FormatBytes(entry.Size)}).",
            string.Empty,
            [HdrPaths.StageAltsHashes]));

        return findings;
    }
}
