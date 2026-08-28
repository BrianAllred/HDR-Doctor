using HdrDoctor.Core.Model;
using HdrDoctor.Core.Profiles;
using HdrDoctor.Core.Services;
using HdrDoctor.Core.Sources;

// Runs a real scan from the command line. Kept around because pointing it at an
// actual install is the fastest way to sanity-check a change to the checks.
//
//   hdr-doctor-cli [folder] [--verify] [--emulator-settings] [--update]
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

var progress = new Progress<ScanProgress>(p => Console.Error.WriteLine($"  .. {p.Activity} {p.Detail}"));

// Writes the optimal emulator settings
//
// Doesn't ask about the iffy settings, a user using this tool can fix them
// manually if needed.
if (args.Any(a => a is "--emulator-settings" or "-e"))
{
    if (profile.Emulator is not { } target || !target.HasConfig)
    {
        Console.Error.WriteLine(
            $"{root} does not belong to an emulator whose config this can find, so there is nothing to write.");

        return 1;
    }

    var running = EmulatorProcessGuard.Find(target);

    if (running.Count > 0)
    {
        Console.Error.WriteLine(
            $"{target.ProductName} is running ({string.Join(", ", running.Select(r => $"{r.Name} pid {r.Pid}"))}). "
            + "It rewrites its settings on exit, so anything written now would be undone. "
            + "Close it and run this again.");

        return 1;
    }

    var plan = await EmulatorSettingsWriter.PlanAsync(target, CancellationToken.None);

    if (plan.IsEmpty)
    {
        Console.WriteLine($"Every optimal setting on is already correct in {target.ProductName}.");
        return 0;
    }

    foreach (var change in plan.Changes)
    {
        Console.WriteLine(
            $"  {change.DisplayName} -> {change.Summary}"
            + $"  (was {change.Current}, writing {Path.GetFileName(change.FilePath)})");
    }

    foreach (var cache in plan.PptcCaches)
    {
        Console.WriteLine($"  deleting the stale PPTC cache at {cache}");
    }

    await EmulatorSettingsWriter.ApplyAsync(target, plan, progress, CancellationToken.None);

    Console.WriteLine(
        $"Wrote {plan.Changes.Count} setting{(plan.Changes.Count == 1 ? "" : "s")} to {target.ProductName}. "
        + "The previous config is alongside it as .bak.");

    return 0;
}

await using var source = new LocalDirectorySource(root);

var service = new ScanService(new ReleaseManifestClient(http), paths);

var result = await service.ScanAsync(profile, source, ignoreMusicFiles: false, progress, CancellationToken.None);

if (verify)
{
    var verification = await service.VerifyFilesAsync(
        profile, source, ignoreMusicFiles: false, progress, CancellationToken.None);

    result = result.WithVerification(verification);
}

Console.WriteLine(ReportWriter.Write(result));
return 0;
