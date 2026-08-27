namespace HdrDoctor.Core.Sources;

/// <summary>One file or directory inside an install source.</summary>
/// <param name="RelativePath">
/// Forward-slash path relative to the install root, with no leading slash —
/// e.g. <c>ultimate/mods/hdr/plugin.nro</c>. This is the canonical form
/// everything in the app compares against, including release manifests.
/// </param>
/// <param name="IsDirectory">True for directories.</param>
/// <param name="Size">Size in bytes; 0 for directories.</param>
public sealed record SourceEntry(string RelativePath, bool IsDirectory, long Size)
{
    public string Name => RelativePath.Length == 0
        ? string.Empty
        : RelativePath[(RelativePath.LastIndexOf('/') + 1)..];
}
