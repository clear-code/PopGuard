using static PopGuard.NativeMethods;

namespace PopGuard;

/// <summary>
/// Listens for window create/show events (SetWinEventHook) and immediately evaluates the new
/// top-level window with PopGuard, so matched popups are pushed back with minimal visible time.
/// Complements the polling safety net (events can miss some cases; the poller catches stragglers).
/// Must be started on an STA thread that runs a message loop (OUTOFCONTEXT events are delivered there).
/// </summary>
internal sealed class WindowEventWatcher
{
    private readonly GuardEngine _windowFilter;
    private WinEventDelegate? _proc; // keep a reference so the callback is not collected by GC
    private IntPtr _hook;

    public WindowEventWatcher(GuardEngine windowFilter) => _windowFilter = windowFilter;

    public void Start()
    {
        _proc = OnWinEvent;
        // One hook over CREATE..SHOW (DESTROY in between is filtered out in the callback).
        _hook = SetWinEventHook(
            EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW, IntPtr.Zero,
            _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        Logger.Line(_hook != IntPtr.Zero
            ? "win-event: hook installed (immediate detection)"
            : "win-event: SetWinEventHook failed; relying on polling only");
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        // Only window-object create/show for top-level windows (ignore controls/sub-objects).
        if ((eventType != EVENT_OBJECT_CREATE && eventType != EVENT_OBJECT_SHOW)
            || idObject != OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
        {
            return;
        }

        // Top-level only (the owning root window is itself).
        if (GetAncestor(hwnd, GA_ROOT) != hwnd)
        {
            return;
        }

        try
        {
            _windowFilter.Consider(hwnd);
        }
        catch
        {
            // Never let a hook callback throw.
        }
    }
}
