using System.Diagnostics;
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
/// Decides which windows to suppress and drives the pieces that do it: it gates on the suppression
/// state (<see cref="SuppressionState"/>), classifies each window (rule match, modal / shell / exclusion
/// skips), and hands matched windows to <see cref="WindowSuppressor"/>. Suppression-state members are
/// forwarded so callers keep a single entry point.
/// </summary>
internal sealed class GuardEngine
{
    private readonly IReadOnlyList<TargetRule> _rules;
    private readonly bool _excludeSystemWindows;
    private readonly int _ownPid = Process.GetCurrentProcess().Id;

    private readonly SuppressionState _state = new();
    private readonly WindowSuppressor _suppressor = new();

    // "Skip" reasons each log a given window only once (not every tick). Each is independently synchronized.
    private readonly OncePerWindowLog _modalSkips = new();      // skipped as a modal/dialog
    private readonly OncePerWindowLog _systemSkips = new();     // skipped as a Windows shell window
    private readonly OncePerWindowLog _excludedSkips = new();   // skipped by an exclusion rule

    // Window classes owned by the Windows shell that must never be touched. The taskbar and desktop
    // are TOPMOST, so a broad rule (e.g. process "*") would otherwise demote/hide them — on Windows 10
    // this makes the taskbar disappear. Matching is case-insensitive.
    private static readonly HashSet<string> SystemWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",            // primary taskbar
        "Shell_SecondaryTrayWnd",   // taskbar on secondary monitors
        "Progman",                  // desktop (Program Manager)
        "WorkerW",                  // desktop wallpaper host
        "NotifyIconOverflowWindow", // notification-area overflow flyout
    };

    public GuardEngine(IReadOnlyList<TargetRule> rules, bool excludeSystemWindows = true)
    {
        _rules = rules;
        _excludeSystemWindows = excludeSystemWindows;

        // When suppression is released (manual stop, expiry, or the last auto-source turning off),
        // restore the windows that were pushed back.
        _state.Released += DoRestore;
    }

    // --- Suppression state (forwarded to SuppressionState) ---

    public bool IsActive => _state.IsActive;
    public DateTime? ActiveUntilUtc => _state.ActiveUntilUtc;
    public bool IsAutoActive => _state.IsAutoActive;
    public AutoSuppressReason AutoReason => _state.AutoReason;

    /// <summary>Activate suppression manually. A null <paramref name="duration"/> means unlimited.</summary>
    public void Activate(TimeSpan? duration) => _state.Activate(duration);

    /// <summary>Stop suppression and restore the windows that were pushed back.</summary>
    public void Deactivate(string reason) => _state.Deactivate(reason);

    /// <summary>React to a Windows 11 focus session starting/ending.</summary>
    public void OnFocusChanged(bool focusActive) => _state.SetAutoSource(AutoSource.Focus, focusActive, "focus");

    /// <summary>React to the microphone starting/stopping use (e.g. a call/meeting).</summary>
    public void OnMicrophoneChanged(bool micInUse) => _state.SetAutoSource(AutoSource.Microphone, micInUse, "microphone");

    /// <summary>Release suppression if the expiry has passed. Called from the poller every tick.</summary>
    public void CheckExpiry() => _state.CheckExpiry();

    /// <summary>Restore all pushed-back windows. Safe to call on exit/failure.</summary>
    public void RestoreAll() => DoRestore();

    private void DoRestore()
    {
        _suppressor.RestoreAll();
        _modalSkips.Clear();
        _systemSkips.Clear();
        _excludedSkips.Clear();
    }

    /// <summary>
    /// Evaluate a single window and, if a rule matches, push it to the back.
    /// If an already-handled window re-asserted topmost, push it back again (re-assert handling).
    /// </summary>
    public void Consider(IntPtr hwnd)
    {
        // Only act while suppression is active (enabled for a limited time from the tray).
        if (!_state.IsActive)
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
        string process = Win32Windows.ProcessName((int)pid);
        if (!AnyRuleMatchesProcess(process))
        {
            return;
        }

        string className = Win32Windows.ClassName(hwnd);
        string title = Win32Windows.Title(hwnd);

        // Never touch the Windows shell's own windows (taskbar, desktop, tray overflow), even when a
        // broad rule matches. Demoting/hiding the taskbar would make it vanish (seen on Windows 10).
        if (_excludeSystemWindows && IsSystemShellWindow(className))
        {
            _systemSkips.LogOnce(hwnd, $"guardEngine: skip: process={process} class={className} (system shell window)");
            return;
        }

        TargetRule? rule = FindRule(process, className, title);
        if (rule is null)
        {
            return;
        }

        // Exclusion rule ("never suppress"). The first matching rule wins, so an exclude rule placed
        // above broader rules carves out exceptions.
        if (rule.Exclude)
        {
            _excludedSkips.LogOnce(hwnd, $"guardEngine: skip: process={process} title={title} (excluded by rule)");
            return;
        }

        // Modal/dialog avoidance: pushing a modal dialog back can make the app look stuck, so skip it.
        // Err on the safe side for enterprise use: detect by behavior (owner disabled), style, and class.
        if (IsLikelyModal(hwnd, out string modalReason))
        {
            _modalSkips.LogOnce(hwnd, $"guardEngine: skip: process={process} title={title} (likely dialog: {modalReason})");
            return;
        }

        _suppressor.Apply(hwnd, process, title, rule.Hide);
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

    private static bool IsTopMost(IntPtr hwnd)
        => (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

    /// <summary>Whether the window class is a Windows shell window that must never be touched (taskbar, desktop, …).</summary>
    internal static bool IsSystemShellWindow(string className)
        => SystemWindowClasses.Contains(className ?? string.Empty);

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

        if (Win32Windows.ClassName(hwnd) == "#32770")
        {
            reason = "standard dialog class (#32770)";
            return true;
        }

        reason = string.Empty;
        return false;
    }
}
