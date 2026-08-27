using System.Security.Cryptography;
using FluentFTP;
using FluentFTP.Exceptions;

namespace HdrDoctor.Core.Sources;

/// <summary>
/// A read-only view of a Switch's SD card over its FTP server.
/// </summary>
/// <remarks>
/// Lets someone diagnose a real console without pulling the card out, which is the
/// difference between a two-minute check and a fifteen-minute one.
///
/// It implements <see cref="IInstallSource"/> and deliberately not
/// <see cref="IMutableInstallSource"/>. Fixes are not merely hidden over FTP — the
/// types make them impossible. Writing to a live console's SD card over a flaky
/// wireless link, with the game potentially running, is not a risk worth taking for
/// the convenience.
///
/// A practical consequence: hashing is a full download of every file. Over wifi that
/// is slow enough that the caller should expect a long scan, which is why hashing runs
/// serially here rather than in parallel — several concurrent transfers make a Switch's
/// FTP server slower, not faster.
/// </remarks>
public sealed class FtpSource : IInstallSource
{
    private readonly AsyncFtpClient _client;

    private FtpSource(AsyncFtpClient client, string rootDescription)
    {
        _client = client;
        RootDescription = rootDescription;
    }

    public string RootDescription { get; }

    public static async Task<FtpSource> ConnectAsync(
        string host,
        int port,
        string? username,
        string? password,
        CancellationToken ct)
    {
        var client = new AsyncFtpClient(host, username ?? string.Empty, password ?? string.Empty, port);
        client.Config.ConnectTimeout = 10_000;
        client.Config.ReadTimeout = 30_000;

        // A Switch's SD card collects mod folders named by whoever packaged them, and
        // a stray control character in one is enough for the default sanitizer to
        // throw mid-listing and take the whole scan down. Renaming instead means one
        // bad name costs one wrong path in the report, not the report.
        client.Config.SanitizeMode = FtpSanitize.Rename;

        try
        {
            await client.Connect(ct).ConfigureAwait(false);

            // Connecting only proves the server is there and accepted the login.
            // Listing the root proves it will hand the card over, which is the half a
            // wrong user fails at — and a server that refuses here would otherwise
            // produce a scan reporting every HDR folder as missing.
            await client.GetListing("/", FtpListOption.Auto, ct).ConfigureAwait(false);
        }
        catch (FtpAuthenticationException e)
        {
            await CloseAsync(client).ConfigureAwait(false);

            // Translated so callers can tell a login the server refused from a host it
            // cannot reach without referencing FluentFTP. Only the first is worth
            // asking the user about.
            throw new FtpLoginRefusedException(e.Message, e);
        }
        catch
        {
            await CloseAsync(client).ConfigureAwait(false);
            throw;
        }

        return new FtpSource(client, $"ftp://{host}:{port}/");
    }

    /// <summary>Closes a client that never became a source, best effort.</summary>
    private static async Task CloseAsync(AsyncFtpClient client)
    {
        try
        {
            await client.Disconnect().ConfigureAwait(false);
        }
        catch
        {
            // Failing to hang up on a connection that already failed is not news.
        }

        client.Dispose();
    }

    public async Task<bool> FileExistsAsync(string relativePath, CancellationToken ct) =>
        await _client.FileExists(ToRemote(relativePath), ct).ConfigureAwait(false);

    public async Task<bool> DirectoryExistsAsync(string relativePath, CancellationToken ct) =>
        await _client.DirectoryExists(ToRemote(relativePath), ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<SourceEntry>> ListAsync(
        string relativeDirectory,
        bool recursive,
        CancellationToken ct)
    {
        var remote = ToRemote(relativeDirectory);

        if (!await _client.DirectoryExists(remote, ct).ConfigureAwait(false))
        {
            return [];
        }

        var options = recursive ? FtpListOption.Recursive : FtpListOption.Auto;
        var listing = await _client.GetListing(remote, options, ct).ConfigureAwait(false);

        return [.. listing
            .Select(item => new SourceEntry(
                ToRelative(item.FullName),
                item.Type == FtpObjectType.Directory,
                item.Type == FtpObjectType.Directory ? 0 : item.Size))];
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct)
    {
        // Buffered into memory rather than streamed: the Switch's FTP server does not
        // cope well with a long-lived data connection being held open while the caller
        // does something else with it.
        var buffer = new MemoryStream();
        await _client.DownloadStream(buffer, ToRemote(relativePath), token: ct).ConfigureAwait(false);
        buffer.Position = 0;
        return buffer;
    }

    public async Task<string?> ReadAllTextAsync(string relativePath, CancellationToken ct)
    {
        if (!await FileExistsAsync(relativePath, ct).ConfigureAwait(false))
        {
            return null;
        }

        await using var stream = await OpenReadAsync(relativePath, ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    public async Task<string> ComputeMd5Async(string relativePath, CancellationToken ct)
    {
        await using var stream = await OpenReadAsync(relativePath, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(await MD5.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    public async Task<SourceEntry?> StatAsync(string relativePath, CancellationToken ct)
    {
        var remote = ToRemote(relativePath);

        if (await _client.DirectoryExists(remote, ct).ConfigureAwait(false))
        {
            return new SourceEntry(Normalize(relativePath), true, 0);
        }

        if (!await _client.FileExists(remote, ct).ConfigureAwait(false))
        {
            return null;
        }

        var size = await _client.GetFileSize(remote, -1, ct).ConfigureAwait(false);
        return new SourceEntry(Normalize(relativePath), false, size);
    }

    private static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').Trim('/');

    private static string ToRemote(string relativePath) => "/" + Normalize(relativePath);

    private static string ToRelative(string remotePath) =>
        remotePath.Replace('\\', '/').TrimStart('/');

    public async ValueTask DisposeAsync()
    {
        await _client.Disconnect().ConfigureAwait(false);
        _client.Dispose();
    }
}
