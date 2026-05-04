using LinkManager.Configuration;
using LinkManager.Engine;
using LinkManager.Helpers;
using LinkManager.Models;

namespace LinkManager.UI;

/// <summary>
/// Manages the system tray NotifyIcon, dynamic tooltip, icon color,
/// and context menu. Observes the LinkEngine via events.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly LinkEngine _engine;
    private readonly ToastNotifier _toastNotifier;
    private readonly ContextMenuStrip _menu;
    private readonly SynchronizationContext _syncContext;
    private AppConfig _config;

    // Menu item references for toggling
    private ToolStripMenuItem _pauseItem = null!;
    private ToolStripMenuItem _notificationsItem = null!;
    private ToolStripMenuItem _autoStartItem = null!;
    private ToolStripMenuItem _statusItem = null!;

    // Tracks which icons we've created so we can dispose them
    private Icon? _currentIcon;

    public TrayIcon(LinkEngine engine, AppConfig config)
    {
        _engine = engine;
        _config = config;

        // Tray-only apps don't create a Form, so WinForms never auto-installs
        // WindowsFormsSynchronizationContext. We install it explicitly here on
        // the main thread so we can marshal engine events to the UI thread.
        if (SynchronizationContext.Current == null)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _syncContext = SynchronizationContext.Current!;

        _notifyIcon = new NotifyIcon { Visible = true, Text = "LinkManager" };
        _menu = BuildContextMenu();
        _notifyIcon.ContextMenuStrip = _menu;

        _toastNotifier = new ToastNotifier(_notifyIcon, () => _config.EnableNotifications);

        // Wire engine events (must marshal to UI thread via BeginInvoke)
        engine.OnStateChanged += () => SafeInvoke(UpdateDisplay);
        engine.OnSwitchCompleted += group => SafeInvoke(() =>
        {
            var names = string.Join(", ", group.Select(a => a.Identifier));
            _toastNotifier.Notify("LinkManager — Network Switch", $"Now active: {names}");
        });

        // Initial display
        UpdateDisplay();
    }

    // ── Context menu builder ──────────────────────────────────────────────────

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        // Status label (disabled, updated dynamically)
        _statusItem = new ToolStripMenuItem("Status: Initializing...")
        {
            Enabled = false,
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold)
        };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        // Force Re-evaluate
        var forceItem = new ToolStripMenuItem("Force Re-evaluate");
        forceItem.Click += (_, _) => _engine.ForceReevaluate();
        menu.Items.Add(forceItem);

        // Pause / Resume
        _pauseItem = new ToolStripMenuItem("Pause Auto-Fallback");
        _pauseItem.Click += OnPauseClicked;
        menu.Items.Add(_pauseItem);

        menu.Items.Add(new ToolStripSeparator());

        // Notifications toggle
        _notificationsItem = new ToolStripMenuItem("Notifications")
        {
            CheckOnClick = true,
            Checked = _config.EnableNotifications
        };
        _notificationsItem.CheckedChanged += OnNotificationsToggled;
        menu.Items.Add(_notificationsItem);

        // Auto-start toggle
        _autoStartItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = AutoStartManager.IsRegistered()
        };
        _autoStartItem.CheckedChanged += OnAutoStartToggled;
        menu.Items.Add(_autoStartItem);

        menu.Items.Add(new ToolStripSeparator());

        // Open Config
        var openConfigItem = new ToolStripMenuItem("Open Config");
        openConfigItem.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("notepad.exe", ConfigLoader.ConfigPath); }
            catch { }
        };
        menu.Items.Add(openConfigItem);

        // Reload Config
        var reloadItem = new ToolStripMenuItem("Reload Config");
        reloadItem.Click += (_, _) =>
        {
            var fresh = ConfigLoader.Load();
            _config = fresh;
            _engine.ReloadConfig(fresh);
        };
        menu.Items.Add(reloadItem);

        menu.Items.Add(new ToolStripSeparator());

        // Exit
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            _engine.Stop();
            Application.Exit();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    // ── Display update ────────────────────────────────────────────────────────

    private void UpdateDisplay()
    {
        UpdateIcon();
        UpdateTooltip();
        UpdateStatusLabel();
        _pauseItem.Text = _engine.IsPaused ? "Resume Auto-Fallback" : "Pause Auto-Fallback";
    }

    private void UpdateIcon()
    {
        var state = DetermineSystemState();
        var oldIcon = _currentIcon;
        _currentIcon = IconRenderer.CreateIcon(state);
        _notifyIcon.Icon = _currentIcon;
        oldIcon?.Dispose();
    }

    private void UpdateTooltip()
    {
        var lines = new List<string>();

        // Header
        if (_engine.IsPaused)
            lines.Add("LinkManager — PAUSED");
        else if (_engine.IsCoolingDown)
            lines.Add("LinkManager — Switching...");
        else if (_engine.ActiveGroupPriority < 0)
            lines.Add("LinkManager — No Internet");
        else if (_engine.ActiveGroupPriority == 0)
            lines.Add("LinkManager — Stable (Primary)");
        else
            lines.Add($"LinkManager — Backup (Group {_engine.ActiveGroupPriority})");

        lines.Add(new string('─', 24));

        // Per-adapter status
        foreach (var adapter in _engine.Adapters.OrderBy(a => a.Priority))
        {
            string icon = adapter.IsActive ? "●" : (adapter.IsHealthy ? "○" : "✕");
            string health = !adapter.IsPresent
                ? "Unplugged"
                : adapter.IsHealthy ? "Internet OK" : $"No Internet ({adapter.ConsecutiveFailures} fails)";

            string name = adapter.InterfaceName ?? adapter.Identifier;
            lines.Add($"[P{adapter.Priority}] {icon} {TruncateName(name, 16)}: {health}");
        }

        // NotifyIcon tooltip max is 128 chars — trim to be safe
        var tooltip = string.Join("\n", lines);
        if (tooltip.Length > 127) tooltip = tooltip[..127];
        _notifyIcon.Text = tooltip;
    }

    private void UpdateStatusLabel()
    {
        if (_engine.IsPaused)
            _statusItem.Text = "Status: Paused";
        else if (_engine.IsCoolingDown)
            _statusItem.Text = "Status: Switching...";
        else if (_engine.ActiveGroupPriority < 0)
            _statusItem.Text = "Status: ⚠ No Internet";
        else
            _statusItem.Text = $"Status: ✓ Active (Group {_engine.ActiveGroupPriority})";
    }

    private IconRenderer.SystemState DetermineSystemState()
    {
        if (_engine.IsPaused) return IconRenderer.SystemState.Paused;
        if (_engine.IsCoolingDown) return IconRenderer.SystemState.Switching;
        if (_engine.ActiveGroupPriority < 0) return IconRenderer.SystemState.AllDead;
        if (_engine.ActiveGroupPriority == 0) return IconRenderer.SystemState.PrimaryActive;
        return IconRenderer.SystemState.BackupActive;
    }

    // ── Menu event handlers ───────────────────────────────────────────────────

    private void OnPauseClicked(object? sender, EventArgs e)
    {
        _engine.IsPaused = !_engine.IsPaused;
        UpdateDisplay();
    }

    private void OnNotificationsToggled(object? sender, EventArgs e)
    {
        _config.EnableNotifications = _notificationsItem.Checked;
        ConfigLoader.Save(_config);
    }

    private async void OnAutoStartToggled(object? sender, EventArgs e)
    {
        try
        {
            if (_autoStartItem.Checked)
                await AutoStartManager.Register();
            else
                await AutoStartManager.Unregister();

            _config.StartWithWindows = _autoStartItem.Checked;
            ConfigLoader.Save(_config);
        }
        catch (Exception ex)
        {
            _autoStartItem.Checked = !_autoStartItem.Checked; // revert
            MessageBox.Show($"Failed to update startup task:\n{ex.Message}",
                "LinkManager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static string TruncateName(string name, int max) =>
        name.Length <= max ? name : name[..(max - 1)] + "…";

    /// <summary>
    /// Marshals action to the UI thread using the captured SynchronizationContext.
    /// Safe to call from any background thread or timer callback.
    /// </summary>
    private void SafeInvoke(Action action)
        => _syncContext.Post(_ => action(), null);

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _currentIcon?.Dispose();
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
