namespace LinkManager.Models;

/// <summary>
/// Runtime state for a single adapter. Created from config at startup
/// and refreshed every poll tick. Never cached across ticks.
/// </summary>
public sealed class AdapterState
{
    // ── From config ──────────────────────────────────────────────────────────

    /// <summary>
    /// The identifier string from config (IP, Name, or Description).
    /// </summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// Priority group. Lower = higher priority. Same = load-balanced group.
    /// </summary>
    public int Priority { get; set; }

    // ── Resolved at runtime each tick ────────────────────────────────────────

    /// <summary>
    /// The current IPv4 address assigned to this adapter, or null if absent/down.
    /// </summary>
    public string? CurrentIp { get; set; }

    /// <summary>
    /// The actual Windows interface name (e.g., "Ethernet 4"), resolved each tick.
    /// </summary>
    public string? InterfaceName { get; set; }

    /// <summary>
    /// The Windows interface index used for netsh commands. -1 if not found.
    /// </summary>
    public int InterfaceIndex { get; set; } = -1;

    /// <summary>
    /// True if the adapter currently exists in the OS and has an IPv4 address.
    /// False if unplugged, disabled, or still negotiating DHCP.
    /// </summary>
    public bool IsPresent { get; set; }

    // ── Health tracking (hysteresis) ─────────────────────────────────────────

    /// <summary>
    /// Whether this adapter is currently considered to have internet connectivity.
    /// Only changes after hitting FailThreshold or RestoreThreshold.
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Number of consecutive successful probes since last failure.
    /// </summary>
    public int ConsecutiveSuccesses { get; set; }

    /// <summary>
    /// Number of consecutive failed probes since last success.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    // ── Display ──────────────────────────────────────────────────────────────

    /// <summary>
    /// True if this adapter is currently the active route for internet traffic.
    /// For load-balanced groups, multiple adapters can be active simultaneously.
    /// </summary>
    public bool IsActive { get; set; }

    // ── Methods ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resets hysteresis counters. Called on config reload and ForceReevaluate.
    /// </summary>
    public void ResetCounters()
    {
        ConsecutiveSuccesses = 0;
        ConsecutiveFailures = 0;
    }

    public override string ToString() =>
        $"[P{Priority}] {Identifier} | IP={CurrentIp ?? "none"} | " +
        $"Present={IsPresent} | Healthy={IsHealthy} | Active={IsActive}";
}
