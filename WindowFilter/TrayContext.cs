using System.Drawing;
using System.Windows.Forms;

namespace WindowFilter;

/// <summary>
/// タスクトレイに常駐し、メニューからウィンドウ抑止を期限付きで有効化する。
/// 抑止時間の候補は設定ファイル（durations）から渡される。
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly WindowFilter _guard;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly System.Windows.Forms.Timer _uiTimer;

    public TrayContext(WindowFilter guard, IReadOnlyList<DurationOption> durations)
    {
        _guard = guard;

        _statusItem = new ToolStripMenuItem("状態: 抑止していません") { Enabled = false };
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
            Icon = SystemIcons.Application,
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
        }
        base.Dispose(disposing);
    }
}
