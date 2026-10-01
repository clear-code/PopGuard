using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace WindowFilter;

/// <summary>
/// タスクトレイに常駐し、メニューからウィンドウ抑止を期限付きで有効化する。
/// 抑止時間の候補は設定ファイル（durations）から渡される。
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    // メニューの「状態:」テキスト色（メニュー背景で読みやすいよう濃いめ）。
    private static readonly Color ActiveTextColor = Color.FromArgb(0, 128, 0);     // 緑
    private static readonly Color InactiveTextColor = Color.FromArgb(105, 105, 105); // 灰

    private readonly WindowFilter _guard;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly System.Windows.Forms.Timer _uiTimer;

    // 事前に用意して exe に埋め込んだアイコン（抑止中=緑 / 非抑止=灰）を読み込む。
    private readonly Icon _activeIcon;
    private readonly Icon _inactiveIcon;

    // メニュー「状態:」項目に付ける 16px アイコン（埋め込みアイコンから抽出）。
    private readonly Bitmap _activeDot;
    private readonly Bitmap _inactiveDot;

    private bool? _lastActive; // 初回は必ず反映させるため null 始まり

    public TrayContext(WindowFilter guard, IReadOnlyList<DurationOption> durations)
    {
        _guard = guard;

        _activeIcon = LoadIcon("tray_active.ico");
        _inactiveIcon = LoadIcon("tray_inactive.ico");
        _activeDot = new Icon(_activeIcon, new Size(16, 16)).ToBitmap();
        _inactiveDot = new Icon(_inactiveIcon, new Size(16, 16)).ToBitmap();

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

        // 色・画像・アイコン・停止ボタンは「状態が変わったとき」だけ更新する（毎秒の代入を避ける）。
        if (_lastActive != active)
        {
            _notifyIcon.Icon = active ? _activeIcon : _inactiveIcon;
            _statusItem.ForeColor = active ? ActiveTextColor : InactiveTextColor;
            _statusItem.Image = active ? _activeDot : _inactiveDot;
            _stopItem.Enabled = active;
            _lastActive = active;
        }

        // テキストは有限の抑止中のみ毎秒変わる。固定文言の再代入は setter 側で無視される。
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

    /// <summary>exe に埋め込んだアイコン（Assets\*.ico）を名前で読み込む。</summary>
    private static Icon LoadIcon(string fileName)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        string? name = Array.Find(
            asm.GetManifestResourceNames(),
            n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));

        if (name is null)
        {
            Logger.Line("tray: アイコンリソースが見つかりません: " + fileName);
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
        }
        base.Dispose(disposing);
    }
}
