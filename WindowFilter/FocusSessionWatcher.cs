namespace WindowFilter;

/// <summary>
/// Subscribes to the Windows 11 focus (focus session) state and relays changes to <see cref="WindowFilter"/>.
/// When auto-suppress is enabled, suppression is turned on while focus is active and off when it ends
/// (manual actions are respected). Manual "Do Not Disturb" alone does not change IsFocusActive, so it is not covered.
/// </summary>
internal sealed class FocusSessionWatcher
{
    private readonly WindowFilter _windowFilter;
    private readonly bool _autoSuppress;

    // Hold the reference so the event subscription is not collected by GC.
    private Windows.UI.Shell.FocusSessionManager? _manager;

    public FocusSessionWatcher(WindowFilter windowFilter, bool autoSuppress)
    {
        _windowFilter = windowFilter;
        _autoSuppress = autoSuppress;
    }

    /// <summary>Start subscribing. Must be called on an STA thread with a message pump.</summary>
    public void Start()
    {
        // FocusSessionManager was added in Windows 11 22H2 (22621). The type is absent on older OSes,
        // so guard by OS version before touching it (skip cleanly instead of relying on exceptions).
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            Logger.Line("focus-session: not supported on this Windows (< 22H2/22621). Running without focus sync.");
            return;
        }

        try
        {
            if (!Windows.UI.Shell.FocusSessionManager.IsSupported)
            {
                Logger.Line("focus-session: not supported on this device (IsSupported=false)");
                return;
            }

            _manager = Windows.UI.Shell.FocusSessionManager.GetDefault();
            bool active = _manager.IsFocusActive;
            Logger.Line($"focus-session: supported, IsFocusActive={active}, autoSuppress={_autoSuppress}");

            _manager.IsFocusActiveChanged += OnFocusActiveChanged;

            // If focus is already active at startup, reflect it.
            if (_autoSuppress && active)
            {
                _windowFilter.OnFocusChanged(true);
            }
        }
        catch (Exception ex)
        {
            Logger.Line("focus-session: failed to query/subscribe: " + ex.Message);
        }
    }

    private void OnFocusActiveChanged(Windows.UI.Shell.FocusSessionManager sender, object args)
    {
        bool active = sender.IsFocusActive;
        Logger.Line($"focus-session: IsFocusActive={active}");

        if (_autoSuppress)
        {
            _windowFilter.OnFocusChanged(active);
        }
    }
}
