using LinkManager.Configuration;
using LinkManager.Hooks;
using LinkManager.Models;
using LinkManager.Network;

namespace LinkManager.Engine;

/// <summary>
/// Core state machine. Runs a background loop that:
/// 1. Resolves adapters from the OS
/// 2. Probes connectivity (ICMP + HTTP) through each adapter's source IP
/// 3. Applies hysteresis to determine health
/// 4. Switches the active priority group when needed
/// 5. Resets metrics to defaults when all adapters are dead
/// </summary>
public sealed class LinkEngine : IDisposable
{
    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired whenever any adapter state changes (health, presence, active).</summary>
    public event Action? OnStateChanged;

    /// <summary>Fired when a group switch begins. Arg = new target group.</summary>
    public event Action<IReadOnlyList<AdapterState>>? OnSwitchStarted;

    /// <summary>Fired after cooldown when a group switch completes.</summary>
    public event Action<IReadOnlyList<AdapterState>>? OnSwitchCompleted;

    /// <summary>Log messages from the engine and all subsystems.</summary>
    public event Action<string>? OnLog;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>Live snapshot of all adapter states.</summary>
    public IReadOnlyList<AdapterState> Adapters => _adapters;

    /// <summary>
    /// Priority number of the currently active group, or -1 if all adapters are dead.
    /// </summary>
    public int ActiveGroupPriority { get; private set; } = -1;

    /// <summary>True while the hold-down cooldown is in progress after a switch.</summary>
    public bool IsCoolingDown { get; private set; }

    /// <summary>
    /// Set to true to pause the polling loop (no metric changes, no probing).
    /// Set from the tray icon "Pause" menu item.
    /// </summary>
    public volatile bool IsPaused;

    // ── Private fields ────────────────────────────────────────────────────────

    private List<AdapterState> _adapters = new();
    private AppConfig _config;

    private ConnectivityProbe _probe = null!;       // set by RebuildFromConfig in ctor
    private HealthEvaluator _healthEvaluator = null!; // set by RebuildFromConfig in ctor
    private SwitchProtocol _switchProtocol = null!;   // set by RebuildFromConfig in ctor
    private HookRunner _hookRunner = null!;            // set by RebuildFromConfig in ctor

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    // Signal ForceReevaluate to the loop
    private volatile bool _forceReevaluate;

    // ── Constructor ───────────────────────────────────────────────────────────

    public LinkEngine(AppConfig config)
    {
        _config = config;
        RebuildFromConfig(config);
    }

    // ── Control ───────────────────────────────────────────────────────────────

    /// <summary>Starts the background polling loop.</summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoop(_cts.Token));
        Log("[Engine] Started.");
    }

    /// <summary>Gracefully stops the polling loop.</summary>
    public void Stop()
    {
        Log("[Engine] Stopping...");
        _cts?.Cancel();
        _loopTask?.Wait(TimeSpan.FromSeconds(5));
        Log("[Engine] Stopped.");
    }

    /// <summary>
    /// Resets all hysteresis counters and triggers an immediate probe on the next tick.
    /// </summary>
    public void ForceReevaluate()
    {
        Log("[Engine] Force re-evaluate requested.");
        foreach (var a in _adapters) a.ResetCounters();
        _forceReevaluate = true;
    }

    /// <summary>
    /// Applies a new config (e.g., after hot-reload from FileSystemWatcher).
    /// Restarts the loop with fresh state.
    /// </summary>
    public void ReloadConfig(AppConfig newConfig)
    {
        Log("[Engine] Config reloaded — rebuilding.");
        _cts?.Cancel();
        _loopTask?.Wait(TimeSpan.FromSeconds(3));

        _config = newConfig;
        RebuildFromConfig(newConfig);

        if (_cts != null) // was running
            Start();
    }

    // ── Main loop ─────────────────────────────────────────────────────────────

    private async Task RunLoop(CancellationToken ct)
    {
        bool isFirstIteration = true;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (IsPaused)
                {
                    await Task.Delay(_config.PollIntervalMs, ct);
                    continue;
                }

                // ── Step 1: Resolve all adapters from live OS state ──────────
                foreach (var adapter in _adapters)
                {
                    adapter.CurrentIp = AdapterResolver.ResolveIp(adapter.Identifier);
                    adapter.IsPresent = adapter.CurrentIp != null;
                    adapter.InterfaceIndex = AdapterResolver.ResolveInterfaceIndex(adapter.Identifier);
                    adapter.InterfaceName = AdapterResolver.ResolveInterface(adapter.Identifier)?.Name;
                }

                // ── Step 2: Probe present adapters in parallel ───────────────
                var presentAdapters = _adapters.Where(a => a.IsPresent).ToList();
                var probeTasks = presentAdapters.Select(async adapter =>
                {
                    bool result = await _probe.CheckConnectivity(adapter.CurrentIp!);
                    return (adapter, result);
                });
                var results = await Task.WhenAll(probeTasks);

                // ── Step 3: Apply hysteresis ─────────────────────────────────
                // On bootstrap (first iteration), use threshold=1 so we classify immediately
                bool bootstrap = isFirstIteration;
                isFirstIteration = false;

                if (bootstrap)
                {
                    // Temporarily lower thresholds for initial classification
                    _healthEvaluator.UpdateThresholds(1, 1);
                }

                bool anyChanged = false;
                foreach (var (adapter, success) in results)
                    anyChanged |= _healthEvaluator.Evaluate(adapter, success);

                foreach (var absent in _adapters.Where(a => !a.IsPresent))
                    anyChanged |= _healthEvaluator.Evaluate(absent, false);

                if (bootstrap)
                {
                    // Restore configured thresholds after first pass
                    _healthEvaluator.UpdateThresholds(_config.FailThreshold, _config.RestoreThreshold);
                    anyChanged = true; // always update metrics on bootstrap
                }

                // ── Step 4: Determine best group ─────────────────────────────
                // Group adapters by priority, find lowest-priority group with ≥1 healthy member
                var bestGroup = _adapters
                    .Where(a => a.IsHealthy)
                    .GroupBy(a => a.Priority)
                    .OrderBy(g => g.Key)
                    .FirstOrDefault();

                // ── Step 5: Handle all-dead state ────────────────────────────
                if (bestGroup == null)
                {
                    if (ActiveGroupPriority != -1 || bootstrap)
                    {
                        Log("[Engine] ALL adapters dead — resetting metrics to automatic.");
                        ActiveGroupPriority = -1;
                        foreach (var a in _adapters)
                        {
                            a.IsActive = false;
                            if (a.InterfaceIndex >= 0)
                            {
                                try { await MetricManager.ResetToAutomatic(a.InterfaceIndex); }
                                catch (Exception ex) { Log($"[Engine] Reset failed: {ex.Message}"); }
                            }
                        }
                        anyChanged = true;
                    }
                }
                // ── Step 6: Switch if group changed ──────────────────────────
                else if (bestGroup.Key != ActiveGroupPriority)
                {
                    int newPriority = bestGroup.Key;
                    Log($"[Engine] Group change: {ActiveGroupPriority} → {newPriority}");
                    ActiveGroupPriority = newPriority;

                    IsCoolingDown = true;
                    OnStateChanged?.Invoke();

                    await _switchProtocol.Execute(
                        bestGroup.ToList(),
                        _adapters,
                        _hookRunner,
                        _config.SwitchCooldownMs);

                    IsCoolingDown = false;
                    anyChanged = true;
                }
                // ── Step 7: Within-group member change (load-balance refresh) ─
                else if (anyChanged)
                {
                    // Same group, but membership may have changed (e.g., one member died/recovered)
                    // Re-apply metrics without firing a full switch
                    await RefreshGroupMetrics(bestGroup.ToList());
                }

                if (anyChanged)
                    OnStateChanged?.Invoke();

                // ── Wait for next tick (or skip delay if ForceReevaluate) ────
                if (_forceReevaluate)
                {
                    _forceReevaluate = false;
                    Log("[Engine] Immediate re-poll (force).");
                    continue;
                }

                await Task.Delay(_config.PollIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"[Engine] Unhandled error in loop: {ex.Message}");
                // Don't crash — wait one interval and try again
                try { await Task.Delay(_config.PollIntervalMs, ct); } catch { break; }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-applies metrics for the current active group when membership changes
    /// (e.g., one adapter in a load-balanced group fails) without triggering a full switch.
    /// </summary>
    private async Task RefreshGroupMetrics(List<AdapterState> currentGroupHealthy)
    {
        int activePriority = ActiveGroupPriority;
        var tasks = _adapters
            .Where(a => a.InterfaceIndex >= 0)
            .Select(async adapter =>
            {
                bool isActive = adapter.Priority == activePriority && adapter.IsHealthy;
                adapter.IsActive = isActive;
                int metric = isActive ? 10 : MetricManager.ComputeFallbackMetric(adapter.Priority);
                try { await MetricManager.SetMetricDual(adapter.InterfaceIndex, metric); }
                catch (Exception ex) { Log($"[Engine] Metric refresh failed for {adapter.Identifier}: {ex.Message}"); }
            });
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Rebuilds all subsystems from a fresh config. Called on startup and config reload.
    /// </summary>
    private void RebuildFromConfig(AppConfig config)
    {
        _adapters = config.Adapters.Select(c => new AdapterState
        {
            Identifier = c.Identifier,
            Priority = c.Priority
        }).ToList();

        _probe = new ConnectivityProbe(
            config.TestEndpoints,
            config.HttpTestEndpoints,
            config.ProbeTimeoutMs,
            Log);

        _healthEvaluator = new HealthEvaluator(config.FailThreshold, config.RestoreThreshold);
        _hookRunner = new HookRunner(config.Hooks, Log);

        _switchProtocol = new SwitchProtocol(Log);
        _switchProtocol.OnSwitchStarted += group => OnSwitchStarted?.Invoke(group);
        _switchProtocol.OnSwitchCompleted += group => OnSwitchCompleted?.Invoke(group);

        ActiveGroupPriority = -1;
        _forceReevaluate = false;
    }

    private void Log(string message) => OnLog?.Invoke(message);

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
