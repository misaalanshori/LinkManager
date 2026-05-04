using LinkManager.Hooks;
using LinkManager.Models;
using LinkManager.Network;

namespace LinkManager.Engine;

/// <summary>
/// Executes the full switch sequence when the active priority group changes:
/// update metrics → mark active flags → run hooks → hold-down cooldown.
/// </summary>
public sealed class SwitchProtocol
{
    /// <summary>Fired at the start of a switch, before metrics change. UI goes "Switching..."</summary>
    public event Action<IReadOnlyList<AdapterState>>? OnSwitchStarted;

    /// <summary>Fired after cooldown completes. UI returns to stable state.</summary>
    public event Action<IReadOnlyList<AdapterState>>? OnSwitchCompleted;

    private readonly Action<string> _log;

    public SwitchProtocol(Action<string>? log = null)
    {
        _log = log ?? Console.WriteLine;
    }

    /// <summary>
    /// Performs the group switch:
    /// 1. Announce switch to UI
    /// 2. Update all interface metrics
    /// 3. Update IsActive flags
    /// 4. Run hooks
    /// 5. Hold-down cooldown
    /// </summary>
    /// <param name="targetGroup">All healthy adapters in the new active priority group.</param>
    /// <param name="allAdapters">Every adapter (for setting fallback metrics).</param>
    /// <param name="hookRunner">Hooks to fire after metrics are set.</param>
    /// <param name="cooldownMs">Hold-down timer duration.</param>
    public async Task Execute(
        IReadOnlyList<AdapterState> targetGroup,
        IReadOnlyList<AdapterState> allAdapters,
        HookRunner hookRunner,
        int cooldownMs)
    {
        var targetPriority = targetGroup.First().Priority;
        _log($"[Switch] Switching to priority group {targetPriority} " +
             $"({string.Join(", ", targetGroup.Select(a => a.Identifier))})");

        // Step 1: Notify UI — "Switching..."
        OnSwitchStarted?.Invoke(targetGroup);

        // Step 2: Set metrics
        // Active group → metric 10 (all healthy members get same metric → Windows load-balances)
        // All other present adapters → fallback metric based on priority order
        var metricTasks = allAdapters
            .Where(a => a.InterfaceIndex >= 0)
            .Select(async adapter =>
            {
                bool isInTargetGroup = adapter.Priority == targetPriority && adapter.IsHealthy;
                int metric = isInTargetGroup
                    ? 10
                    : MetricManager.ComputeFallbackMetric(adapter.Priority);

                try
                {
                    await MetricManager.SetMetricDual(adapter.InterfaceIndex, metric);
                    _log($"[Switch] {adapter.Identifier} → metric {metric}");
                }
                catch (Exception ex)
                {
                    _log($"[Switch] Failed to set metric for {adapter.Identifier}: {ex.Message}");
                }
            });

        await Task.WhenAll(metricTasks);

        // Step 3: Update IsActive flags
        foreach (var adapter in allAdapters)
        {
            adapter.IsActive = adapter.Priority == targetPriority && adapter.IsHealthy;
        }

        // Step 4: Run hooks
        await hookRunner.RunAll();

        // Step 5: Hold-down cooldown — prevents rapid re-switching while OS stabilizes
        _log($"[Switch] Holding down for {cooldownMs}ms...");
        await Task.Delay(cooldownMs);

        _log($"[Switch] Cooldown complete. Active group: {targetPriority}");
        OnSwitchCompleted?.Invoke(targetGroup);
    }
}
