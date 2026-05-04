namespace LinkManager.Configuration;

/// <summary>
/// Represents one adapter entry in the config.json "adapters" list.
/// </summary>
public sealed class AdapterProfileConfig
{
    /// <summary>
    /// Identifies the adapter. Can be:
    /// - An interface Name (e.g., "Wi-Fi", "vEthernet (VMSwitch)")
    /// - A hardware Description substring (e.g., "Remote NDIS Compatible Device")
    /// - A static IP address (e.g., "192.168.88.200")
    /// </summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// Priority group number. Lower = higher priority.
    /// Adapters sharing the same number form a load-balance group.
    /// Adapters with different numbers form a strict failover chain.
    /// Examples:
    ///   priority 0, 0 → both active at same time (load-balanced)
    ///   priority 0, 1  → strict primary/backup
    /// </summary>
    public int Priority { get; set; } = 0;
}
