using System.Diagnostics;

namespace LinkManager.Helpers;

/// <summary>
/// Manages the Windows Task Scheduler entry that launches LinkManager at logon
/// with elevated privileges (required for netsh metric changes).
/// </summary>
public static class AutoStartManager
{
    private const string TaskName = "LinkManager";

    /// <summary>
    /// Registers a Task Scheduler task that runs LinkManager at logon
    /// with highest privileges. Overwrites any existing task with the same name.
    /// </summary>
    public static async Task Register()
    {
        var exePath = Environment.ProcessPath
            ?? System.Reflection.Assembly.GetExecutingAssembly().Location;

        // schtasks /create - create or replace the task
        await RunSchtasks(
            $"/create /tn \"{TaskName}\" /tr \"{exePath}\" " +
            $"/sc onlogon /rl highest /f");
    }

    /// <summary>
    /// Removes the Task Scheduler entry.
    /// </summary>
    public static async Task Unregister()
    {
        await RunSchtasks($"/delete /tn \"{TaskName}\" /f");
    }

    /// <summary>
    /// Returns true if the Task Scheduler task currently exists.
    /// </summary>
    public static bool IsRegistered()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = $"/query /tn \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit();
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static async Task RunSchtasks(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start schtasks.exe");

        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"schtasks failed (exit {proc.ExitCode}): {err.Trim()}");
        }
    }
}
