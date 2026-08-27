using HdrDoctor.Core.Model;
using HdrDoctor.Core.Profiles;
using HdrDoctor.Core.Services;
using HdrDoctor.Core.Sources;

// Runs a real scan from the command line. Kept around because pointing it at an
// actual install is the fastest way to sanity-check a change to the checks.
//
//   hdr-doctor-cli [folder] [--verify] [--update]
//
// File verification is a step of its own here too: it hashes the whole install, so
// it happens only when --verify asks for it, and the report says so when it did not.
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("hdr-doctor/1.0");

// Replaces this executable
if (args.Any(a => a is "--update" or "-u"))
{
    var updater = new UpdateService(http);

    try
    {
        var available = await updater.CheckAsync(CancellationToken.None);

        if (available is null)
        {
            Console.WriteLine($"HDR Doctor {UpdateService.CurrentVersion.ToString(3)} is the newest build for this platform.");
            return 0;
        }

        Console.Error.WriteLine($"Updating to {available.Tag} ({available.AssetName}).");

        var installed = await updater.ApplyAsync(
            available,
            new Progress<ScanProgress>(p => Console.Error.WriteLine($"  .. {p.Activity} {p.Detail}")),
            CancellationToken.None);

        Console.WriteLine($"Updated to {available.Tag}. Run {installed} again.");
        return 0;
    }
    catch (Exception e) when (e is HttpRequestException or InvalidOperationException or UnauthorizedAccessException or IOException)
    {
        Console.Error.WriteLine($"Update failed: {e.Message}");
        return 1;
    }
}

var root = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "/home/brian/.local/share/eden/sdmc";
var verify = args.Any(a => a is "--verify" or "-v");
var paths = SystemAppPaths.Instance;

var emulator = new EmulatorLocator(paths).IdentifyByPath(root);
var profile = (emulator is not null
    ? InstallProfile.ForEmulator(emulator)
    : InstallProfile.ForLocal(Path.GetFileName(root), root, ProfileStore.SuggestPlatform(root, paths)))
    with { IsFtpMount = FtpMountDetector.IsFtpMount(root) };

Console.Error.WriteLine($"profile: {profile.Name} platform={profile.Platform} emulator={profile.EmulatorName ?? "-"}");

await using var source = new LocalDirectorySource(root);

var service = new ScanService(new ReleaseManifestClient(http), paths);
var progress = new Progress<ScanProgress>(p => Console.Error.WriteLine($"  .. {p.Activity} {p.Detail}"));

var result = await service.ScanAsync(profile, source, ignoreMusicFiles: false, progress, CancellationToken.None);

if (verify)
{
    var verification = await service.VerifyFilesAsync(
        profile, source, ignoreMusicFiles: false, progress, CancellationToken.None);

    result = result.WithVerification(verification);
}

Console.WriteLine(ReportWriter.Write(result));
return 0;
