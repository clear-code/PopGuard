using System.Diagnostics;
using System.Text;
using static WindowFilter.NativeMethods;

namespace WindowFilter;

/// <summary>どかし方。</summary>
internal enum HideMethod
{
    Bottom,   // Z オーダーの最背面へ送る（最も影響が小さく、確実に戻せる）
    Minimize, // 最小化
    Hide,     // 非表示
}

/// <summary>
/// ルールに一致したウィンドウを裏へ送り、追跡しておき、終了・異常時に必ず元へ戻す。
/// TOPMOST を条件にするかはルールごとに設定できる（TopMostOnly）。
///
/// 呼び出す Win32 API は SetWindowPos / ShowWindowAsync のみ。
/// プロセス停止・ファイル/レジストリ変更は一切行わない。
/// </summary>
internal sealed class WindowFilter
{
    private sealed class Tracked
    {
        public IntPtr Hwnd;
        public bool WasTopMost;
        public HideMethod Applied;
        public string Process = string.Empty;
        public string Title = string.Empty;
    }

    private readonly IReadOnlyList<TargetRule> _rules;
    private readonly int _ownPid = Process.GetCurrentProcess().Id;
    private readonly object _lock = new();
    private readonly Dictionary<IntPtr, Tracked> _tracked = new();

    // モーダルとみなして対象外にした HWND（同じものを毎回ログしないための記録）。_lock で保護する。
    private readonly HashSet<IntPtr> _skippedModal = new();

    // 抑止の有効期限（UTC）。null なら抑止していない。トレイ操作（UI スレッド）と
    // ポーリング（Timer スレッド）の両方から触るため _stateLock で保護する。
    private readonly object _stateLock = new();
    private DateTime? _activeUntil;
    private bool _autoActive; // フォーカス連動で自動的に有効化したか

    public WindowFilter(IReadOnlyList<TargetRule> rules) => _rules = rules;

    /// <summary>抑止が現在有効か。</summary>
    public bool IsActive
    {
        get
        {
            lock (_stateLock)
            {
                return _activeUntil is { } until && DateTime.UtcNow < until;
            }
        }
    }

    /// <summary>抑止の有効期限（UTC）。無効なら null。表示用。</summary>
    public DateTime? ActiveUntilUtc
    {
        get
        {
            lock (_stateLock)
            {
                return _activeUntil;
            }
        }
    }

    /// <summary>現在の抑止がフォーカス連動による自動抑止か。表示用。</summary>
    public bool IsAutoActive
    {
        get
        {
            lock (_stateLock)
            {
                return _autoActive && _activeUntil.HasValue;
            }
        }
    }

    /// <summary>抑止を手動で有効にする。<paramref name="duration"/> が null なら無制限。</summary>
    public void Activate(TimeSpan? duration)
    {
        lock (_stateLock)
        {
            _activeUntil = duration is { } d ? DateTime.UtcNow + d : DateTime.MaxValue;
            _autoActive = false; // 手動操作
        }
        Logger.Line(duration is { } dd
            ? $"guard: 抑止を開始（{dd.TotalMinutes:0} 分）"
            : "guard: 抑止を開始（無制限）");
    }

    /// <summary>抑止を止め、裏へ送ったウィンドウを元に戻す。</summary>
    public void Deactivate(string reason)
    {
        bool wasActive;
        lock (_stateLock)
        {
            wasActive = _activeUntil.HasValue;
            _activeUntil = null;
            _autoActive = false;
        }
        if (wasActive)
        {
            RestoreAll();
            Logger.Line($"guard: 抑止を終了（{reason}）");
        }
    }

    /// <summary>
    /// フォーカス（フォーカス セッション）の状態変化を受けて自動抑止を切り替える。
    /// 自動で始めた抑止だけ自動で解除し、手動の抑止は尊重する。
    /// </summary>
    public void OnFocusChanged(bool focusActive)
    {
        bool started = false;
        bool ended = false;
        lock (_stateLock)
        {
            if (focusActive)
            {
                // 抑止していなければ、フォーカス終了まで自動で抑止する。
                if (_activeUntil is null)
                {
                    _activeUntil = DateTime.MaxValue;
                    _autoActive = true;
                    started = true;
                }
            }
            else if (_autoActive)
            {
                // 自動で始めたものだけ解除（手動はそのまま）。
                _activeUntil = null;
                _autoActive = false;
                ended = true;
            }
        }

        if (started)
        {
            Logger.Line("guard: 抑止を開始（フォーカス中）");
        }
        if (ended)
        {
            RestoreAll();
            Logger.Line("guard: 抑止を終了（フォーカス終了）");
        }
    }

    /// <summary>有効期限が切れていれば抑止を解除する。ポーリングから毎回呼ぶ。</summary>
    public void CheckExpiry()
    {
        bool expired;
        lock (_stateLock)
        {
            expired = _activeUntil is { } until && DateTime.UtcNow >= until;
            if (expired)
            {
                _activeUntil = null;
                _autoActive = false;
            }
        }
        if (expired)
        {
            RestoreAll();
            Logger.Line("guard: 抑止を終了（時間満了）");
        }
    }

    /// <summary>
    /// 1 つのウィンドウを評価し、一致するルールがあれば裏へ送る。
    /// 既に処理済みの相手が最前面を立て直していたら、もう一度裏へ送る（再アサート対策）。
    /// </summary>
    public void Consider(IntPtr hwnd)
    {
        // 抑止が有効なときだけ動く（トレイから期限付きで有効化される）。
        if (!IsActive)
        {
            return;
        }
        if (hwnd == IntPtr.Zero || _rules.Count == 0)
        {
            return;
        }
        if (!IsWindow(hwnd) || !IsWindowVisible(hwnd))
        {
            return;
        }

        // 現状は TOPMOST のウィンドウのみを対象にする。
        // ルールの topMostOnly は設定として残しているが未実装（常に TOPMOST 扱い）。
        if (!IsTopMost(hwnd))
        {
            return;
        }

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || (int)pid == _ownPid)
        {
            return;
        }

        // まず安いプロセス名で候補を絞る（タイトル取得は高コストなので後回し）。
        string process = ProcessName((int)pid);
        if (!AnyRuleMatchesProcess(process))
        {
            return;
        }

        string className = ClassName(hwnd);
        string title = Title(hwnd);

        TargetRule? rule = FindRule(process, className, title);
        if (rule is null)
        {
            return;
        }

        // モーダル／ダイアログ回避：裏へ送るとアプリが進めなくなる（固まって見える）ため触らない。
        // 企業向けに安全側へ倒し、挙動（オーナー無効）だけでなくスタイル・クラスでも識別する。
        if (IsLikelyModal(hwnd, out string modalReason))
        {
            bool firstTime;
            lock (_lock)
            {
                firstTime = _skippedModal.Add(hwnd);
                if (_skippedModal.Count > 256)
                {
                    _skippedModal.Clear();
                }
            }
            if (firstTime)
            {
                Logger.Line($"guard skip: process={process} title={title} (ダイアログの可能性: {modalReason})");
            }
            return;
        }

        lock (_lock)
        {
            if (_tracked.TryGetValue(hwnd, out Tracked? known))
            {
                // 追跡済み。相手が最前面を立て直したので、ログを増やさず再度裏へ送る。
                SendBack(hwnd, known.Applied);
                return;
            }

            var t = new Tracked
            {
                Hwnd = hwnd,
                WasTopMost = true, // TOPMOST のみ対象なので必ず true。復元時に TOPMOST へ戻す。
                Applied = rule.Hide,
                Process = process,
                Title = title,
            };
            _tracked[hwnd] = t;

            Demote(hwnd);
            SendBack(hwnd, t.Applied);
            Logger.Line($"guard demote: process={t.Process} title={t.Title} method={t.Applied}");
        }
    }

    private bool AnyRuleMatchesProcess(string process)
    {
        foreach (TargetRule r in _rules)
        {
            if (r.MatchesProcess(process))
            {
                return true;
            }
        }
        return false;
    }

    private TargetRule? FindRule(string process, string className, string title)
    {
        foreach (TargetRule r in _rules)
        {
            if (r.Matches(process, className, title))
            {
                return r;
            }
        }
        return null;
    }

    /// <summary>追跡中のウィンドウを、隠し方を逆転してから（元が TOPMOST なら）TOPMOST に戻す。終了・異常時に必ず通る。</summary>
    public void RestoreAll()
    {
        lock (_lock)
        {
            foreach (Tracked t in _tracked.Values)
            {
                if (!IsWindow(t.Hwnd))
                {
                    Logger.Line($"guard restore: process={t.Process} title={t.Title} (gone)");
                    continue;
                }

                if (t.Applied == HideMethod.Minimize)
                {
                    ShowWindowAsync(t.Hwnd, SW_RESTORE);
                }
                else if (t.Applied == HideMethod.Hide)
                {
                    ShowWindowAsync(t.Hwnd, SW_SHOWNA);
                }

                if (t.WasTopMost)
                {
                    SetWindowPos(t.Hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
                }

                Logger.Line($"guard restore: process={t.Process} title={t.Title}");
            }

            _tracked.Clear();
            _skippedModal.Clear();
        }
    }

    private static bool IsTopMost(IntPtr hwnd)
        => (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

    /// <summary>
    /// モーダル／ダイアログの可能性が高いかを、挙動・スタイル・クラスの複数シグナルで判定する。
    /// 企業向けに「本物のダイアログは絶対に裏送りしない」安全側へ倒すため、いずれか 1 つでも
    /// 該当すれば対象外とする。<paramref name="reason"/> に該当理由を返す（診断用）。
    ///
    /// - オーナー無効：モーダル中はオーナーの他ウィンドウが無効化される（挙動としての最強シグナル）
    /// - WS_EX_DLGMODALFRAME：モーダルフレームを持つダイアログ
    /// - クラス "#32770"：標準の Win32 ダイアログボックス（MessageBox / DialogBox 由来）
    /// </summary>
    private static bool IsLikelyModal(IntPtr hwnd, out string reason)
    {
        IntPtr owner = GetWindow(hwnd, GW_OWNER);
        if (owner != IntPtr.Zero && !IsWindowEnabled(owner))
        {
            reason = "オーナー無効";
            return true;
        }

        if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_DLGMODALFRAME) != 0)
        {
            reason = "モーダルフレーム(WS_EX_DLGMODALFRAME)";
            return true;
        }

        if (ClassName(hwnd) == "#32770")
        {
            reason = "標準ダイアログクラス(#32770)";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    /// <summary>最前面属性だけを外す。</summary>
    private static bool Demote(IntPtr hwnd)
        => SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);

    /// <summary>
    /// 明示的に裏へ送る。HWND_NOTOPMOST は「非最前面グループの先頭」に置くだけなので、
    /// これを行わないと見た目が変わらない。
    /// </summary>
    private static bool SendBack(IntPtr hwnd, HideMethod method) => method switch
    {
        HideMethod.Minimize => ShowWindowAsync(hwnd, SW_MINIMIZE),
        HideMethod.Hide => ShowWindowAsync(hwnd, SW_HIDE),
        _ => SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS),
    };

    private static string ProcessName(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Title(IntPtr hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(len + 2);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string ClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        int n = NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return n > 0 ? sb.ToString() : string.Empty;
    }
}
