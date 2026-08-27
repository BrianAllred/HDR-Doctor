using HdrDoctor.Core.Services;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Core.Model;

/// <summary>Everything a check needs to inspect an installation.</summary>
public sealed class ScanContext
{
    public required IInstallSource Source { get; init; }

    public required InstallPlatform Platform { get; init; }

    /// <summary>Versions read from the install. <see cref="HdrVersionInfo.Empty"/> when HDR is not installed.</summary>
    public required HdrVersionInfo Versions { get; init; }

    /// <summary>
    /// The per-file manifest for the detected release.
    /// </summary>
    public ReleaseManifest? Manifest { get; init; }

    /// <summary>Why the manifest is null, for the report's "skipped checks" section.</summary>
    public string? ManifestUnavailableReason { get; init; }

    /// <summary>The emulator this install belongs to, when known.</summary>
    public EmulatorInstallation? Emulator { get; init; }

    /// <summary>Whether to skip hash-checking music files, as the launcher optionally does.</summary>
    public bool IgnoreMusicFiles { get; init; }

    public IProgress<ScanProgress>? Progress { get; init; }

    public void Report(string activity, string? detail = null, double? fraction = null) =>
        Progress?.Report(new ScanProgress(activity, detail, fraction));
}

/// <param name="Activity">What is happening, e.g. "Verifying files".</param>
/// <param name="Detail">The current item, e.g. the file being hashed.</param>
/// <param name="Fraction">0..1 where known, null for indeterminate.</param>
public sealed record ScanProgress(string Activity, string? Detail, double? Fraction);
