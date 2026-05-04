using System.Diagnostics;

namespace LinkManager.Hooks;

/// <summary>
/// A hook that spawns an external process (e.g., powershell.exe, a .bat script).
/// Captures stdout/stderr and logs exit code.
/// </summary>
public sealed class ProcessHook : IHook
{
    private readonly string _path;
    private readonly string _args;
    private readonly Action<string> _log;

    public string Name => $"Process: {_path} {_args}".TrimEnd();

    public ProcessHook(string path, string args, Action<string>? log = null)
    {
        _path = path;
        _args = args;
        _log = log ?? Console.WriteLine;
    }

    public async Task ExecuteAsync()
    {
        _log($"[Hook] Running: {Name}");
        try
        {
            var psi = new ProcessStartInfo(_path, _args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start process: {_path}");

            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();

            await proc.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);

            _log($"[Hook] Exit {proc.ExitCode}: {_path}");
            if (!string.IsNullOrWhiteSpace(stderr.Result))
                _log($"[Hook] STDERR: {stderr.Result.Trim()}");
        }
        catch (Exception ex)
        {
            _log($"[Hook] ERROR running {Name}: {ex.Message}");
        }
    }
}
