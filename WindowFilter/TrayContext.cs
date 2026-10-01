using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WindowFilter;

/// <summary>
/// タスクトレイに常駐し、メニューからウィンドウ抑止を期限付きで有効化する。
/// 抑止時間の候補は設定ファイル（durations）から渡される。
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    // 抑止中/非抑止を色で示すアイコン（実行時に描画）。
    private static readonly Color ActiveColor = Color.FromArgb(46, 204, 113);   // 緑: 抑止中
    private static readonly Color InactiveColor = Color.FromArgb(150, 150, 150); // 灰: 抑止していない

    // メニューの「状態:」テキスト色（メニュー背景で読みやすいよう、アイコンより濃いめ）。
    private static readonly Color ActiveTextColor = Color.FromArgb(0, 128, 0);     // 緑
    private static readonly Color InactiveTextColor = Color.FromArgb(105, 105, 105); // 灰

    private readonly WindowFilter _guard;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly System.Windows.Forms.Timer _uiTimer;

    private readonly Icon _activeIcon;
    private readonly Icon _inactiveIcon;
    private IntPtr _activeHandle;
    private IntPtr _inactiveHandle;

    // メニュー「状態:」項目に付ける色付きの丸（有効項目なので色が出る）。
    private readonly Bitmap _activeDot;
    private readonly Bitmap _inactiveDot;

    private bool _lastActive;

    public TrayContext(WindowFilter guard, IReadOnlyList<DurationOption> durations)
    {
        _guard = guard;

        _activeIcon = MakeDotIcon(ActiveColor, out _activeHandle);
        _inactiveIcon = MakeDotIcon(InactiveColor, out _inactiveHandle);
        _activeDot = MakeDotBitmap(ActiveColor, 16);
        _inactiveDot = MakeDotBitmap(InactiveColor, 16);

        // 無効（Enabled=false）だとテキスト色が効かないため、有効のままにしてクリックは無処理。
        _statusItem = new ToolStripMenuItem("状態: 抑止していません");
        _stopItem = new ToolStripMenuItem("抑止を停止", null, (_, _) => Stop()) { Enabled = false };

        // 「ウィンドウを抑止する」→ 候補（30分 / 1時間 / … / 無制限）のサブメニュー
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

        // 残り時間表示と、満了時のメニュー状態を追従させるための更新用タイマー。
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
        _guard.Deactivate("手動停止");
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        DateTime? until = _guard.ActiveUntilUtc;
        bool active = until.HasValue;

        if (until is { } u)
        {
            string text = u == DateTime.MaxValue
                ? "抑止中（無制限）"
                : $"抑止中（残り {Remaining(u)}）";
            _statusItem.Text = "状態: " + text;
            _notifyIcon.Text = "WindowFilter — " + text;
            _stopItem.Enabled = true;
        }
        else
        {
            _statusItem.Text = "状態: 抑止していません";
            _notifyIcon.Text = "WindowFilter";
            _stopItem.Enabled = false;
        }

        // 「状態:」項目も色分けする（テキスト色＋色付きの丸）。
        _statusItem.ForeColor = active ? ActiveTextColor : InactiveTextColor;
        _statusItem.Image = active ? _activeDot : _inactiveDot;

        // 状態が変わったときだけトレイアイコンを差し替える（緑=抑止中 / 灰=非抑止）。
        if (active != _lastActive)
        {
            _notifyIcon.Icon = active ? _activeIcon : _inactiveIcon;
            _lastActive = active;
        }
    }

    /// <summary>指定色の丸を描いたビットマップ（メニュー項目の画像用）。</summary>
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
        using var pen = new Pen(Color.FromArgb(90, 0, 0, 0), 1.5f);
        g.DrawEllipse(pen, m, m, d, d);
        return bmp;
    }

    /// <summary>指定色の丸を描いたトレイ用アイコンを生成する。handle は後で DestroyIcon する。</summary>
    private static Icon MakeDotIcon(Color color, out IntPtr handle)
    {
        using var bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 3, 3, 26, 26);
            using var pen = new Pen(Color.FromArgb(90, 0, 0, 0), 2f);
            g.DrawEllipse(pen, 3, 3, 26, 26);
        }

        handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    private static string Remaining(DateTime untilUtc)
    {
        TimeSpan remain = untilUtc - DateTime.UtcNow;
        if (remain < TimeSpan.Zero)
        {
            remain = TimeSpan.Zero;
        }
        // 1 時間以上は h:mm:ss、未満は m:ss で表示。
        return remain.TotalHours >= 1
            ? $"{(int)remain.TotalHours}:{remain.Minutes:00}:{remain.Seconds:00}"
            : $"{remain.Minutes}:{remain.Seconds:00}";
    }

    private void ExitApp()
    {
        _guard.Deactivate("終了");
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
            if (_activeHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_activeHandle);
                _activeHandle = IntPtr.Zero;
            }
            if (_inactiveHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_inactiveHandle);
                _inactiveHandle = IntPtr.Zero;
            }
        }
        base.Dispose(disposing);
    }
}
