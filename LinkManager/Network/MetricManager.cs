using System.Diagnostics;

namespace LinkManager.Network;

/// <summary>
/// Manages Windows interface metrics via netsh.
/// Requires the process to be running as Administrator.
/// </summary>
public static class MetricManager
{
    /// <summary>
    /// Sets the IPv4 metric for the given interface index.
    /// Lower metric = higher routing priority.
    /// </summary>
    public static async Task SetMetricIpv4(int interfaceIndex, int metric)
        => await RunNetsh($"interface ipv4 set interface {interfaceIndex} metric={metric}");

    /// <summary>
    /// Sets the IPv6 metric for the given interface index.
    /// </summary>
    public static async Task SetMetricIpv6(int interfaceIndex, int metric)
        => await RunNetsh($"interface ipv6 set interface {interfaceIndex} metric={metric}");

    /// <summary>
    /// Sets both IPv4 and IPv6 metrics for the given interface index.
    /// </summary>
    public static async Task SetMetricDual(int interfaceIndex, int metric)
    {
        await SetMetricIpv4(interfaceIndex, metric);
        await SetMetricIpv6(interfaceIndex, metric);
    }

    /// <summary>
    /// Resets the interface metric back to Windows automatic (OSPF-calculated).
    /// Called when all adapters are dead so the system isn't left in a broken state.
    /// </summary>
    public static async Task ResetToAutomatic(int interfaceIndex)
        => await RunNetsh($"interface ipv4 set interface {interfaceIndex} metric=automatic");

    /// <summary>
    /// Computes the fallback metric for a given priority group.
    /// Active group (priority 0) gets 10. Priority 1 → 110, priority 2 → 120, etc.
    /// This gives Windows a native fallback order even between our polling cycles.
    /// </summary>
    public static int ComputeFallbackMetric(int priority)
        => 100 + (priority * 10);

    // ── Private ───────────────────────────────────────────────────────────────

    private static async Task RunNetsh(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start netsh");

            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync();
                throw new InvalidOperationException(
                    $"netsh failed (exit {proc.ExitCode}): {err.Trim()}");
            }
        }
        catch (Exception ex)
        {
            // Surface as console error — caller logs it
            Console.Error.WriteLine($"[MetricManager] netsh error: {ex.Message}");
            throw;
        }
    }
}
