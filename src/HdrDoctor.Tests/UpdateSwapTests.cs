using System.Net;
using System.Security.Cryptography;
using System.Text;
using HdrDoctor.Core.Services;

namespace HdrDoctor.Tests;

/// <summary>
/// The swap itself, against a throwaway file standing in for the executable.
/// </summary>
/// <remarks>
/// This is the one path in the app that can leave a user with no working HDR Doctor at
/// all, so it is exercised rather than reasoned about. <c>$APPIMAGE</c> is what makes
/// that possible: <see cref="UpdateService.ExecutablePath"/> prefers it over
/// <see cref="Environment.ProcessPath"/>, so pointing it at a temp file targets that
/// instead of the test runner.
/// </remarks>
public sealed class UpdateSwapTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "hdr-doctor-update-" + Guid.NewGuid().ToString("n"));

    private readonly string _executable;
    private readonly string? _previousAppImage = Environment.GetEnvironmentVariable("APPIMAGE");

    public UpdateSwapTests()
    {
        Directory.CreateDirectory(_directory);
        _executable = Path.Combine(_directory, "HDR-Doctor-x86_64.AppImage");
        File.WriteAllText(_executable, "the old version");

        Environment.SetEnvironmentVariable("APPIMAGE", _executable);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPIMAGE", _previousAppImage);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_verified_download_takes_the_running_executable_s_place()
    {
        var payload = "the new version"u8.ToArray();
        using var http = new HttpClient(new CannedRelease(payload, Sha256Of(payload)));

        var installed = await new UpdateService(http).ApplyAsync(Update(), null, CancellationToken.None);

        Assert.Equal(_executable, installed);
        Assert.Equal("the new version", File.ReadAllText(_executable));
    }

    [Fact]
    public async Task Nothing_is_left_behind_beside_the_executable()
    {
        var payload = "the new version"u8.ToArray();
        using var http = new HttpClient(new CannedRelease(payload, Sha256Of(payload)));

        await new UpdateService(http).ApplyAsync(Update(), null, CancellationToken.None);

        // The staged download is always cleaned up; the moved-aside original only
        // survives on Windows, where it is still locked until this process exits.
        Assert.False(File.Exists(_executable + ".new"));
        Assert.Equal(OperatingSystem.IsWindows(), File.Exists(_executable + ".old"));
    }

    [Fact]
    public async Task A_download_that_does_not_match_the_checksum_is_refused()
    {
        var payload = "a corrupted download"u8.ToArray();
        using var http = new HttpClient(new CannedRelease(payload, Sha256Of("something else"u8.ToArray())));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new UpdateService(http).ApplyAsync(Update(), null, CancellationToken.None));

        // The point of refusing: the user still has the version they started.
        Assert.Equal("the old version", File.ReadAllText(_executable));
        Assert.False(File.Exists(_executable + ".new"));
    }

    [Fact]
    public async Task A_release_with_no_checksum_for_this_asset_is_refused()
    {
        var payload = "the new version"u8.ToArray();
        using var http = new HttpClient(new CannedRelease(payload, sums: "abc123  some-other-asset\n"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new UpdateService(http).ApplyAsync(Update(), null, CancellationToken.None));

        Assert.Equal("the old version", File.ReadAllText(_executable));
    }

    private static AppUpdate Update() => new(
        new Version(9, 9, 9),
        "v9.9.9",
        "HDR-Doctor-x86_64.AppImage",
        "https://example.invalid/download",
        "https://example.invalid/SHA256SUMS",
        Notes: null);

    private static string Sha256Of(byte[] payload) =>
        Convert.ToHexStringLower(SHA256.HashData(payload));

    /// <summary>Serves one release: the asset on any URL, and its SHA256SUMS.</summary>
    private sealed class CannedRelease : HttpMessageHandler
    {
        private readonly byte[] _payload;
        private readonly string _sums;

        public CannedRelease(byte[] payload, string? hash = null, string? sums = null)
        {
            _payload = payload;
            _sums = sums ?? $"{hash}  HDR-Doctor-x86_64.AppImage\n";
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS", StringComparison.Ordinal)
                    ? new StringContent(_sums, Encoding.UTF8)
                    : new ByteArrayContent(_payload),
            });
    }
}
