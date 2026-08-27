using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

/// <summary>
/// Host directories pointed at a temp folder, so path-dependent checks can be tested
/// without touching the developer's real config — and so Windows and macOS layouts
/// can be exercised from Linux.
/// </summary>
public sealed class FakeAppPaths : IAppPaths, IDisposable
{
    private readonly string _root;

    public FakeAppPaths(bool windows = false, bool macOs = false)
    {
        _root = Path.Combine(Path.GetTempPath(), "hdr-paths-tests", Guid.NewGuid().ToString("n"));
        IsWindows = windows;
        IsMacOs = macOs;

        Directory.CreateDirectory(ConfigHome);
        Directory.CreateDirectory(DataHome);
        Directory.CreateDirectory(LocalPrograms);
    }

    public string ConfigHome => Path.Combine(_root, "config");

    public string DataHome => Path.Combine(_root, "data");

    public string LocalPrograms => Path.Combine(_root, "programs");

    public string Home => _root;

    public bool IsWindows { get; }

    public bool IsMacOs { get; }

    /// <summary>Writes the desktop launcher's config where the Linux launcher keeps it.</summary>
    public string WriteLauncherConfig(string? ryuPath, string? sdcardPath)
    {
        var directory = Path.Combine(ConfigHome, "hdr-launcher");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "launcher-config.json");
        File.WriteAllText(path, $$"""
            {"ryuPath": {{Json(ryuPath)}}, "sdcardPath": {{Json(sdcardPath)}}}
            """);

        return path;
    }

    private static string Json(string? value) =>
        value is null ? "null" : System.Text.Json.JsonSerializer.Serialize(value);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
