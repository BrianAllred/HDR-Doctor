using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HdrDoctor.App.Services;
using HdrDoctor.Core.Checks;
using HdrDoctor.Core;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Profiles;
using HdrDoctor.Core.Remediations;
using HdrDoctor.Core.Services;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.App.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly DialogService _dialogs;

    private ProfileSettings _settings;
    private CancellationTokenSource? _scanCancellation;
    private ScanResult? _lastResult;

    /// <summary>
    /// FTP passwords that have been proven this run, by profile id.
    /// </summary>
    /// <remarks>
    /// Declining to store a password should cost one prompt per run, not one per
    /// connection: without this, adding a profile asks for the password to validate
    /// it and then the very next Scan asks again, and Verify would ask a third time.
    /// Held here rather than on the profile because saving one reloads the list from
    /// disk, which is exactly where the password deliberately is not.
    /// </remarks>
    private readonly Dictionary<string, string> _sessionPasswords = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanScan))]
    [NotifyPropertyChangedFor(nameof(CanVerify))]
    [NotifyPropertyChangedFor(nameof(CanFix))]
    [NotifyPropertyChangedFor(nameof(CanApplyEmulatorSettings))]
    [NotifyPropertyChangedFor(nameof(HasEmulatorSettings))]
    [NotifyPropertyChangedFor(nameof(IsReadOnlySource))]
    [NotifyPropertyChangedFor(nameof(ReadOnlyReason))]
    [NotifyPropertyChangedFor(nameof(CanOpenFolder))]
    public partial ProfileViewModel? SelectedProfile { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanScan))]
    [NotifyPropertyChangedFor(nameof(CanVerify))]
    [NotifyPropertyChangedFor(nameof(CanFix))]
    [NotifyPropertyChangedFor(nameof(CanApplyEmulatorSettings))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Choose an install and press Scan.";

    [ObservableProperty]
    public partial string? StatusDetail { get; set; }

    [ObservableProperty]
    public partial double ProgressFraction { get; set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanVerify))]
    [NotifyPropertyChangedFor(nameof(CanFix))]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    public partial bool HasScanned { get; set; }

    [ObservableProperty]
    public partial bool ShowPassingChecks { get; set; }

    [ObservableProperty]
    public partial bool IgnoreMusicFiles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanVerify))]
    public partial EnvironmentSummary? Environment { get; set; }

    public MainViewModel(AppServices services, DialogService dialogs)
    {
        _services = services;
        _dialogs = dialogs;
        _settings = services.Profiles.Load();

        LoadProfiles();
    }

    public ObservableCollection<ProfileViewModel> Profiles { get; } = [];

    public ObservableCollection<FindingGroupViewModel> Groups { get; } = [];

    public ObservableCollection<SkippedCheck> Skipped { get; } = [];

    public bool CanScan => !IsBusy && SelectedProfile?.IsAvailable == true;

    public bool CanOpenFolder => SelectedProfile is { IsAvailable: true, Profile.Path: not null };

    public bool CanVerify =>
        !IsBusy && HasScanned && SelectedProfile?.IsAvailable == true && Environment?.FileListAvailable == true;

    private SkippedCheck? UnverifiedNote =>
        Skipped.FirstOrDefault(s => s.CheckId == FileVerificationCheck.CheckId);

    public bool NeedsVerification => HasScanned && UnverifiedNote is not null;

    public string? VerificationPrompt => UnverifiedNote?.Reason;

    public bool HasResults => HasScanned && Groups.Count > 0;

    public bool HasSkipped => Skipped.Count > 0;

    /// <summary>
    /// True for FTP profiles because FTP is unreliable with writes, especially
    /// in the atmosphere folder.
    /// </summary>
    public bool IsReadOnlySource => SelectedProfile is not null && !SelectedProfile.Profile.SupportsFixes;

    public static string ReadOnlyReason => "This install is being read over FTP, so nothing here can be changed. You can diagnose the problem and export a report, but fixes and reinstalls are disabled.";

    public bool CanFix => !IsBusy && HasScanned && !IsReadOnlySource && SelectedFixes.Any();

    public bool HasEmulatorSettings => SelectedProfile?.Profile.Emulator?.HasConfig == true;

    public bool CanApplyEmulatorSettings => !IsBusy && HasEmulatorSettings;

    private IEnumerable<FindingViewModel> SelectedFixes =>
        Groups.SelectMany(g => g.Findings).Where(f => f is { CanBeFixed: true, SelectedForFix: true });

    // ---- Profiles -----------------------------------------------------------

    private void LoadProfiles()
    {
        Profiles.Clear();

        var stored = _settings.Profiles;

        // First run: offer whatever emulators are already on the machine rather than
        // making the user go hunting.
        if (stored.Count == 0)
        {
            stored = [.. ProfileStore.SuggestProfiles(_services.Paths)];
            if (stored.Count > 0)
            {
                _settings = _settings with { Profiles = stored };
                _services.Profiles.Save(_settings);
            }
        }

        // A folder can be an FTP mount wearing a local path — curlftpfs, a mapped FTP
        // drive. It reads like any other folder, so nothing later in the scan would
        // notice. Asked here rather than where the profile is created because it is a
        // fact about the machine right now, not about the profile: the same path is a
        // mount today and a bare folder tomorrow, and every route in — the picker, the
        // command line, stored profiles, the first-run emulator suggestions — reloads
        // through here.
        foreach (var profile in stored)
        {
            Profiles.Add(new ProfileViewModel(
                profile with { IsFtpMount = FtpMountDetector.IsFtpMount(profile.Path ?? "") }));
        }

        IgnoreMusicFiles = _settings.IgnoreMusicFiles;

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _settings.LastUsedProfileId)
                          ?? Profiles.FirstOrDefault(p => p.IsAvailable)
                          ?? Profiles.FirstOrDefault();

        if (Profiles.Count == 0)
        {
            Status = "No installs configured yet. Use \"Add folder\" to point at your SD card or emulator sdmc folder.";
        }
    }

    /// <summary>
    /// Applies anything given on the command line: select or create a profile for the
    /// named folder, optionally start scanning straight away, and optionally verify
    /// the files afterwards.
    /// </summary>
    public async Task ApplyStartupOptionsAsync(StartupOptions options)
    {
        if (options.Folder is not null)
        {
            var full = Path.GetFullPath(options.Folder);

            var existing = Profiles.FirstOrDefault(p =>
                p.Profile.Path is not null &&
                string.Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(p.Profile.Path)),
                    Path.TrimEndingDirectorySeparator(full),
                    StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                SelectedProfile = existing;
            }
            else
            {
                AddFolder(full);
            }
        }

        if (options.ScanOnStart && CanScan)
        {
            await ScanAsync();
        }

        if (options.VerifyOnStart && CanVerify)
        {
            await VerifyFilesAsync();
        }
    }

    /// <summary>
    /// Creates a profile for a folder, working out which emulator it belongs to and
    /// which platform's rules apply.
    /// </summary>
    private void AddFolder(string folder)
    {
        var emulator = new EmulatorLocator(_services.Paths).IdentifyByPath(folder);

        InstallProfile profile;

        if (emulator is not null)
        {
            profile = InstallProfile.ForEmulator(emulator);
        }
        else
        {
            profile = InstallProfile.ForLocal(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)),
                folder,
                ProfileStore.SuggestPlatform(folder, _services.Paths));
        }

        // IsFtpMount is not set here: SaveProfile reloads the list, and LoadProfiles
        // detects it for every profile.
        SaveProfile(profile, keepSelection: true);
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var folder = await _dialogs.PickFolderAsync("Select your SD card or emulator sdmc folder");
        if (folder is null)
        {
            return;
        }

        AddFolder(folder);
    }

    [RelayCommand]
    private async Task AddFtpAsync()
    {
        // The dialog does not return until the connection has been made, so anything
        // that arrives here is known to work.
        if (await _dialogs.AddFtpProfileAsync() is not { } added)
        {
            return;
        }

        CacheSessionPassword(added.Profile);

        SaveProfile(
            added.RememberPassword ? added.Profile : added.Profile with { Password = null },
            keepSelection: true);
    }

    [RelayCommand]
    private void RemoveProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        _sessionPasswords.Remove(SelectedProfile.Id);

        var remaining = _settings.Profiles.Where(p => p.Id != SelectedProfile.Id).ToList();
        _settings = _settings with { Profiles = remaining, LastUsedProfileId = null };
        _services.Profiles.Save(_settings);

        LoadProfiles();
    }

    /// <summary>
    /// Flips the platform a profile is treated as, in case
    /// auto-detection got it wrong.
    /// </summary>
    [RelayCommand]
    private void TogglePlatform()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var flipped = SelectedProfile.Profile with
        {
            Platform = SelectedProfile.Profile.Platform == InstallPlatform.Switch
                ? InstallPlatform.Emulator
                : InstallPlatform.Switch,
        };

        SaveProfile(flipped, keepSelection: true);
    }

    private void SaveProfile(InstallProfile profile, bool keepSelection = false)
    {
        var profiles = _settings.Profiles.Where(p => p.Id != profile.Id).Append(profile).ToList();

        _settings = _settings with
        {
            Profiles = profiles,
            LastUsedProfileId = profile.Id,
        };

        _services.Profiles.Save(_settings);
        LoadProfiles();

        if (keepSelection)
        {
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        }
    }

    partial void OnSelectedProfileChanged(ProfileViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        _settings = _settings with { LastUsedProfileId = value.Id };
        _services.Profiles.Save(_settings);

        ClearResults();
        Status = value.IsAvailable
            ? "Ready to scan."
            : value.Detail;
    }

    partial void OnIgnoreMusicFilesChanged(bool value)
    {
        _settings = _settings with { IgnoreMusicFiles = value };
        _services.Profiles.Save(_settings);
    }

    partial void OnShowPassingChecksChanged(bool value) => Rebuild();

    // ---- Scanning -----------------------------------------------------------

    [RelayCommand]
    private async Task ScanAsync()
    {
        ClearResults();

        await RunAgainstInstallAsync(
            "Scanning…",
            canceled: "Scan canceled.",
            failed: "The scan could not be completed.",
            failureTitle: "Scan failed",
            async (profile, source, progress, ct) =>
            {
                _lastResult = await _services.Scanner.ScanAsync(
                    profile, source, IgnoreMusicFiles, progress, ct);

                Environment = _lastResult.Environment;
                HasScanned = true;
                Rebuild();

                Status = Summarize(_lastResult);
                StatusDetail = null;
            });
    }

    [RelayCommand]
    private void CancelScan() => _scanCancellation?.Cancel();

    [RelayCommand]
    private async Task VerifyFilesAsync()
    {
        if (_lastResult is null)
        {
            return;
        }

        await RunAgainstInstallAsync(
            "Verifying files…",
            canceled: "File verification canceled.",
            failed: "The files could not be verified.",
            failureTitle: "File verification failed",
            async (profile, source, progress, ct) =>
            {
                var verification = await _services.Scanner.VerifyFilesAsync(
                    profile, source, IgnoreMusicFiles, progress, ct);

                _lastResult = _lastResult!.WithVerification(verification);
                Rebuild();

                Status = verification.Ran
                    ? Summarize(_lastResult)
                    : "The files could not be verified.";
                StatusDetail = verification.Skipped?.Reason;
            });
    }

    /// <summary>
    /// The scaffolding Scan and Verify share: mark the window busy, open the install,
    /// and turn cancellation or failure into status text instead of an unhandled
    /// exception on the UI thread.
    /// </summary>
    private async Task RunAgainstInstallAsync(
        string starting,
        string canceled,
        string failed,
        string failureTitle,
        Func<InstallProfile, IInstallSource, IProgress<ScanProgress>, CancellationToken, Task> work)
    {
        if (SelectedProfile is null || IsBusy)
        {
            return;
        }

        var profile = SelectedProfile.Profile;

        _scanCancellation = new CancellationTokenSource();
        IsBusy = true;
        IsProgressIndeterminate = true;
        Status = starting;

        try
        {
            await using var source = await OpenSourceAsync(profile, _scanCancellation.Token);
            await work(profile, source, NewProgress(), _scanCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Status = canceled;
            StatusDetail = null;
        }
        catch (Exception e)
        {
            Status = failed;
            StatusDetail = e.Message;
            await _dialogs.ShowMessageAsync(failureTitle, e.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = true;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }
    }

    /// <summary>Pipes a running operation's progress into the status bar.</summary>
    private Progress<ScanProgress> NewProgress() => new(p =>
    {
        Status = p.Activity;
        StatusDetail = p.Detail;
        IsProgressIndeterminate = p.Fraction is null;
        ProgressFraction = p.Fraction ?? 0;
    });

    /// <summary>
    /// Opens the install a profile points at, asking for a login if the server turns
    /// the stored one down.
    /// </summary>
    /// <remarks>
    /// Both Scan and Verify come through here, so the prompt lives here rather than in
    /// either of them. A profile whose password the user chose not to store fails its
    /// first connection every session by design — that is the case this exists for,
    /// which is why the first ask carries no error text.
    /// </remarks>
    private async Task<IInstallSource> OpenSourceAsync(InstallProfile profile, CancellationToken ct)
    {
        if (profile.Kind != ProfileKind.Ftp)
        {
            return new LocalDirectorySource(profile.Path!);
        }

        // The session's password outranks the file's: it is either the one the file
        // does not have, or the one that replaced a stored password the server just
        // turned down.
        profile = profile with
        {
            Password = _sessionPasswords.GetValueOrDefault(profile.Id) ?? profile.Password,
        };

        string? error = null;
        var asked = false;
        var remember = false;

        while (true)
        {
            try
            {
                var source = await FtpSource.ConnectAsync(
                    profile.Host!,
                    profile.Port,
                    profile.Username,
                    profile.Password,
                    ct);

                // Only a login that actually worked is worth keeping.
                if (asked)
                {
                    RememberLogin(profile, remember);
                }

                return source;
            }
            catch (FtpLoginRefusedException e)
            {
                // The one failure a password can fix. Anything else — no route to the
                // host, a refused port — is left to the caller to report, because no
                // amount of retyping helps.
                error = asked ? e.Message : null;
            }

            var credentials = await _dialogs.AskFtpCredentialsAsync(profile, error);

            if (credentials is null)
            {
                throw new OperationCanceledException();
            }

            profile = profile with
            {
                Username = credentials.Username,
                Password = credentials.Password,
            };

            remember = credentials.RememberPassword;
            asked = true;
        }
    }

    /// <summary>
    /// Writes a working login back: the password is kept for the session either way,
    /// and reaches the file only if the user asked for that.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="SaveProfile"/>: that reloads the whole list and
    /// resets the selection, and this runs in the middle of a scan. A corrected
    /// username does have to reach the stored profile, or the cached password would
    /// keep being offered against the name that was already refused.
    /// </remarks>
    private void RememberLogin(InstallProfile profile, bool rememberPassword)
    {
        CacheSessionPassword(profile);

        var stored = rememberPassword ? profile : profile with { Password = null };

        _settings = _settings with
        {
            Profiles = [.. _settings.Profiles.Select(p => p.Id == stored.Id ? stored : p)],
        };

        _services.Profiles.Save(_settings);

        if (Profiles.FirstOrDefault(p => p.Id == stored.Id) is { } view)
        {
            view.Profile = stored;
            view.Refresh();
        }
    }

    private void CacheSessionPassword(InstallProfile profile)
    {
        if (profile.Password is not null)
        {
            _sessionPasswords[profile.Id] = profile.Password;
        }
    }

    private static string Summarize(ScanResult result) => result.HasProblems
        ? string.Join(", ", result.ProblemCounts().Select(c => c.Severity.Describe(c.Count)))
        : "No problems found — this install looks correct.";

    private void Rebuild()
    {
        Groups.Clear();

        if (_lastResult is null)
        {
            OnPropertyChanged(nameof(HasResults));
            return;
        }

        var visible = _lastResult.Findings.Where(f => ShowPassingChecks || f.Severity != Severity.Ok);

        foreach (var group in visible.GroupBy(f => f.Category).OrderBy(g => g.Key))
        {
            var findings = group.Select(f => new FindingViewModel(f)).ToList();

            foreach (var finding in findings)
            {
                finding.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FindingViewModel.SelectedForFix))
                    {
                        OnPropertyChanged(nameof(CanFix));
                    }
                };
            }

            Groups.Add(new FindingGroupViewModel(group.Key, findings));
        }

        Skipped.Clear();
        foreach (var skipped in _lastResult.Skipped)
        {
            Skipped.Add(skipped);
        }

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasSkipped));
        OnPropertyChanged(nameof(CanFix));
        OnPropertyChanged(nameof(NeedsVerification));
        OnPropertyChanged(nameof(VerificationPrompt));
    }

    private void ClearResults()
    {
        _lastResult = null;
        Groups.Clear();
        Skipped.Clear();
        Environment = null;
        HasScanned = false;
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasSkipped));
        OnPropertyChanged(nameof(CanFix));
        OnPropertyChanged(nameof(NeedsVerification));
        OnPropertyChanged(nameof(VerificationPrompt));
    }

    // ---- Fixes --------------------------------------------------------------

    [RelayCommand]
    private async Task ApplyFixesAsync()
    {
        if (SelectedProfile is null || IsReadOnlySource)
        {
            return;
        }

        var selected = SelectedFixes.Select(f => f.Finding).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        // If there's a conflict between two non-HDR mods, ask which one to keep.
        for (var i = 0; i < selected.Count; i++)
        {
            if (selected[i].Remediation is not ModConflictRemediation { Choices.Count: > 0 } conflict)
            {
                continue;
            }

            var keep = await _dialogs.PickConflictKeepAsync(selected[i].Title, conflict);

            if (keep is null)
            {
                // Backing out of one conflict drops that fix.
                selected.RemoveAt(i--);
                continue;
            }

            selected[i] = selected[i] with { Remediation = conflict.With(keep) };
        }

        if (selected.Count == 0)
        {
            return;
        }

        var destructive = selected.Any(f => f.Remediation!.IsDestructive);

        var confirmed = await _dialogs.ConfirmAsync(
            destructive ? "Apply fixes — this deletes files" : "Apply fixes",
            RemediationService.DescribePlan(selected),
            $"Apply {selected.Count} fix{(selected.Count == 1 ? "" : "es")}",
            destructive);

        if (!confirmed)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await using var source = new LocalDirectorySource(SelectedProfile.Profile.Path!);

            var outcomes = await RemediationService.ApplyAsync(
                source,
                selected,
                NewProgress(),
                CancellationToken.None);

            var failed = outcomes.Where(o => !o.Succeeded).ToList();

            if (failed.Count > 0)
            {
                await _dialogs.ShowMessageAsync(
                    "Some fixes could not be applied",
                    string.Join("\n\n", failed.Select(f => $"{f.Finding.Title}\n{f.Error}")));
            }
        }
        finally
        {
            IsBusy = false;
        }

        await ScanAsync();
    }


    // ---- Emulator settings --------------------------------------------------

    /// <summary>
    /// Writes optimal settings for the emulator.
    /// </summary>
    /// <remarks>
    /// Not in Apply Fixes because it's technically not a fix.
    /// </remarks>
    [RelayCommand]
    private async Task ApplyEmulatorSettingsAsync()
    {
        if (SelectedProfile?.Profile.Emulator is not { } emulator)
        {
            return;
        }

        if (!await StopEmulatorAsync(emulator))
        {
            return;
        }

        IsBusy = true;

        try
        {
            var plan = await EmulatorSettingsWriter.PlanAsync(emulator, CancellationToken.None);

            if (plan.IsEmpty)
            {
                await _dialogs.ShowMessageAsync(
                    "Nothing to change",
                    $"Every setting is already correct in {emulator.ProductName}.");

                return;
            }

            var chosen = await _dialogs.ConfirmSettingsAsync(emulator.ProductName, plan);

            if (chosen is null || chosen.Count == 0)
            {
                return;
            }

            await EmulatorSettingsWriter.ApplyAsync(
                emulator,
                plan with { Changes = chosen },
                NewProgress(),
                CancellationToken.None);

            Status = $"Wrote {chosen.Count} setting{(chosen.Count == 1 ? "" : "s")} to {emulator.ProductName}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            await _dialogs.ShowMessageAsync("The settings could not be written", e.Message);
        }
        finally
        {
            IsBusy = false;
        }

        if (HasScanned)
        {
            await ScanAsync();
        }
    }

    /// <summary>
    /// Stops every instance of the emulator.
    /// False when the user declined or something is still running.
    /// </summary>
    /// <remarks>
    /// Kills *all* instances so that nothing can block or overwrite
    /// the settings being changed.
    /// </remarks>
    private async Task<bool> StopEmulatorAsync(EmulatorInstallation emulator)
    {
        var running = EmulatorProcessGuard.Find(emulator);

        if (running.Count == 0)
        {
            return true;
        }

        var listed = string.Join("\n", running.Select(r => $"    {r.Name} (pid {r.Pid})"));

        var confirmed = await _dialogs.ConfirmAsync(
            $"{emulator.ProductName} has to close first",
            (running.Count == 1
                ? $"{emulator.ProductName} is running:"
                : $"{running.Count} {emulator.ProductName} processes are running:")
            + $"\n\n{listed}\n\n"
            + "It rewrites its settings when it exits, so anything written while it is open would be "
            + "overwritten as soon as it closes.",
            "Close it and continue",
            destructive: false);

        if (!confirmed)
        {
            return false;
        }

        IsBusy = true;

        try
        {
            Status = $"Waiting for {emulator.ProductName} to close…";

            var left = await EmulatorProcessGuard.CloseAsync(
                running, TimeSpan.FromSeconds(10), CancellationToken.None);

            if (left.Count == 0)
            {
                return true;
            }

            var forced = await _dialogs.ConfirmAsync(
                "Force it to close?",
                string.Join("\n", left.Select(r => $"    {r.Name} (pid {r.Pid})"))
                + "\n\ndid not respond. This usually means the window is already gone and the process is "
                + "stuck. Forcing it to close is usually safe, though it may lose any unsaved progress.",
                "Force close",
                destructive: true);

            if (!forced)
            {
                return false;
            }

            left = await EmulatorProcessGuard.KillAsync(left, TimeSpan.FromSeconds(5), CancellationToken.None);

            if (left.Count == 0)
            {
                return true;
            }

            await _dialogs.ShowMessageAsync(
                "It is still running",
                string.Join("\n", left.Select(r => $"    {r.Name} (pid {r.Pid})"))
                + "\n\ncould not be stopped. Close it from your system's task manager and try again.");

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Fresh install ------------------------------------------------------

    [RelayCommand]
    private async Task FreshInstallAsync()
    {
        if (SelectedProfile is null || IsReadOnlySource)
        {
            return;
        }

        var profile = SelectedProfile.Profile;
        IsBusy = true;

        try
        {
            await using var source = new LocalDirectorySource(profile.Path!);

            var preview = await FreshInstallService.PreviewAsync(source, CancellationToken.None);

            var summary = preview.Count == 0
                ? "No existing HDR folders were found, so nothing will be deleted."
                : string.Join("\n", preview.Select(p =>
                    $"  {p.Folder} — {p.FileCount:N0} files, {Formatting.FormatBytes(p.TotalBytes)}"));

            var channel = Environment?.Channel is ReleaseChannel.PreRelease
                ? ReleaseChannel.PreRelease
                : ReleaseChannel.Beta;

            var confirmed = await _dialogs.ConfirmAsync(
                "Reinstall HDR from scratch",
                "This deletes HDR's own mod folders and installs the latest release in their place:\n\n"
                + summary
                + "\n\nYour other mods, your plugins folder and Skyline itself are left alone.\n\n"
                + $"The {channel} release will be downloaded — this needs an internet connection and may take "
                + "a while.\n\nThis cannot be undone.",
                "Delete and reinstall",
                destructive: true);

            if (!confirmed)
            {
                return;
            }

            await _services.FreshInstall.RunAsync(source, channel, NewProgress(), CancellationToken.None);
        }
        catch (Exception e)
        {
            await _dialogs.ShowMessageAsync("Reinstall failed", e.Message);
            return;
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = true;
        }

        await ScanAsync();
    }

    // ---- Report -------------------------------------------------------------

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        if (_lastResult is null)
        {
            return;
        }

        var suggested = $"hdr-report-{DateTime.Now:yyyy-MM-dd-HHmm}.txt";
        var path = await _dialogs.SaveFileAsync("Save report", suggested);

        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, ReportWriter.Write(_lastResult, ShowPassingChecks));
            Status = $"Report saved to {path}";
        }
        catch (Exception e)
        {
            await _dialogs.ShowMessageAsync("Could not save the report", e.Message);
        }
    }

    [RelayCommand]
    private async Task PickLogAsync()
    {
        var path = await _dialogs.PickFileAsync("Select a Skyline or emulator log", ["log", "txt"]);
        if (path is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var findings = await SkylineLogParser.ParseAsync(path, CancellationToken.None);

            var group = new FindingGroupViewModel(
                CheckCategory.SkylineLog,
                findings.Where(f => ShowPassingChecks || f.Severity != Severity.Ok)
                    .Select(f => new FindingViewModel(f)));

            // Replace whatever the automatic log search found with
            // the user-picked log, if selected.
            var existing = Groups.FirstOrDefault(g => g.Category == CheckCategory.SkylineLog);
            if (existing is not null)
            {
                Groups[Groups.IndexOf(existing)] = group;
            }
            else
            {
                Groups.Add(group);
            }

            HasScanned = true;
            Status = $"Read {Path.GetFileName(path)}.";
            OnPropertyChanged(nameof(HasResults));
        }
        catch (Exception e)
        {
            await _dialogs.ShowMessageAsync("Could not read that log", e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens a path in the system file manager, selecting it where possible.
    /// </summary>
    [RelayCommand]
    private void Reveal(string? relativePath)
    {
        if (relativePath is null || SelectedProfile?.Profile.Path is null)
        {
            return;
        }

        // Findings carry relative paths, but a few (config files, logs) are
        // already absolute.
        var absolute = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(SelectedProfile.Profile.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));

        var target = File.Exists(absolute) || Directory.Exists(absolute)
            ? absolute
            : Path.GetDirectoryName(absolute);

        if (target is null)
        {
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\""));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open", ["-R", target]));
            }
            else
            {
                var folder = Directory.Exists(target) ? target : Path.GetDirectoryName(target);
                if (folder is { Length: > 0 })
                {
                    Process.Start(new ProcessStartInfo("xdg-open", [folder]));
                }
            }
        }
        catch (Exception)
        {
        }
    }

    // ---- Updating HDR Doctor -------------------------------------------------

    /// <summary>Where <see cref="RestartForUpdate"/> should relaunch from.</summary>
    private string? _restartTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateHeadline))]
    [NotifyPropertyChangedFor(nameof(UpdateDetail))]
    public partial AppUpdate? AvailableUpdate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateHeadline))]
    [NotifyPropertyChangedFor(nameof(UpdateDetail))]
    public partial bool UpdateInstalled { get; set; }

    public bool HasUpdate => AvailableUpdate is not null;

    public string UpdateHeadline => UpdateInstalled
        ? $"HDR Doctor {AvailableUpdate?.Version.ToString(3)} is ready"
        : $"HDR Doctor {AvailableUpdate?.Version.ToString(3)} is available";

    public string UpdateDetail => UpdateInstalled
        ? "Restart to start using it."
        : $"You are running {UpdateService.CurrentVersion.ToString(3)}. A newer HDR Doctor version is available.";

    /// <summary>
    /// Asks GitHub whether a newer HDR Doctor exists in the background.
    /// </summary>
    /// <remarks>
    public async Task CheckForUpdatesAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            AvailableUpdate = await new UpdateService(_services.Http).CheckAsync(deadline.Token);
        }
        catch (Exception)
        {
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (AvailableUpdate is not { } update || IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = true;

        try
        {
            _restartTarget = await new UpdateService(_services.Http)
                .ApplyAsync(update, NewProgress(), CancellationToken.None);

            UpdateInstalled = true;
            Status = $"HDR Doctor {update.Version.ToString(3)} installed. Restart to use it.";
            StatusDetail = null;
        }
        catch (Exception e)
        {
            Status = "The update could not be installed.";
            StatusDetail = e.Message;
            await _dialogs.ShowMessageAsync("Update failed", e.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = true;
        }
    }

    [RelayCommand]
    private void RestartForUpdate()
    {
        if (_restartTarget is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_restartTarget) { UseShellExecute = false });
        }
        catch (Exception)
        {
            return;
        }

        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// <summary>Hides the banner for this run. The next launch asks again.</summary>
    [RelayCommand]
    private void DismissUpdate() => AvailableUpdate = null;
}
