using System.Security.Cryptography;

namespace HdrDoctor.Core.Sources;

/// <summary>
/// An installation on a local filesystem: a mounted SD card or an emulator's
/// sdmc directory. This is the only source that supports fixes.
/// </summary>
public sealed class LocalDirectorySource : IMutableInstallSource
{
    private readonly CaseInsensitivePathResolver _resolver;

    public LocalDirectorySource(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Install root must not be empty.", nameof(rootPath));
        }

        var full = Path.GetFullPath(rootPath);
        _resolver = new CaseInsensitivePathResolver(full);
        RootDescription = full;
    }

    public string RootDescription { get; }

    public Task<bool> FileExistsAsync(string relativePath, CancellationToken ct) =>
        Task.FromResult(File.Exists(_resolver.Resolve(relativePath)));

    public Task<bool> DirectoryExistsAsync(string relativePath, CancellationToken ct) =>
        Task.FromResult(Directory.Exists(_resolver.Resolve(relativePath)));

    public Task<IReadOnlyList<SourceEntry>> ListAsync(string relativeDirectory, bool recursive, CancellationToken ct)
    {
        var absolute = _resolver.Resolve(relativeDirectory);
        if (!Directory.Exists(absolute))
        {
            return Task.FromResult<IReadOnlyList<SourceEntry>>([]);
        }

        var options = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var results = new List<SourceEntry>();

        foreach (var entry in Directory.EnumerateFileSystemEntries(absolute, "*", options))
        {
            ct.ThrowIfCancellationRequested();

            var info = new FileInfo(entry);
            var isDirectory = info.Attributes.HasFlag(FileAttributes.Directory);
            results.Add(new SourceEntry(
                _resolver.ToRelative(entry),
                isDirectory,
                isDirectory ? 0 : info.Length));
        }

        return Task.FromResult<IReadOnlyList<SourceEntry>>(results);
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct) =>
        Task.FromResult<Stream>(new FileStream(
            _resolver.Resolve(relativePath),
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 64 * 1024,
            useAsync: true));

    public async Task<string?> ReadAllTextAsync(string relativePath, CancellationToken ct)
    {
        var absolute = _resolver.Resolve(relativePath);
        return File.Exists(absolute) ? await File.ReadAllTextAsync(absolute, ct).ConfigureAwait(false) : null;
    }

    public async Task<string> ComputeMd5Async(string relativePath, CancellationToken ct)
    {
        await using var stream = await OpenReadAsync(relativePath, ct).ConfigureAwait(false);
        var hash = await MD5.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public Task<SourceEntry?> StatAsync(string relativePath, CancellationToken ct)
    {
        var absolute = _resolver.Resolve(relativePath);

        if (Directory.Exists(absolute))
        {
            return Task.FromResult<SourceEntry?>(new SourceEntry(_resolver.ToRelative(absolute), true, 0));
        }

        if (File.Exists(absolute))
        {
            return Task.FromResult<SourceEntry?>(
                new SourceEntry(_resolver.ToRelative(absolute), false, new FileInfo(absolute).Length));
        }

        return Task.FromResult<SourceEntry?>(null);
    }

    public Task DeleteFileAsync(string relativePath, CancellationToken ct)
    {
        var absolute = _resolver.Resolve(relativePath);
        if (File.Exists(absolute))
        {
            File.Delete(absolute);
            _resolver.Invalidate();
        }

        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string relativePath, CancellationToken ct)
    {
        var absolute = _resolver.Resolve(relativePath);
        if (Directory.Exists(absolute))
        {
            Directory.Delete(absolute, recursive: true);
            _resolver.Invalidate();
        }

        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string relativePath, CancellationToken ct)
    {
        Directory.CreateDirectory(_resolver.Resolve(relativePath));
        _resolver.Invalidate();
        return Task.CompletedTask;
    }

    public async Task WriteAsync(string relativePath, Stream content, CancellationToken ct)
    {
        var absolute = _resolver.Resolve(relativePath);
        var directory = Path.GetDirectoryName(absolute);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using (var file = new FileStream(absolute, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        {
            await content.CopyToAsync(file, ct).ConfigureAwait(false);
        }

        _resolver.Invalidate();
    }

    public Task MoveAsync(string fromRelativePath, string toRelativePath, CancellationToken ct)
    {
        var from = _resolver.Resolve(fromRelativePath);
        var to = _resolver.Resolve(toRelativePath);

        var directory = Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Move(from, to, overwrite: true);
        _resolver.Invalidate();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
