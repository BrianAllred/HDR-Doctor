using System;
using System.Net.Http;
using HdrDoctor.Core.Profiles;
using HdrDoctor.Core.Services;

namespace HdrDoctor.App.Services;

/// <summary>
/// The application's long-lived services.
/// </summary>
/// <remarks>
/// Avoiding the complexity of a DI container for now,
/// since the scope of the app is so small.
/// </remarks>
public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        Paths = SystemAppPaths.Instance;

        Http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("hdr-doctor/1.0");

        Scanner = new ScanService(new ReleaseManifestClient(Http), Paths);
        FreshInstall = new FreshInstallService(Http);
        Profiles = new ProfileStore(Paths);
    }

    public IAppPaths Paths { get; }

    public HttpClient Http { get; }

    public ScanService Scanner { get; }

    public FreshInstallService FreshInstall { get; }

    public ProfileStore Profiles { get; }

    public void Dispose() => Http.Dispose();
}
