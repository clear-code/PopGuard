namespace WindowFilter;

/// <summary>
/// Windows 11 のフォーカス（フォーカス セッション）の状態を購読し、変化を <see cref="WindowFilter"/> に伝える。
/// 自動抑止が有効なら、フォーカス中は抑止を自動 ON、終了で自動 OFF にする（手動操作は尊重）。
/// 手動の「応答不可（Do Not Disturb）」単体では IsFocusActive は変化しないため対象外。
/// </summary>
internal sealed class FocusSessionWatcher
{
    private readonly WindowFilter _guard;
    private readonly bool _autoSuppress;

    // イベント購読を保持するため参照を持ち続ける（GC 回収防止）。
    private Windows.UI.Shell.FocusSessionManager? _manager;

    public FocusSessionWatcher(WindowFilter guard, bool autoSuppress)
    {
        _guard = guard;
        _autoSuppress = autoSuppress;
    }

    /// <summary>購読を開始する。メッセージポンプのある STA スレッドで呼ぶこと。</summary>
    public void Start()
    {
        // FocusSessionManager は Windows 11 22H2(22621) で追加。古い OS では型自体が無いので、
        // 触れる前に OS バージョンでガードする（例外に頼らず綺麗にスキップ）。
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            Logger.Line("focus-session: この Windows では未対応（22H2/22621 未満）。フォーカス連動なしで動作します。");
            return;
        }

        try
        {
            if (!Windows.UI.Shell.FocusSessionManager.IsSupported)
            {
                Logger.Line("focus-session: この端末では未対応（IsSupported=false）");
                return;
            }

            _manager = Windows.UI.Shell.FocusSessionManager.GetDefault();
            bool active = _manager.IsFocusActive;
            Logger.Line($"focus-session: supported, IsFocusActive={active}, autoSuppress={_autoSuppress}");

            _manager.IsFocusActiveChanged += OnFocusActiveChanged;

            // 起動時すでにフォーカス中なら反映する。
            if (_autoSuppress && active)
            {
                _guard.OnFocusChanged(true);
            }
        }
        catch (Exception ex)
        {
            Logger.Line("focus-session: 取得・購読に失敗: " + ex.Message);
        }
    }

    private void OnFocusActiveChanged(Windows.UI.Shell.FocusSessionManager sender, object args)
    {
        bool active = sender.IsFocusActive;
        Logger.Line($"focus-session: IsFocusActive={active}");

        if (_autoSuppress)
        {
            _guard.OnFocusChanged(active);
        }
    }
}
