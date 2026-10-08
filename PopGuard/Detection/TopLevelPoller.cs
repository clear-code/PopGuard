namespace PopGuard;

/// <summary>
/// Periodically scans top-level windows (enumerated via Win32 EnumWindows) and reports newly
/// appeared ones. It detects, without relying on events, popups that do not raise WindowOpenedEvent
/// and do not appear directly under the UIA root (e.g. Thunderbird's notifications).
/// It also evaluates each window with GuardEngine and pushes rule-matched ones to the back.
/// </summary>
internal sealed class TopLevelPoller
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(700);

    private readonly WindowReporter _reporter;
    private readonly GuardEngine _guardEngine;
    private readonly NotificationStateWatcher _notifState = new(); // diagnostic: DND/quiet-time logging
    private readonly HashSet<long> _known = new(); // only touched inside Tick, which never overlaps (see _running)
    // Referencing WinForms makes the name Timer ambiguous; be explicit about the thread-pool Timer.
    private System.Threading.Timer? _timer;
    private int _running; // 1 while a tick is in progress; prevents overlapping callbacks

    public TopLevelPoller(WindowReporter reporter, GuardEngine guardEngine)
    {
        _reporter = reporter;
        _guardEngine = guardEngine;
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
        // System.Threading.Timer does not prevent reentrancy: if one tick runs longer than the
        // interval, the next would start on another thread. Skip overlapping ticks so _known and
        // the watchers are only ever touched by a single thread at a time.
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;
        }

        try
        {
            // Release/restore if the suppression expiry has passed.
            _guardEngine.CheckExpiry();

            // Diagnostic: log notification-state changes (to confirm DND -> QUNS_QUIET_TIME).
            _notifState.Poll();

            List<IntPtr> current = Win32Windows.EnumerateVisibleTopLevel();

            var present = new HashSet<long>();
            foreach (IntPtr h in current)
            {
                present.Add(h.ToInt64());

                // Evaluate every top-level window each tick and keep pushing rule-matched ones back
                // (so they stay down even if they re-assert topmost).
                _guardEngine.Consider(h);

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
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
