namespace PopGuard;

/// <summary>An external condition that can auto-start suppression (and auto-release it when it ends).</summary>
internal enum AutoSource
{
    Focus,      // A Windows 11 focus session is active
    Microphone, // The microphone is in use (e.g. during a call/meeting)
}

/// <summary>Why suppression is currently auto-active. For tray display.</summary>
internal enum AutoSuppressReason
{
    None,
    Focus,
    Microphone,
    Multiple,
}

/// <summary>
/// The suppression on/off state machine: whether suppression is active, until when, whether it was
/// started manually or by an auto-source (focus session / microphone), and which sources are on.
/// Thread-safe. When suppression becomes released it raises <see cref="Released"/> (outside the lock)
/// so the owner can restore the windows that were pushed back.
///
/// Log lines use the stable "guardEngine:" tag (a documented log contract), regardless of this type's name.
/// </summary>
internal sealed class SuppressionState
{
    private readonly object _lock = new();
    private DateTime? _activeUntil;   // null = not suppressing
    private bool _autoActive;         // whether the current activation was auto-started (by an AutoSource)
    private readonly HashSet<AutoSource> _autoSources = new(); // sources currently "on"; mirrors watcher events

    /// <summary>Raised (outside the lock) right after suppression is released, so windows can be restored.</summary>
    public event Action? Released;

    /// <summary>Whether suppression is currently active.</summary>
    public bool IsActive
    {
        get
        {
            lock (_lock)
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
            lock (_lock)
            {
                return _activeUntil;
            }
        }
    }

    /// <summary>Whether the current suppression was auto-activated (focus session / microphone). For display.</summary>
    public bool IsAutoActive
    {
        get
        {
            lock (_lock)
            {
                return _autoActive && _activeUntil.HasValue;
            }
        }
    }

    /// <summary>Which auto-source(s) are driving the current auto-suppression. For display.</summary>
    public AutoSuppressReason AutoReason
    {
        get
        {
            lock (_lock)
            {
                if (!_autoActive || !_activeUntil.HasValue || _autoSources.Count == 0)
                {
                    return AutoSuppressReason.None;
                }
                if (_autoSources.Count > 1)
                {
                    return AutoSuppressReason.Multiple;
                }
                return _autoSources.Contains(AutoSource.Microphone)
                    ? AutoSuppressReason.Microphone
                    : AutoSuppressReason.Focus;
            }
        }
    }

    /// <summary>Activate suppression manually. A null <paramref name="duration"/> means unlimited.</summary>
    public void Activate(TimeSpan? duration)
    {
        lock (_lock)
        {
            _activeUntil = duration is { } d ? DateTime.UtcNow + d : DateTime.MaxValue;
            _autoActive = false; // manual action
        }
        Logger.Line(duration is { } dd
            ? $"guardEngine: suppression started ({dd.TotalMinutes:0} min)"
            : "guardEngine: suppression started (unlimited)");
    }

    /// <summary>Stop suppression. Raises <see cref="Released"/> if it was active.</summary>
    public void Deactivate(string reason)
    {
        bool wasActive;
        lock (_lock)
        {
            wasActive = _activeUntil.HasValue;
            _activeUntil = null;
            _autoActive = false;
        }
        if (wasActive)
        {
            Released?.Invoke();
            Logger.Line($"guardEngine: suppression ended ({reason})");
        }
    }

    /// <summary>
    /// Toggle an auto-suppress source on/off. Auto-suppression starts when the first source turns on
    /// (unless suppression is already active, e.g. manual) and is auto-released only once every
    /// auto-started source is off. Manual suppression is always respected and never auto-released.
    /// </summary>
    public void SetAutoSource(AutoSource source, bool active, string name)
    {
        bool started = false;
        bool ended = false;
        lock (_lock)
        {
            if (active)
            {
                if (!_autoSources.Add(source))
                {
                    return; // already on; no change
                }
                // Auto-start only if nothing is suppressing yet (don't override manual suppression).
                if (_activeUntil is null)
                {
                    _activeUntil = DateTime.MaxValue;
                    _autoActive = true;
                    started = true;
                }
            }
            else
            {
                if (!_autoSources.Remove(source))
                {
                    return; // was not on; no change
                }
                // Release only what we auto-started, and only once every source is off.
                if (_autoActive && _autoSources.Count == 0)
                {
                    _activeUntil = null;
                    _autoActive = false;
                    ended = true;
                }
            }
        }

        if (started)
        {
            Logger.Line($"guardEngine: suppression started (auto: {name})");
        }
        if (ended)
        {
            Released?.Invoke();
            Logger.Line($"guardEngine: suppression ended (auto: {name} ended)");
        }
    }

    /// <summary>Release suppression if the expiry has passed. Called from the poller every tick.</summary>
    public void CheckExpiry()
    {
        bool expired;
        lock (_lock)
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
            Released?.Invoke();
            Logger.Line("guardEngine: suppression ended (time expired)");
        }
    }
}
