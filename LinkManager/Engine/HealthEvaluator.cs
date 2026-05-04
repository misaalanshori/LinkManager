using LinkManager.Models;

namespace LinkManager.Engine;

/// <summary>
/// Applies hysteresis to adapter health state.
/// An adapter must fail <see cref="FailThreshold"/> consecutive probes to be marked unhealthy,
/// and must pass <see cref="RestoreThreshold"/> consecutive probes to be marked healthy again.
/// This prevents route flapping from intermittent connectivity.
/// </summary>
public sealed class HealthEvaluator
{
    public int FailThreshold { get; private set; }
    public int RestoreThreshold { get; private set; }

    public HealthEvaluator(int failThreshold, int restoreThreshold)
    {
        FailThreshold = failThreshold;
        RestoreThreshold = restoreThreshold;
    }

    /// <summary>
    /// Updates the adapter's health tracking based on the latest probe result.
    /// </summary>
    /// <returns>True if <see cref="AdapterState.IsHealthy"/> changed this call.</returns>
    public bool Evaluate(AdapterState adapter, bool probeSuccess)
    {
        // Adapter is physically absent — force unhealthy immediately, no hysteresis
        if (!adapter.IsPresent)
        {
            var wasHealthy = adapter.IsHealthy;
            adapter.IsHealthy = false;
            adapter.ResetCounters();
            return wasHealthy; // changed if it was healthy before
        }

        if (probeSuccess)
        {
            adapter.ConsecutiveSuccesses++;
            adapter.ConsecutiveFailures = 0;

            if (!adapter.IsHealthy && adapter.ConsecutiveSuccesses >= RestoreThreshold)
            {
                adapter.IsHealthy = true;
                return true; // CHANGED: unhealthy → healthy
            }
        }
        else
        {
            adapter.ConsecutiveFailures++;
            adapter.ConsecutiveSuccesses = 0;

            if (adapter.IsHealthy && adapter.ConsecutiveFailures >= FailThreshold)
            {
                adapter.IsHealthy = false;
                return true; // CHANGED: healthy → unhealthy
            }
        }

        return false; // no change
    }

    /// <summary>
    /// Updates thresholds — called when config is reloaded.
    /// </summary>
    public void UpdateThresholds(int failThreshold, int restoreThreshold)
    {
        FailThreshold = failThreshold;
        RestoreThreshold = restoreThreshold;
    }
}
