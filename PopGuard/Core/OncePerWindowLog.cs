namespace PopGuard;

/// <summary>
/// Logs a message the first time it sees a given window handle, then stays quiet for that handle.
/// Used to avoid logging the same "skipped" window on every poll tick. Thread-safe; the backing
/// set is capped so it cannot grow without bound (clearing it just allows a re-log later).
/// </summary>
internal sealed class OncePerWindowLog
{
    private const int MaxEntries = 256;

    private readonly object _lock = new();
    private readonly HashSet<IntPtr> _seen = new();

    /// <summary>
    /// Write <paramref name="message"/> only if this handle has not been logged before.
    /// Returns true if it logged (first time for this handle), false if it was suppressed as a repeat.
    /// </summary>
    public bool LogOnce(IntPtr hwnd, string message)
    {
        bool firstSeen;
        lock (_lock)
        {
            firstSeen = _seen.Add(hwnd);
            if (_seen.Count > MaxEntries)
            {
                _seen.Clear();
            }
        }
        if (firstSeen)
        {
            Logger.Line(message);
        }
        return firstSeen;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _seen.Clear();
        }
    }
}
