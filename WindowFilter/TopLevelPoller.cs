namespace WindowFilter;

/// <summary>
/// トップレベルウィンドウ（Win32 の EnumWindows で列挙）を定期的に走査し、
/// 新しく現れたものを報告する。WindowOpenedEvent を上げず、UIA のルート直下にも
/// 現れないポップアップ（Thunderbird の通知など）を、イベントに頼らず検出する。
/// あわせて各ウィンドウを WindowFilter で評価し、ルール一致のものは裏へ送る。
/// </summary>
internal sealed class TopLevelPoller
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(700);

    private readonly WindowReporter _reporter;
    private readonly WindowFilter _guard;
    private readonly HashSet<long> _known = new(); // タイマーコールバック単一なので排他不要
    // WinForms を参照しているため Timer 名が衝突する。スレッドプールの Timer を明示する。
    private System.Threading.Timer? _timer;

    public TopLevelPoller(WindowReporter reporter, WindowFilter guard)
    {
        _reporter = reporter;
        _guard = guard;
    }

    public void Start()
    {
        // 起動時点で既に開いているウィンドウは「既知」として記録し、報告はしない。
        foreach (IntPtr h in Win32Windows.EnumerateVisibleTopLevel())
        {
            _known.Add(h.ToInt64());
        }

        _timer = new System.Threading.Timer(_ => Tick(), null, Interval, Interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick()
    {
        try
        {
            // 抑止の有効期限が切れていれば解除・復元する。
            _guard.CheckExpiry();

            List<IntPtr> current = Win32Windows.EnumerateVisibleTopLevel();

            var present = new HashSet<long>();
            foreach (IntPtr h in current)
            {
                present.Add(h.ToInt64());

                // 毎回すべてのトップレベルを評価し、ルール一致の窓は裏へ送り続ける
                // （相手が最前面を立て直しても押さえ込む）
                _guard.Consider(h);

                // 新規に出現したもののうち、意味のある大きさのものだけ報告する。
                if (_known.Add(h.ToInt64()) && Win32Windows.IsReasonableSize(h))
                {
                    _reporter.Report("toplevel", h, Win32Windows.ProcessName(h), Win32Windows.Title(h));
                }
            }

            // 閉じたウィンドウは既知集合から外す（再度開いたら改めて報告するため）。
            _known.IntersectWith(present);
        }
        catch
        {
            // 列挙失敗時はこの回をスキップ。
        }
    }
}
