using HdrDoctor.Core.Model;
using HdrDoctor.Core.Remediations;

namespace HdrDoctor.Core.Checks;

/// <summary>
/// Flags a leftover HID system-module patch on a Switch install.
/// </summary>
/// <remarks>
/// HDR used to ship "HID-HDR", a mitm for the HID system module
/// (title ID <c>0100000000000013</c>), to adjust analog stick gates. It was dropped
/// because it is broken on recent Switch firmware.
///
/// Switch only
/// </remarks>
public sealed class HidModuleCheck : ICheck
{
    public string Id => "hid-module";

    public CheckCategory Category => CheckCategory.HidModule;

    public PlatformScope Scope => PlatformScope.SwitchOnly;

    public string DisplayName => "HID module";

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext ctx, CancellationToken ct)
    {
        if (!await ctx.Source.DirectoryExistsAsync(HdrPaths.HidContents, ct).ConfigureAwait(false))
        {
            return [Finding.Ok(Id, "No HID module patch installed")];
        }

        var contents = await ctx.Source
            .ListAsync(HdrPaths.HidContents, recursive: true, ct)
            .ConfigureAwait(false);

        var files = contents.Where(e => !e.IsDirectory).ToList();

        if (files.Count == 0)
        {
            return
            [
                new Finding(
                    Id,
                    Severity.Info,
                    "Empty HID module folder left behind",
                    $"{HdrPaths.HidContents} exists but contains no files.",
                    "This is what is left after the old HID module was removed. It does nothing and is harmless, "
                    + "though you can delete it if you want",
                    [HdrPaths.HidContents],
                    new DeleteDirectoryRemediation(
                        HdrPaths.HidContents,
                        $"Delete the empty folder {HdrPaths.HidContents}."))
            ];
        }

        return
        [
            new Finding(
                Id,
                Severity.Critical,
                "An old HID module patch is installed",
                $"{HdrPaths.HidContents} contains {files.Count} file{(files.Count == 1 ? "" : "s")}.",
                "This is HID-HDR, a system-module patch HDR used to ship to adjust analog stick gates. It is broken on "
                + "recent Switch firmware and HDR no longer installs it. Leaving it will render CFW unbootable. Delete this folder.",
                [.. files.Select(f => f.RelativePath).Take(20)],
                new DeleteDirectoryRemediation(
                    HdrPaths.HidContents,
                    $"Delete {HdrPaths.HidContents} and everything inside it. "
                    + "This removes the old HID system-module patch."))
        ];
    }
}
