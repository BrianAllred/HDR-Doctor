using HdrDoctor.Core.Model;

namespace HdrDoctor.Core.Checks;

/// <summary>
/// Sanity-checks the contents of the selected folder before the detailed checks run.
/// </summary>
/// <remarks>
/// This catches whether the folder is an SD root at all.
/// </remarks>
public sealed class InstallLayoutCheck : ICheck
{
    public string Id => "layout";

    public CheckCategory Category => CheckCategory.InstallLayout;

    public PlatformScope Scope => PlatformScope.Any;

    public string DisplayName => "Install layout";

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct)
    {
        var findings = new List<Finding>();

        var hasUltimate = await ctx.Source.DirectoryExistsAsync("ultimate", ct).ConfigureAwait(false);
        var hasAtmosphere = await ctx.Source.DirectoryExistsAsync("atmosphere", ct).ConfigureAwait(false);

        if (!hasUltimate && !hasAtmosphere)
        {
            findings.Add(new Finding(
                Id,
                Severity.Critical,
                "This does not look like an SD card root",
                $"Neither an 'ultimate' nor an 'atmosphere' folder was found in {ctx.Source.RootDescription}.",
                "Pick the folder that *contains* 'atmosphere' and 'ultimate', not one of them. On an emulator this is "
                + "usually a folder named 'sdmc' or 'sdcard'; on a Switch it is the top level of the card itself.",
                [ctx.Source.RootDescription]));
        }

        return findings;
    }
}
