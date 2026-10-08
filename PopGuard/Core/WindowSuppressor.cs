using static PopGuard.NativeMethods;

namespace PopGuard;

/// <summary>
/// Pushes individual windows out of the way and tracks them so they can always be restored.
/// The only Win32 APIs used are SetWindowPos / ShowWindowAsync — it never kills processes or
/// modifies files/registry. Thread-safe.
/// </summary>
internal sealed class WindowSuppressor
{
    private sealed class Tracked
    {
        public IntPtr Hwnd;
        public bool WasTopMost;
        public HideMethod Applied;
        public string Process = string.Empty;
        public string Title = string.Empty;
    }

    private readonly object _lock = new();
    private readonly Dictionary<IntPtr, Tracked> _tracked = new();

    /// <summary>
    /// Push a matched window back. If it is already tracked it only re-applies the send-back
    /// (handling a window that re-asserted topmost); otherwise it clears topmost, applies the hide
    /// method, and starts tracking it for restore.
    /// </summary>
    public void Apply(IntPtr hwnd, string process, string title, HideMethod method)
    {
        lock (_lock)
        {
            if (_tracked.TryGetValue(hwnd, out Tracked? known))
            {
                // Already tracked. It re-asserted topmost, so push it back again without extra logging.
                SendBack(hwnd, known.Applied);
                return;
            }

            var t = new Tracked
            {
                Hwnd = hwnd,
                WasTopMost = true, // TOPMOST-only target, so always true; restored to TOPMOST later.
                Applied = method,
                Process = process,
                Title = title,
            };
            _tracked[hwnd] = t;

            Demote(hwnd);
            SendBack(hwnd, t.Applied);
            Logger.Line($"guardEngine: demote: process={t.Process} title={t.Title} method={t.Applied}");
        }
    }

    /// <summary>Reverse the hide method, then (if it was TOPMOST) restore TOPMOST. Always runs on exit/failure.</summary>
    public void RestoreAll()
    {
        lock (_lock)
        {
            foreach (Tracked t in _tracked.Values)
            {
                if (!IsWindow(t.Hwnd))
                {
                    Logger.Line($"guardEngine: restore: process={t.Process} title={t.Title} (gone)");
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

                Logger.Line($"guardEngine: restore: process={t.Process} title={t.Title}");
            }

            _tracked.Clear();
        }
    }

    /// <summary>Clear only the topmost attribute.</summary>
    private static bool Demote(IntPtr hwnd)
        => SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);

    /// <summary>
    /// Explicitly push the window to the back. HWND_NOTOPMOST only moves it to the top of the
    /// non-topmost group, so without this the appearance would not change.
    /// </summary>
    private static bool SendBack(IntPtr hwnd, HideMethod method) => method switch
    {
        HideMethod.Minimize => ShowWindowAsync(hwnd, SW_MINIMIZE),
        HideMethod.Hide => ShowWindowAsync(hwnd, SW_HIDE),
        _ => SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS),
    };
}
