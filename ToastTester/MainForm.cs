using Microsoft.Toolkit.Uwp.Notifications;

namespace ToastTester;

/// <summary>
/// テスト用の操作ウィンドウ。ボタンで、または一定間隔で右下にトーストを表示する。
/// トースト側が PopGuard の検知・裏送り対象（TOPMOST の独立ウィンドウ）になる。
/// </summary>
internal sealed class MainForm : Form
{
    private int _counter;
    private readonly System.Windows.Forms.Timer _autoTimer = new() { Interval = 3000 };

    public MainForm()
    {
        Text = "Toast Tester";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(320, 200);

        var show = new Button
        {
            Text = "自前トーストを表示",
            Left = 16,
            Top = 16,
            Width = 220,
            Height = 32,
        };
        show.Click += (_, _) => ShowToast();
        Controls.Add(show);

        var showOs = new Button
        {
            Text = "OS トースト（本物）を表示",
            Left = 16,
            Top = 56,
            Width = 220,
            Height = 32,
        };
        showOs.Click += (_, _) => ShowOsToast();
        Controls.Add(showOs);

        var showModal = new Button
        {
            Text = "モーダルウィンドウを表示（TOPMOST）",
            Left = 16,
            Top = 96,
            Width = 220,
            Height = 32,
        };
        showModal.Click += (_, _) => ShowModal();
        Controls.Add(showModal);

        var auto = new CheckBox
        {
            Text = "3秒ごとに自前トーストを自動表示",
            AutoSize = true,
            Left = 16,
            Top = 148,
        };
        auto.CheckedChanged += (_, _) =>
        {
            if (auto.Checked)
            {
                _autoTimer.Start();
            }
            else
            {
                _autoTimer.Stop();
            }
        };
        Controls.Add(auto);

        _autoTimer.Tick += (_, _) => ShowToast();
    }

    private void ShowToast()
    {
        _counter++;
        var toast = new ToastForm($"通知 #{_counter}   {DateTime.Now:HH:mm:ss}");
        toast.Show(); // オーナーを付けず独立したトップレベルウィンドウにする
    }

    /// <summary>
    /// 本物の OS トーストを表示する。描画・表示位置・消滅は OS の通知プラットフォームが行い、
    /// アプリはウィンドウを持たない（PopGuard では抑止できない種類）。
    /// 非パッケージ Win32 アプリの AUMID 登録は ToastContentBuilder（Compat 層）が肩代わりする。
    /// </summary>
    private void ShowOsToast()
    {
        _counter++;
        try
        {
            new ToastContentBuilder()
                .AddText("ToastTester")
                .AddText($"本物の OS トースト #{_counter}   {DateTime.Now:HH:mm:ss}")
                .Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "OS トーストの表示に失敗しました:\n" + ex.Message,
                "ToastTester", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// モーダルウィンドウを表示する。ShowDialog でオーナー（このウィンドウ）が無効化され、
    /// かつ TOPMOST なので、PopGuard のモーダル回避ガードの確認に使える。
    /// </summary>
    private void ShowModal()
    {
        using var dlg = new ModalForm();
        dlg.ShowDialog(this);
    }
}
