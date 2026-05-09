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
    /// HTTP GET targets used as fallback if ALL ICMP pings fail (or ICMP is disabled).
    /// Each URL has an exact content validator in ConnectivityProbe — any HTTP response that
    /// doesn't match the expected body/status is treated as a captive portal and rejected.
    /// At least ProbeQuorum of these must pass for an adapter to be considered healthy.
    /// </summary>
    public List<string> HttpTestEndpoints { get; set; } = new()
    {
        // Microsoft NCSI (modern + legacy)
        "http://www.msftconnecttest.com/connecttest.txt",
        "https://www.msftncsi.com/ncsi.txt",
        // Google (desktop + Android + static CDN — three independent PoPs)
        "https://www.google.com/generate_204",
        "http://connectivitycheck.gstatic.com/generate_204",
        "http://www.gstatic.com/generate_204",
        // Cloudflare
        "https://cp.cloudflare.com",
        // Mozilla Firefox NCSI
        "http://detectportal.firefox.com/success.txt",
        // GNOME NetworkManager
        "http://nmcheck.gnome.org/check_network_status.txt",
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

    /// <summary>
    /// Minimum number of HTTP endpoints that must pass content validation
    /// for an adapter to be considered healthy.
    /// Auto-clamped to [1, HttpTestEndpoints.Count] at runtime.
    /// Default 2: requires a majority of the default 3 endpoints to agree.
    /// </summary>
    public int ProbeQuorum { get; set; } = 2;

    /// <summary>
    /// When true, ICMP ping is run before HTTP probes as a fast-failure path.
    /// ICMP results can only accelerate failure detection — they cannot declare
    /// an adapter healthy on their own, preventing ISP DNS-proxy false positives.
    /// Set false to skip ICMP entirely (fewer ping.exe processes, slightly slower failure detection).
    /// </summary>
    public bool EnableIcmpProbe { get; set; } = true;

    /// <summary>Hooks to run after every group switch event.</summary>
    public List<HookConfig> Hooks { get; set; } = new();

    /// <summary>Whether to show Windows toast notifications on group switches.</summary>
    public bool EnableNotifications { get; set; } = true;

    /// <summary>Whether to register the app in Windows Task Scheduler at logon.</summary>
    public bool StartWithWindows { get; set; } = true;
}
