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

    /// <summary>
    /// Sets one key in a Qt-style ini, editing the file's lines in place.
    /// </summary>
    /// <remarks>
    /// Edits by line instead of using an INI parser since Qt's format isn't
    /// standard.
    /// </remarks>
    public static string SetValue(string content, string section, string key, string value)
    {
        // Qt writes the host's line ending; keep whichever this file already uses.
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Split('\n').Select(line => line.TrimEnd('\r')).ToList();

        var (start, end) = FindSection(lines, section);

        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add($"[{WebUtility.UrlEncode(section)}]");
            lines.Add($"{key}\\default=false");
            lines.Add($"{key}={value}");

            return string.Join(newline, lines);
        }

        var valueLine = -1;
        var defaultLine = -1;
        var lastContent = start;

        for (var i = start + 1; i < end; i++)
        {
            var trimmed = lines[i].Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            lastContent = i;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = trimmed[..separator].Trim();

            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                valueLine = i;
            }
            else if (string.Equals(name, $"{key}\\default", StringComparison.OrdinalIgnoreCase))
            {
                defaultLine = i;
            }
        }

        if (defaultLine >= 0)
        {
            lines[defaultLine] = $"{key}\\default=false";
        }

        if (valueLine >= 0)
        {
            lines[valueLine] = $"{key}={value}";
            return string.Join(newline, lines);
        }

        var at = (defaultLine >= 0 ? defaultLine : lastContent) + 1;

        if (defaultLine < 0)
        {
            lines.Insert(at++, $"{key}\\default=false");
        }

        lines.Insert(at, $"{key}={value}");

        return string.Join(newline, lines);
    }

    public static async Task SetValueAsync(
        string path,
        string section,
        string key,
        string value,
        CancellationToken ct)
    {
        var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(path, SetValue(content, section, key, value), ct).ConfigureAwait(false);
    }

    /// <summary>The section's header line and the line the next section starts on.</summary>
    private static (int Start, int End) FindSection(List<string> lines, string section)
    {
        var start = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();

            if (line.Length < 2 || line[0] != '[' || line[^1] != ']')
            {
                continue;
            }

            if (start >= 0)
            {
                return (start, i);
            }

            if (string.Equals(WebUtility.UrlDecode(line[1..^1]), section, StringComparison.OrdinalIgnoreCase))
            {
                start = i;
            }
        }

        return (start, lines.Count);
    }
}
