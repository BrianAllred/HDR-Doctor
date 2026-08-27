using System.Net;

namespace HdrDoctor.Core.Services;

/// <summary>
/// A minimal reader for yuzu-family <c>qt-config.ini</c> files.
/// </summary>
/// <remarks>
/// Qt's settings writer is not quite standard INI. Two quirks matter here:
/// section names are percent-encoded, so <c>[Data Storage]</c> is written
/// <c>[Data%20Storage]</c>; and each setting is stored as up to three lines —
/// <c>key=value</c>, <c>key\default=true|false</c>, and in per-game files
/// <c>key\use_global=true|false</c>. The backslash-suffixed lines are metadata about
/// the settings.
/// </remarks>
public sealed class IniFile
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections;

    private IniFile(Dictionary<string, Dictionary<string, string>> sections) => _sections = sections;

    public static IniFile Parse(string content)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        sections[string.Empty] = current;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');

            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                var name = WebUtility.UrlDecode(line[1..^1]);
                if (!sections.TryGetValue(name, out current!))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    sections[name] = current;
                }

                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            current[key] = value.Trim('"');
        }

        return new IniFile(sections);
    }

    public static async Task<IniFile?> LoadAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        return Parse(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
    }

    public string? GetRaw(string section, string key) =>
        _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value)
            ? value
            : null;

    /// <summary>
    /// True when this file's per-game entry defers to the global config. Qt only
    /// writes <c>use_global</c> into per-game files; its absence means "defer".
    /// </summary>
    public bool UsesGlobal(string section, string key) =>
        !string.Equals(GetRaw(section, $"{key}\\use_global"), "false", StringComparison.OrdinalIgnoreCase);
}
