using System.Drawing.Drawing2D;

namespace LinkManager.UI;

/// <summary>
/// Programmatically draws 16×16 system tray icons representing the current engine state.
/// No .ico asset files required.
/// </summary>
public static class IconRenderer
{
    // ── State enum ────────────────────────────────────────────────────────────

    public enum SystemState
    {
        /// <summary>Highest-priority group (priority 0) is active and healthy. Green.</summary>
        PrimaryActive,

        /// <summary>Running on a fallback/backup group. Orange.</summary>
        BackupActive,

        /// <summary>No adapter has internet. Red.</summary>
        AllDead,

        /// <summary>Currently executing a switch or in hold-down cooldown. Blue.</summary>
        Switching,

        /// <summary>Engine is paused. Gray.</summary>
        Paused
    }

    // ── Color palette ─────────────────────────────────────────────────────────

    private static readonly Color ColorPrimary  = Color.FromArgb(0x22, 0xC5, 0x5E); // vivid green
    private static readonly Color ColorBackup   = Color.FromArgb(0xF5, 0xA6, 0x23); // amber
    private static readonly Color ColorDead     = Color.FromArgb(0xE5, 0x3E, 0x3E); // red
    private static readonly Color ColorSwitching = Color.FromArgb(0x3B, 0x82, 0xF6); // blue
    private static readonly Color ColorPaused   = Color.FromArgb(0x94, 0xA3, 0xB8); // slate gray

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a 16×16 icon for the given system state.
    /// Caller is responsible for disposal.
    /// </summary>
    public static Icon CreateIcon(SystemState state)
    {
        var fillColor = state switch
        {
            SystemState.PrimaryActive  => ColorPrimary,
            SystemState.BackupActive   => ColorBackup,
            SystemState.AllDead        => ColorDead,
            SystemState.Switching      => ColorSwitching,
            SystemState.Paused         => ColorPaused,
            _                          => ColorPaused
        };

        return DrawCircleIcon(fillColor);
    }

    // ── Private drawing ───────────────────────────────────────────────────────

    private static Icon DrawCircleIcon(Color fillColor)
    {
        var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Subtle dark border for contrast against any taskbar background
            var border = Color.FromArgb(80, 0, 0, 0);
            using (var borderBrush = new SolidBrush(border))
                g.FillEllipse(borderBrush, 1, 1, 14, 14);

            // Main fill
            using (var fillBrush = new SolidBrush(fillColor))
                g.FillEllipse(fillBrush, 2, 2, 12, 12);

            // Subtle highlight for a glassy look
            using var highlightBrush = new LinearGradientBrush(
                new Rectangle(3, 2, 9, 6),
                Color.FromArgb(120, 255, 255, 255),
                Color.Transparent,
                LinearGradientMode.Vertical);
            g.FillEllipse(highlightBrush, 3, 2, 9, 6);
        }

        // Convert bitmap → icon, then immediately destroy the GDI handle
        var hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        var copy = (Icon)icon.Clone();

        // Destroy the original GDI handle to avoid leaking
        NativeMethods.DestroyIcon(hIcon);
        icon.Dispose();
        bmp.Dispose();

        return copy;
    }
}

/// <summary>P/Invoke for GDI icon handle cleanup.</summary>
internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyIcon(IntPtr hIcon);
}
