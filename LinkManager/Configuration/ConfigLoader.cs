using System.Text.Json;

namespace LinkManager.Configuration;

/// <summary>
/// Handles loading, saving, and hot-reloading of config.json.
/// </summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static FileSystemWatcher? _watcher;

    /// <summary>
    /// Resolves the config.json path: next to the exe in production,
    /// or in the project root when running under the debugger.
    /// </summary>
    public static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "config.json");

    /// <summary>
    /// Loads config.json. Creates a default file if it doesn't exist.
    /// </summary>
    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaults = new AppConfig
            {
                Adapters = new List<AdapterProfileConfig>
                {
                    new() { Identifier = "vEthernet (VMSwitch)", Priority = 0 },
                    new() { Identifier = "Wi-Fi",                Priority = 1 },
                    new() { Identifier = "Remote NDIS Compatible Device", Priority = 2 }
                }
            };
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            // Log parse errors but don't crash — return defaults
            Console.Error.WriteLine($"[ConfigLoader] Failed to parse config.json: {ex.Message}");
            return new AppConfig();
        }
    }

    /// <summary>
    /// Saves an AppConfig back to config.json (used for persisting toggle states).
    /// </summary>
    public static void Save(AppConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConfigLoader] Failed to save config.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts watching config.json for external edits.
    /// Calls <paramref name="onReloaded"/> with the fresh config on each change.
    /// Debounced by 300ms to avoid double-fire from editors.
    /// </summary>
    public static void Watch(Action<AppConfig> onReloaded)
    {
        _watcher?.Dispose();

        var dir = Path.GetDirectoryName(ConfigPath)!;
        var file = Path.GetFileName(ConfigPath);

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        // Debounce timer — reset on each event, only fires after 300ms silence
        System.Threading.Timer? debounce = null;

        _watcher.Changed += (_, _) =>
        {
            debounce?.Dispose();
            debounce = new System.Threading.Timer(_ =>
            {
                debounce?.Dispose();
                debounce = null;
                var fresh = Load();
                onReloaded(fresh);
            }, null, 300, Timeout.Infinite);
        };
    }

    /// <summary>Stops watching config.json.</summary>
    public static void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
