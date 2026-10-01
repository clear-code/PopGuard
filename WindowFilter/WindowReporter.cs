namespace WindowFilter;

/// <summary>
/// Reports a detected window as a single log line. Polling runs on a separate (timer) thread,
/// so the dedup state is guarded by a lock.
/// </summary>
internal sealed class WindowReporter
{
    // Dedup window: suppress repeated detections of the same window within this interval.
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMilliseconds(1500);

    private readonly object _lock = new();
    private readonly Dictionary<string, DateTime> _recent = new();

    /// <summary>Report a window picked up via Win32 (EnumWindows). The key is the HWND.</summary>
    public void Report(string kind, IntPtr hwnd, string process, string title)
    {
        string key = "h" + hwnd.ToInt64();
        if (key == "h0" || IsDuplicate(key))
        {
            return;
        }

        Logger.Line($"window {kind}: process={process} title={title}");
    }

    private bool IsDuplicate(string key)
    {
        DateTime now = DateTime.UtcNow;

        lock (_lock)
        {
            if (_recent.TryGetValue(key, out DateTime last) && now - last < DedupWindow)
            {
                _recent[key] = now;
                return true;
            }

            _recent[key] = now;

            if (_recent.Count > 256)
            {
                foreach (string k in _recent
                             .Where(kv => now - kv.Value >= DedupWindow)
                             .Select(kv => kv.Key).ToList())
                {
                    _recent.Remove(k);
                }
            }

            return false;
        }
    }
}
