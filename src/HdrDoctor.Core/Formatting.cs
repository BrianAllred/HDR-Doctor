namespace HdrDoctor.Core;

/// <summary>Display helpers shared by the checks, the report and the UI.</summary>
public static class Formatting
{
    /// <summary>
    /// A file or folder size a person can read at a glance.
    /// </summary>
    /// <remarks>
    /// Tiered rather than fixed to MB because both callers show sizes that can be
    /// tiny: a truncated download and a mod folder holding one file both read as
    /// "0 MB" otherwise, which says nothing.
    /// </remarks>
    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} bytes",
    };
}
