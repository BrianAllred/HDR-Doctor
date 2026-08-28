using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HdrDoctor.Core.Services;

/// <param name="Pid">The process id.</param>
/// <param name="Name">The process name.</param>
public sealed record RunningEmulator(int Pid, string Name);

/// <summary>
/// Finds and stops emulator processes before their config is written.
/// </summary>
/// <remarks>
/// yuzu-family emulators rewrite <c>qt-config.ini</c> from memory when they exit, so
/// changing settings while one is running causes them to be overwritten as as soon as it closes.
///
/// Every matching process is stopped in order to avoid leaving any that might
/// overwrite settings when it does eventually close.
///
/// Two .NET behaviors this has to work around:
/// <see cref="Process.CloseMainWindow"/> returns false on Linux
/// (<see cref="Process.MainWindowHandle"/> is always zero), and
/// <see cref="Process.Kill()"/> sends SIGKILL rather than SIGTERM on *nix in general. 
/// So the polite request is <c>CloseMainWindow</c> on Windows and <c>kill(2)</c>
/// everywhere else first, followed by <c>Process.Kill</c> if necessary.
/// </remarks>
public static partial class EmulatorProcessGuard
{
    private const int Sigterm = 15;

    [LibraryImport("libc", SetLastError = true)]
    private static partial int kill(int pid, int sig);

    /// <summary>Every running process belonging to this emulator.</summary>
    public static IReadOnlyList<RunningEmulator> Find(EmulatorInstallation emulator)
    {
        var self = Environment.ProcessId;
        var found = new List<RunningEmulator>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id != self &&
                    process.ProcessName.StartsWith(emulator.ProductName, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(new RunningEmulator(process.Id, process.ProcessName));
                }
            }
            catch (InvalidOperationException)
            {
                // Exited by user or something else while listing processes.
            }
            finally
            {
                process.Dispose();
            }
        }

        return found;
    }

    /// <summary>
    /// Asks each process to close, then waits for it. Returns the ones still running.
    /// </summary>
    public static Task<IReadOnlyList<RunningEmulator>> CloseAsync(
        IEnumerable<RunningEmulator> emulators,
        TimeSpan timeout,
        CancellationToken ct) =>
        StopAsync(emulators, force: false, timeout, ct);

    /// <summary>
    /// Kills each process. Returns any that don't close.
    /// </summary>
    public static Task<IReadOnlyList<RunningEmulator>> KillAsync(
        IEnumerable<RunningEmulator> emulators,
        TimeSpan timeout,
        CancellationToken ct) =>
        StopAsync(emulators, force: true, timeout, ct);

    private static async Task<IReadOnlyList<RunningEmulator>> StopAsync(
        IEnumerable<RunningEmulator> emulators,
        bool force,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var targets = emulators.ToList();

        foreach (var target in targets)
        {
            Signal(target, force);
        }

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            targets = [.. targets.Where(IsRunning)];

            if (targets.Count == 0)
            {
                return targets;
            }

            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        return [.. targets.Where(IsRunning)];
    }

    private static void Signal(RunningEmulator target, bool force)
    {
        try
        {
            using var process = Process.GetProcessById(target.Pid);

            if (force)
            {
                process.Kill();
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                process.CloseMainWindow();
                return;
            }

            kill(target.Pid, Sigterm);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (InvalidOperationException)
        {
            // Exited while being closed.
        }
    }

    private static bool IsRunning(RunningEmulator target)
    {
        try
        {
            using var process = Process.GetProcessById(target.Pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
