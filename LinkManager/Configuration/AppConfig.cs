namespace LinkManager.Configuration;

/// <summary>
/// Root configuration object, deserialized from config.json.
/// All timing values are in milliseconds.
/// </summary>
public sealed class AppConfig
{
    /// <summary>
    /// Ordered list of adapter profiles. Priority groups: same number = load-balanced.
    /// </summary>
    public List<AdapterProfileConfig> Adapters { get; set; } = new();

    /// <summary>
    /// ICMP ping targets. An adapter is considered reachable if ANY endpoint responds.
    /// Only marked dead if ALL endpoints fail.
    /// </summary>
    public List<string> TestEndpoints { get; set; } = new()
    {
        "8.8.8.8", "1.1.1.1", "9.9.9.9"
    };

    /// <summary>
    /// HTTP HEAD targets used as fallback if ALL ICMP pings fail.
    /// Handles environments where ICMP is blocked by firewall.
    /// </summary>
    public List<string> HttpTestEndpoints { get; set; } = new()
    {
        "http://www.msftconnecttest.com/connecttest.txt",
        "https://www.google.com/generate_204",
        "https://cp.cloudflare.com"
    };

    /// <summary>How often to run the probe cycle.</summary>
    public int PollIntervalMs { get; set; } = 5000;

    /// <summary>
    /// Hold-down timer after a group switch. Prevents rapid re-switching while
    /// the OS flushes DNS, TCP connections drop, and services like Tailscale restart.
    /// </summary>
    public int SwitchCooldownMs { get; set; } = 15000;

    /// <summary>
    /// Number of consecutive failed probes before an adapter is marked unhealthy.
    /// Lower = faster failure detection. Default 3 × 5s poll = 15s to declare dead.
    /// </summary>
    public int FailThreshold { get; set; } = 3;

    /// <summary>
    /// Number of consecutive successful probes before an adapter is restored to healthy.
    /// Higher = more proof of stability before switching back. Default 5 × 5s = 25s.
    /// Asymmetrically higher than FailThreshold to prevent route flapping.
    /// </summary>
    public int RestoreThreshold { get; set; } = 5;

    /// <summary>Timeout in ms for each individual ICMP or HTTP probe attempt.</summary>
    public int ProbeTimeoutMs { get; set; } = 2000;

    /// <summary>Hooks to run after every group switch event.</summary>
    public List<HookConfig> Hooks { get; set; } = new();

    /// <summary>Whether to show Windows toast notifications on group switches.</summary>
    public bool EnableNotifications { get; set; } = true;

    /// <summary>Whether to register the app in Windows Task Scheduler at logon.</summary>
    public bool StartWithWindows { get; set; } = true;
}
