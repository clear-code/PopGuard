using System.Diagnostics;
using System.Text;
using static PopGuard.NativeMethods;

namespace PopGuard;

/// <summary>How a matched window is pushed out of the way.</summary>
internal enum HideMethod
{
    Bottom,   // Send to the back of the Z order (least disruptive, reliably restorable)
    Minimize, // Minimize
    Hide,     // Hide
}

/// <summary>
/// Pushes rule-matched windows to the back, tracks them, and always restores them on exit/failure.
/// Whether TOPMOST is required can be configured per rule (TopMostOnly).
///
/// The only Win32 APIs called are SetWindowPos / ShowWindowAsync.
/// It never kills processes or modifies files/registry.
/// </summary>
internal sealed class GuardEngine
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

    // HWNDs skipped as modal (recorded so we don't log the same one every tick). Guarded by _lock.
    private readonly HashSet<IntPtr> _skippedModal = new();

    // Suppression expiry (UTC). null means not suppressing. Touched by both the tray (UI thread)
    // and the poller (timer thread), so it is guarded by _stateLock.
    private readonly object _stateLock = new();
    private DateTime? _activeUntil;
    private bool _autoActive; // whether activation came from focus-session sync

    public GuardEngine(IReadOnlyList<TargetRule> rules) => _rules = rules;

    /// <summary>Whether suppression is currently active.</summary>
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

    /// <summary>Suppression expiry (UTC), or null if inactive. For display.</summary>
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

    /// <summary>Whether the current suppression was auto-activated by focus-session sync. For display.</summary>
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

    /// <summary>Activate suppression manually. A null <paramref name="duration"/> means unlimited.</summary>
    public void Activate(TimeSpan? duration)
    {
        lock (_stateLock)
        {
            _activeUntil = duration is { } d ? DateTime.UtcNow + d : DateTime.MaxValue;
            _autoActive = false; // manual action
        }
        Logger.Line(duration is { } dd
            ? $"guardEngine: suppression started ({dd.TotalMinutes:0} min)"
            : "guardEngine: suppression started (unlimited)");
    }

    /// <summary>Stop suppression and restore the windows that were pushed back.</summary>
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
            Logger.Line($"guardEngine: suppression ended ({reason})");
        }
    }

    /// <summary>
    /// React to focus (focus session) state changes to toggle auto-suppression.
    /// Only auto-started suppression is auto-released; manual suppression is respected.
    /// </summary>
    public void OnFocusChanged(bool focusActive)
    {
        bool started = false;
        bool ended = false;
        lock (_stateLock)
        {
            if (focusActive)
            {
                // If not already suppressing, auto-suppress until focus ends.
                if (_activeUntil is null)
                {
                    _activeUntil = DateTime.MaxValue;
                    _autoActive = true;
                    started = true;
                }
            }
            else if (_autoActive)
            {
                // Release only what we auto-started (leave manual suppression as is).
                _activeUntil = null;
                _autoActive = false;
                ended = true;
            }
        }

        if (started)
        {
            Logger.Line("guardEngine: suppression started (focus active)");
        }
        if (ended)
        {
            RestoreAll();
            Logger.Line("guardEngine: suppression ended (focus ended)");
        }
    }

    /// <summary>Release suppression if the expiry has passed. Called from the poller every tick.</summary>
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
            Logger.Line("guardEngine: suppression ended (time expired)");
        }
    }

    /// <summary>
    /// Evaluate a single window and, if a rule matches, push it to the back.
    /// If an already-handled window re-asserted topmost, push it back again (re-assert handling).
    /// </summary>
    public void Consider(IntPtr hwnd)
    {
        // Only act while suppression is active (enabled for a limited time from the tray).
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

        // Currently only TOPMOST windows are targeted.
        // The rule's topMostOnly is kept as config but not implemented (always treated as TOPMOST-only).
        if (!IsTopMost(hwnd))
        {
            return;
        }

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || (int)pid == _ownPid)
        {
            return;
        }

        // First narrow candidates by the cheap process name (title lookup is costly, so defer it).
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

        // Modal/dialog avoidance: pushing a modal dialog back can make the app look stuck, so skip it.
        // Err on the safe side for enterprise use: detect by behavior (owner disabled), style, and class.
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
                Logger.Line($"guardEngine: skip: process={process} title={title} (likely dialog: {modalReason})");
            }
            return;
        }

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
                Applied = rule.Hide,
                Process = process,
                Title = title,
            };
            _tracked[hwnd] = t;

            Demote(hwnd);
            SendBack(hwnd, t.Applied);
            Logger.Line($"guardEngine: demote: process={t.Process} title={t.Title} method={t.Applied}");
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

    /// <summary>Reverse the hide method, then (if it was TOPMOST) restore TOPMOST. Always runs on exit/failure.</summary>
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
    /// Decide whether a window is likely a modal/dialog, using several signals (behavior, style, class).
    /// For enterprise safety ("never push a real dialog back"), any single match marks it as excluded.
    /// <paramref name="reason"/> returns the matched signal (for diagnostics).
    ///
    /// - Owner disabled: during modality the owner's other windows are disabled (strongest behavioral signal)
    /// - WS_EX_DLGMODALFRAME: a dialog with a modal frame
    /// - Class "#32770": the standard Win32 dialog box (from MessageBox / DialogBox)
    /// </summary>
    private static bool IsLikelyModal(IntPtr hwnd, out string reason)
    {
        IntPtr owner = GetWindow(hwnd, GW_OWNER);
        if (owner != IntPtr.Zero && !IsWindowEnabled(owner))
        {
            reason = "owner disabled";
            return true;
        }

        if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_DLGMODALFRAME) != 0)
        {
            reason = "modal frame (WS_EX_DLGMODALFRAME)";
            return true;
        }

        if (ClassName(hwnd) == "#32770")
        {
            reason = "standard dialog class (#32770)";
            return true;
        }

        reason = string.Empty;
        return false;
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
