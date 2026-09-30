namespace WindowFilter;

/// <summary>
/// 検知したウィンドウを 1 行で報告する。ポーリングは別スレッド（Timer）で動くため、
/// 重複抑制の状態はロックで保護する。
/// </summary>
internal sealed class WindowReporter
{
    // 同一ウィンドウが短時間に連続検知された場合の抑制時間。
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMilliseconds(1500);

    private readonly object _lock = new();
    private readonly Dictionary<string, DateTime> _recent = new();

    /// <summary>Win32（EnumWindows 経路）で拾ったウィンドウを報告する。キーは HWND。</summary>
    public void Report(string kind, IntPtr hwnd, string process, string title)
    {
        string key = "h" + hwnd.ToInt64();
        if (key == "h0" || IsDuplicate(key))
        {
            return;
        }

        Log.Line($"window {kind}: process={process} title={title}");
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
