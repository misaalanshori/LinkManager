namespace LinkManager.Helpers;

/// <summary>
/// Sends Windows toast notifications for network switch events.
/// Uses NotifyIcon.ShowBalloonTip for maximum compatibility without
/// requiring UWP package identity.
/// Guards against the notification being disabled in config.
/// </summary>
public sealed class ToastNotifier
{
    private readonly NotifyIcon _notifyIcon;
    private Func<bool> _isEnabled;

    /// <param name="notifyIcon">The app's tray NotifyIcon, used to show balloon tips.</param>
    /// <param name="isEnabled">Delegate returning the current enabled state from config.</param>
    public ToastNotifier(NotifyIcon notifyIcon, Func<bool> isEnabled)
    {
        _notifyIcon = notifyIcon;
        _isEnabled = isEnabled;
    }

    /// <summary>
    /// Shows a toast/balloon notification if notifications are enabled.
    /// </summary>
    public void Notify(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (!_isEnabled()) return;

        try
        {
            _notifyIcon.ShowBalloonTip(
                timeout: 4000,
                tipTitle: title,
                tipText: message,
                tipIcon: icon);
        }
        catch
        {
            // Suppress — UI errors should never crash the background engine
        }
    }
}
