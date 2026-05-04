using LinkManager.Configuration;
using LinkManager.Engine;
using LinkManager.Helpers;
using LinkManager.UI;

namespace LinkManager;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 1. Load configuration
        var config = ConfigLoader.Load();

        // 2. Build engine
        var engine = new LinkEngine(config);

        // 3. Wire logging to console (visible in debug, ignored in WinExe release)
        engine.OnLog += msg => Console.WriteLine(msg);

        // 4. Build tray icon (must exist before engine starts so UI events work)
        var tray = new TrayIcon(engine, config);

        // 5. Hot-reload: watch config.json for external edits
        // engine.ReloadConfig() is thread-safe (cancels + restarts the background task),
        // so no UI-thread marshaling is needed here.
        ConfigLoader.Watch(newConfig => engine.ReloadConfig(newConfig));

        // 6. Auto-start: register Task Scheduler entry on first run if configured
        if (config.StartWithWindows && !AutoStartManager.IsRegistered())
        {
            // Fire-and-forget — failure is non-fatal
            AutoStartManager.Register().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Console.Error.WriteLine($"[Startup] Auto-start registration failed: {t.Exception?.Message}");
            });
        }

        // 7. Start the engine loop
        engine.Start();

        // 8. Windows message loop — keeps the tray icon alive
        Application.Run();

        // 9. Cleanup on exit
        ConfigLoader.StopWatching();
        engine.Dispose();
        tray.Dispose();
    }
}
