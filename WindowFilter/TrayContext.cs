using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace WindowFilter;

/// <summary>
/// Resides in the system tray and enables window suppression for a limited time from the menu.
/// The suppression-duration options are passed in from the config file (durations).
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    // Text color of the menu status item
    private static readonly Color ActiveTextColor = Color.FromArgb(0, 128, 0);     // green
    private static readonly Color InactiveTextColor = Color.FromArgb(105, 105, 105); // gray

    // Color of the dot next to the status label (active = green / inactive = gray)
    private static readonly Color ActiveDotColor = Color.FromArgb(46, 204, 113);
    private static readonly Color InactiveDotColor = Color.FromArgb(150, 150, 150);

    private readonly WindowFilter _guard;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly System.Windows.Forms.Timer _uiTimer;

    // Load the pre-made icons embedded in the exe (active = green / inactive = gray)
    private readonly Icon _activeIcon;
    private readonly Icon _inactiveIcon;

    // 16px colored dots shown on the menu status item
    private readonly Bitmap _activeDot;
    private readonly Bitmap _inactiveDot;

    private bool? _lastActive; // starts null so the first update is always applied

    public TrayContext(WindowFilter guard, IReadOnlyList<DurationOption> durations)
    {
        _guard = guard;

        _activeIcon = LoadIcon("tray_active.ico");
        _inactiveIcon = LoadIcon("tray_inactive.ico");
        _activeDot = MakeDotBitmap(ActiveDotColor, 16);
        _inactiveDot = MakeDotBitmap(InactiveDotColor, 16);

        // A disabled (Enabled=false) item ignores ForeColor, so keep it enabled with a no-op click.
        _statusItem = new ToolStripMenuItem("状態: 抑止していません");
        _stopItem = new ToolStripMenuItem("抑止を停止", null, (_, _) => Stop()) { Enabled = false };

        // "Suppress windows" -> submenu of options (30 min / 1 hour / ... / unlimited)
        var suppress = new ToolStripMenuItem("ウィンドウを抑止する");
        foreach (DurationOption opt in durations)
        {
            DurationOption captured = opt;
            suppress.DropDownItems.Add(new ToolStripMenuItem(opt.Label, null, (_, _) => Start(captured)));
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(suppress);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("終了", null, (_, _) => ExitApp()));

        _notifyIcon = new NotifyIcon
        {
            Icon = _inactiveIcon,
            Text = "WindowFilter",
            Visible = true,
            ContextMenuStrip = menu,
        };

        // Update timer to refresh the remaining-time display and track menu state on expiry.
        _uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _uiTimer.Tick += (_, _) => UpdateStatus();
        _uiTimer.Start();

        UpdateStatus();
    }

    private void Start(DurationOption option)
    {
        _guard.Activate(option.Duration);
        UpdateStatus();
        _notifyIcon.ShowBalloonTip(3000, "WindowFilter",
            $"ウィンドウを抑止します（{option.Label}）", ToolTipIcon.Info);
    }

    private void Stop()
    {
        _guard.Deactivate("manual stop");
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        DateTime? until = _guard.ActiveUntilUtc;
        bool active = until.HasValue;

        // Update color/image/icon/stop-button only when the state changes (avoid per-second assignment).
        if (_lastActive != active)
        {
            _notifyIcon.Icon = active ? _activeIcon : _inactiveIcon;
            _statusItem.ForeColor = active ? ActiveTextColor : InactiveTextColor;
            _statusItem.Image = active ? _activeDot : _inactiveDot;
            _stopItem.Enabled = active;
            _lastActive = active;
        }

        // Text changes per second only during finite suppression. Re-assigning the same string is ignored by the setter.
        string text = until switch
        {
            { } when _guard.IsAutoActive => "抑止中（フォーカス中）",
            { } u when u == DateTime.MaxValue => "抑止中（無制限）",
            { } u => $"抑止中（残り {Remaining(u)}）",
            _ => "抑止していません",
        };
        _statusItem.Text = "状態: " + text;
        _notifyIcon.Text = active ? "WindowFilter — " + text : "WindowFilter";
    }

    /// <summary>A colored dot shown next to the status label (for the menu item image).</summary>
    private static Bitmap MakeDotBitmap(Color color, int size)
    {
        var bmp = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        int m = 2;
        int d = size - (m * 2) - 1;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, m, m, d, d);
        return bmp;
    }

    /// <summary>Load an icon embedded in the exe (Assets\*.ico) by file name.</summary>
    private static Icon LoadIcon(string fileName)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        string? name = Array.Find(
            asm.GetManifestResourceNames(),
            n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));

        if (name is null)
        {
            Logger.Line("tray: icon resource not found: " + fileName);
            return SystemIcons.Application;
        }

        using Stream? stream = asm.GetManifestResourceStream(name);
        return stream is not null ? new Icon(stream) : SystemIcons.Application;
    }

    private static string Remaining(DateTime untilUtc)
    {
        TimeSpan remain = untilUtc - DateTime.UtcNow;
        if (remain < TimeSpan.Zero)
        {
            remain = TimeSpan.Zero;
        }
        // Show h:mm:ss for an hour or more, m:ss otherwise.
        return remain.TotalHours >= 1
            ? $"{(int)remain.TotalHours}:{remain.Minutes:00}:{remain.Seconds:00}"
            : $"{remain.Minutes}:{remain.Seconds:00}";
    }

    private void ExitApp()
    {
        _guard.Deactivate("exit");
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uiTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            _activeIcon.Dispose();
            _inactiveIcon.Dispose();
            _activeDot.Dispose();
            _inactiveDot.Dispose();
        }
        base.Dispose(disposing);
    }
}
