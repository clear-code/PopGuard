namespace ToastTester;

/// <summary>
/// 右下に出るトースト風のポップアップ。数秒で自動的に閉じる。
/// 実際のトースト（Thunderbird の通知など）に近づけるため、
/// 最前面（TOPMOST）・非アクティブ化・ツールウィンドウ（タスクバー非表示）にしてある。
/// これにより PopGuard の「TOPMOST の独立ウィンドウを裏へ送る」動作を試せる。
/// </summary>
internal sealed class ToastForm : Form
{
    private const int ScreenMargin = 12;
    private const int StackGap = 8;

    private readonly System.Windows.Forms.Timer _life = new() { Interval = 5000 };

    // フォーカスを奪わずに表示する（実際のトーストと同じ挙動）。
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x00000008;
            const int WS_EX_TOOLWINDOW = 0x00000080;   // タスクバーに出さない
            const int WS_EX_NOACTIVATE = 0x08000000;   // クリックしても前面化しない

            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public ToastForm(string message)
    {
        Text = "ToastTester Toast"; // GetWindowText で取得できるタイトル（ルールの title 照合用）
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(300, 90);
        BackColor = Color.FromArgb(40, 40, 40);

        Controls.Add(new Label
        {
            Text = message,
            ForeColor = Color.White,
            Font = new Font("Yu Gothic UI", 11f),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        });

        _life.Tick += (_, _) => Close();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PositionBottomRight();
        _life.Start();
    }

    /// <summary>右下に配置し、既に開いているトーストの上に積む。</summary>
    private void PositionBottomRight()
    {
        Rectangle wa = Screen.PrimaryScreen!.WorkingArea;

        int stacked = 0;
        foreach (Form f in Application.OpenForms)
        {
            if (f is ToastForm && f != this)
            {
                stacked++;
            }
        }

        int x = wa.Right - Width - ScreenMargin;
        int y = wa.Bottom - Height - ScreenMargin - stacked * (Height + StackGap);
        Location = new Point(x, y);
    }
}
