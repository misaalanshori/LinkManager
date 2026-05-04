using LinkManager.Configuration;

namespace LinkManager.Hooks;

/// <summary>
/// Represents an executable action triggered after a network switch.
/// </summary>
public interface IHook
{
    /// <summary>Human-readable name for logging.</summary>
    string Name { get; }

    /// <summary>Executes the hook action asynchronously.</summary>
    Task ExecuteAsync();
}
