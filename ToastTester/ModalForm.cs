namespace ToastTester;

/// <summary>
/// テスト用のモーダルウィンドウ。TOPMOST かつモーダル（ShowDialog でオーナーを無効化、
/// FixedDialog でモーダルフレーム）にしてあり、PopGuard のモーダル回避ガードで
/// 「抑止されない（guard skip）」ことを確認するためのもの。
/// </summary>
internal sealed class ModalForm : Form
{
    public ModalForm()
    {
        Text = "ToastTester Modal"; // ルール照合用のタイトル
        FormBorderStyle = FormBorderStyle.FixedDialog; // DS_MODALFRAME → WS_EX_DLGMODALFRAME
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;              // 最前面（PopGuard の対象条件を満たす）
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 130);

        Controls.Add(new Label
        {
            Text = "これはモーダルウィンドウです。\n"
                 + "TOPMOST ですが、PopGuard には抑止されず前面のままになります。",
            Dock = DockStyle.Top,
            Height = 70,
            TextAlign = ContentAlignment.MiddleCenter,
        });

        var close = new Button
        {
            Text = "閉じる",
            DialogResult = DialogResult.OK,
            Width = 100,
            Left = (ClientSize.Width - 100) / 2,
            Top = 85,
        };
        Controls.Add(close);
        AcceptButton = close;
    }
}
