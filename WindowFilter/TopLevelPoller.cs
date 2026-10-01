namespace WindowFilter;

/// <summary>
/// Periodically scans top-level windows (enumerated via Win32 EnumWindows) and reports newly
/// appeared ones. It detects, without relying on events, popups that do not raise WindowOpenedEvent
/// and do not appear directly under the UIA root (e.g. Thunderbird's notifications).
/// It also evaluates each window with WindowFilter and pushes rule-matched ones to the back.
/// </summary>
internal sealed class TopLevelPoller
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(700);

    private readonly WindowReporter _reporter;
    private readonly WindowFilter _guard;
    private readonly HashSet<long> _known = new(); // single timer callback, so no locking needed
    // Referencing WinForms makes the name Timer ambiguous; be explicit about the thread-pool Timer.
    private System.Threading.Timer? _timer;

    public TopLevelPoller(WindowReporter reporter, WindowFilter guard)
    {
        _reporter = reporter;
        _guard = guard;
    }

    public void Start()
    {
        // Record windows already open at startup as "known" and do not report them.
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
            // Release/restore if the suppression expiry has passed.
            _guard.CheckExpiry();

            List<IntPtr> current = Win32Windows.EnumerateVisibleTopLevel();

            var present = new HashSet<long>();
            foreach (IntPtr h in current)
            {
                present.Add(h.ToInt64());

                // Evaluate every top-level window each tick and keep pushing rule-matched ones back
                // (so they stay down even if they re-assert topmost).
                _guard.Consider(h);

                // Report only newly appeared windows that are a meaningful size.
                if (_known.Add(h.ToInt64()) && Win32Windows.IsReasonableSize(h))
                {
                    _reporter.Report("toplevel", h, Win32Windows.ProcessName(h), Win32Windows.Title(h));
                }
            }

            // Drop closed windows from the known set (so a reopen is reported again).
            _known.IntersectWith(present);
        }
        catch
        {
            // Skip this round on enumeration failure.
        }
    }
}
