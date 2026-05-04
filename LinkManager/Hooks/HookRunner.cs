using LinkManager.Configuration;

namespace LinkManager.Hooks;

/// <summary>
/// Builds and runs the list of configured hooks after each group switch.
/// Each hook is run sequentially; failures are caught and logged — never fatal.
/// </summary>
public sealed class HookRunner
{
    private readonly List<IHook> _hooks;
    private readonly Action<string> _log;

    public HookRunner(List<HookConfig> configs, Action<string>? log = null)
    {
        _log = log ?? Console.WriteLine;

        _hooks = configs.Select<HookConfig, IHook>(c => c.Type.ToLowerInvariant() switch
        {
            "process" => new ProcessHook(c.Path, c.Args, _log),
            _ => throw new InvalidOperationException($"Unknown hook type: '{c.Type}'")
        }).ToList();
    }

    /// <summary>
    /// Runs all hooks in order. Each hook failure is logged and skipped.
    /// </summary>
    public async Task RunAll()
    {
        if (_hooks.Count == 0) return;

        _log($"[Hooks] Running {_hooks.Count} hook(s)...");
        foreach (var hook in _hooks)
        {
            try
            {
                await hook.ExecuteAsync();
            }
            catch (Exception ex)
            {
                _log($"[Hooks] Hook '{hook.Name}' threw: {ex.Message}");
            }
        }
        _log("[Hooks] All hooks completed.");
    }

    /// <summary>Number of registered hooks.</summary>
    public int Count => _hooks.Count;
}
