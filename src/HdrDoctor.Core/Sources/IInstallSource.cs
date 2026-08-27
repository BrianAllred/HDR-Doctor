namespace HdrDoctor.Core.Sources;

/// <summary>
/// Read access to an HDR installation, wherever it lives: a mounted SD card, an
/// emulator's sdmc directory, or a Switch over FTP.
/// </summary>
/// <remarks>
/// Checks talk to this and never to <see cref="System.IO"/> directly. That is what
/// lets the same check run unchanged against a local folder and a remote Switch.
///
/// All paths are relative to the install root, forward-slash separated, with no
/// leading slash. Implementations are responsible for resolving them
/// case-insensitively — see <see cref="LocalDirectorySource"/> for why that matters.
/// </remarks>
public interface IInstallSource : IAsyncDisposable
{
    /// <summary>The install root, as the user would recognize it.</summary>
    string RootDescription { get; }

    Task<bool> FileExistsAsync(string relativePath, CancellationToken ct);

    Task<bool> DirectoryExistsAsync(string relativePath, CancellationToken ct);

    /// <summary>
    /// Lists the contents of a directory. Returns an empty list when the directory
    /// does not exist — callers that care about the difference should ask
    /// <see cref="DirectoryExistsAsync"/> first.
    /// </summary>
    Task<IReadOnlyList<SourceEntry>> ListAsync(string relativeDirectory, bool recursive, CancellationToken ct);

    Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct);

    /// <summary>Reads a text file, or returns null when it does not exist.</summary>
    Task<string?> ReadAllTextAsync(string relativePath, CancellationToken ct);

    /// <summary>Lowercase hex MD5, matching the format used by content_hashes.json.</summary>
    Task<string> ComputeMd5Async(string relativePath, CancellationToken ct);

    Task<SourceEntry?> StatAsync(string relativePath, CancellationToken ct);
}

/// <summary>
/// An install source that can also be modified. Only local sources implement this.
/// </summary>
/// <remarks>
/// Remediations take this type, so it is impossible to write a fix that could run
/// against a read-only FTP connection — the code would not compile. This is
/// deliberate: the UI banner saying "fixes are disabled" is a courtesy, not the
/// mechanism.
/// </remarks>
public interface IMutableInstallSource : IInstallSource
{
    Task DeleteFileAsync(string relativePath, CancellationToken ct);

    Task DeleteDirectoryAsync(string relativePath, CancellationToken ct);

    Task CreateDirectoryAsync(string relativePath, CancellationToken ct);

    Task WriteAsync(string relativePath, Stream content, CancellationToken ct);

    /// <summary>Moves a file, creating the destination directory if needed.</summary>
    Task MoveAsync(string fromRelativePath, string toRelativePath, CancellationToken ct);
}
