namespace LinkManager.Configuration;

/// <summary>
/// Represents a hook entry in config.json.
/// Hooks are actions triggered automatically after a network switch.
/// </summary>
public sealed class HookConfig
{
    /// <summary>
    /// Hook type. Currently only "process" is supported.
    /// Future: "webhook", "powershell"
    /// </summary>
    public string Type { get; set; } = "process";

    /// <summary>
    /// Path to the executable to run (e.g., "powershell.exe").
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Arguments to pass to the executable.
    /// </summary>
    public string Args { get; set; } = string.Empty;
}
