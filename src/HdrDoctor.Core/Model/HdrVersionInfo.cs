namespace HdrDoctor.Core.Model;

/// <summary>Which HDR release channel an install came from.</summary>
/// <remarks>
/// Derived from the version-string suffix the CI workflows append, exactly as the
/// launcher does it (<c>install.ts:12-30</c>). The channel decides which GitHub repo
/// the file manifest is fetched from.
/// </remarks>
public enum ReleaseChannel
{
    /// <summary>No recognizable suffix, so it's a private or dev build. Manifest lookup is skipped.</summary>
    Unknown,

    /// <summary>HDR-Development/HDR-Releases</summary>
    Beta,

    /// <summary>HDR-Development/HDR-PreReleases</summary>
    PreRelease,
}

/// <summary>Versions read out of the install.</summary>
/// <param name="PluginVersion">Contents of ui/hdr_version.txt, e.g. "v0.50.5-prerelease".</param>
/// <param name="AssetsVersion">Contents of hdr-assets/ui/romfs_version.txt, e.g. "v0.34.76".</param>
/// <param name="Channel">Release channel implied by the plugin version.</param>
/// <param name="MainModFolder">
/// The mod folder HDR itself is installed in. Usually "ultimate/mods/hdr", but CI
/// also produces hdr-pr and hdr-private, and local dev builds default to hdr-dev.
/// </param>
public sealed record HdrVersionInfo(
    string? PluginVersion,
    string? AssetsVersion,
    ReleaseChannel Channel,
    string? MainModFolder)
{
    /// <summary>The release tag, i.e. the version with its channel suffix stripped.</summary>
    public string? ReleaseTag => PluginVersion?.Split('-', 2)[0];

    public static HdrVersionInfo Empty { get; } =
        new(null, null, ReleaseChannel.Unknown, null);

    public static ReleaseChannel ChannelFor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return ReleaseChannel.Unknown;
        }

        // Order matters: "prerelease" also contains "release" but not "beta",
        // and the launcher tests prerelease first.
        if (version.Contains("prerelease", StringComparison.OrdinalIgnoreCase))
        {
            return ReleaseChannel.PreRelease;
        }

        return version.Contains("beta", StringComparison.OrdinalIgnoreCase)
            ? ReleaseChannel.Beta
            : ReleaseChannel.Unknown;
    }

    public static string? RepositoryFor(ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Beta => "HDR-Releases",
        ReleaseChannel.PreRelease => "HDR-PreReleases",
        _ => null,
    };
}
