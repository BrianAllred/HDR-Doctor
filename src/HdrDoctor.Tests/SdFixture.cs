using System.Security.Cryptography;
using HdrDoctor.Core;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Services;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.Tests;

/// <summary>
/// Builds a throwaway SD-card tree on disk for a test to scan.
/// </summary>
/// <remarks>
/// Real directories rather than an in-memory filesystem, because the behavior most
/// worth testing — case-insensitive resolution, real file sizes and hashes — is
/// precisely the behavior a fake filesystem would paper over.
/// </remarks>
public sealed class SdFixture : IDisposable
{
    public SdFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "hdr-doctor-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>Creates a file, and any directories above it, with optional contents.</summary>
    public SdFixture WithFile(string relativePath, string contents = "")
    {
        var absolute = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, contents);
        return this;
    }

    /// <summary>Creates a file of a given size, for checks that care about length.</summary>
    public SdFixture WithFileOfSize(string relativePath, long bytes)
    {
        var absolute = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        using var stream = new FileStream(absolute, FileMode.Create);
        stream.SetLength(bytes);
        return this;
    }

    public SdFixture WithDirectory(string relativePath)
    {
        Directory.CreateDirectory(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return this;
    }

    /// <summary>A minimal but structurally valid HDR install.</summary>
    public SdFixture WithHealthyInstall(InstallPlatform platform = InstallPlatform.Switch)
    {
        WithFile(HdrPaths.SkylineSubsdk, "subsdk");
        WithFile(HdrPaths.SkylineNpdm, "npdm");

        foreach (var plugin in HdrPaths.RequiredPlugins)
        {
            WithFile($"{HdrPaths.PluginsDir}/{plugin.FileName}", plugin.FileName);
        }

        if (platform == InstallPlatform.Switch)
        {
            WithFile($"{HdrPaths.PluginsDir}/{HdrPaths.LauncherNro}", "launcher");
        }

        WithFile($"{HdrPaths.HdrDir}/plugin.nro", "plugin");
        WithFile(HdrPaths.HdrVersionFile, "v9.9.9-prerelease");
        WithFile(HdrPaths.RomfsVersionFile, "v8.8.8");
        WithDirectory(HdrPaths.HdrStagesDir);
        WithFileOfSize(HdrPaths.StageAltsHashes, 40L * 1024 * 1024);

        return this;
    }

    public LocalDirectorySource Source() => new(Root);

    /// <summary>
    /// Builds a manifest describing the fixture exactly as it stands, so a test can
    /// then break one specific thing and assert only that.
    /// </summary>
    public ReleaseManifest SnapshotManifest(string tag = "v9.9.9")
    {
        var entries = new List<string>();

        foreach (var folder in HdrPaths.HdrOwnedFolders)
        {
            var absolute = Path.Combine(Root, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absolute))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(Root, file).Replace(Path.DirectorySeparatorChar, '/');
                entries.Add($$"""{"path": "/{{relative}}", "hash": "{{Md5(file)}}"}""");
            }
        }

        return ReleaseManifest.Parse($"[{string.Join(",", entries)}]", tag);
    }

    private static string Md5(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(MD5.HashData(stream));
    }

    /// <summary>
    /// Runs one check against the fixture. Deliberately does not go through
    /// ScanService — a check test should fail for reasons inside that check.
    /// </summary>
    public async Task<IReadOnlyList<Finding>> RunAsync(
        ICheck check,
        InstallPlatform platform = InstallPlatform.Emulator,
        ReleaseManifest? manifest = null,
        EmulatorInstallation? emulator = null)
    {
        await using var source = Source();

        var ctx = new ScanContext
        {
            Source = source,
            Platform = platform,
            Versions = await HdrVersionReader.ReadAsync(source, CancellationToken.None),
            Manifest = manifest,
            Emulator = emulator,
        };

        // Categorized the same way a real scan does it, so a check test sees the
        // findings the report would show.
        return [.. CheckRunner.Categorize(check, await check.RunAsync(ctx, CancellationToken.None))];
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked temp file must never fail an otherwise-passing test.
        }
    }
}

public static class FindingAssertions
{
    public static Finding Single(this IReadOnlyList<Finding> findings, string titleFragment) =>
        findings.SingleOrDefault(f => f.Title.Contains(titleFragment, StringComparison.OrdinalIgnoreCase))
        ?? throw new Xunit.Sdk.XunitException(
            $"No finding titled like '{titleFragment}'. Got: "
            + string.Join(" | ", findings.Select(f => $"[{f.Severity}] {f.Title}")));

    public static bool Has(this IReadOnlyList<Finding> findings, string titleFragment) =>
        findings.Any(f => f.Title.Contains(titleFragment, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<Finding> Problems(this IReadOnlyList<Finding> findings) =>
        [.. findings.Where(f => f.Severity != Severity.Ok)];
}
