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

    public WindowFilter(IReadOnlyList<TargetRule> rules) => _rules = rules;

    /// <summary>
    /// 1 つのウィンドウを評価し、一致するルールがあれば裏へ送る。
    /// 既に処理済みの相手が最前面を立て直していたら、もう一度裏へ送る（再アサート対策）。
    /// </summary>
    public void Consider(IntPtr hwnd)
    {
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
            Log.Line($"guard demote: process={t.Process} title={t.Title} method={t.Applied}");
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
                    Log.Line($"guard restore: process={t.Process} title={t.Title} (gone)");
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

                Log.Line($"guard restore: process={t.Process} title={t.Title}");
            }

            _tracked.Clear();
        }
    }

    private static bool IsTopMost(IntPtr hwnd)
        => (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

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
