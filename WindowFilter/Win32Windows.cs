using System.Diagnostics;
using System.Text;

namespace WindowFilter;

/// <summary>
/// Win32 でトップレベルウィンドウを列挙・情報取得するヘルパー。
/// EnumWindows は OS の全トップレベルウィンドウを返すため、非アクティブ化ポップアップや
/// トースト（Thunderbird の通知など）も取りこぼさない。
/// </summary>
internal static class Win32Windows
{
    /// <summary>可視のトップレベルウィンドウのハンドルを列挙する。</summary>
    public static List<IntPtr> EnumerateVisibleTopLevel()
    {
        var list = new List<IntPtr>();

        // EnumWindows は同期呼び出しなので、コールバックはローカルで保持すれば十分。
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd))
            {
                list.Add(hwnd);
            }
            return true;
        }, IntPtr.Zero);

        return list;
    }

    /// <summary>ごく小さい／サイズの取れないウィンドウ（補助的な隠れ窓など）を除くための判定。</summary>
    public static bool IsReasonableSize(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT r))
        {
            return false;
        }
        return (r.Right - r.Left) >= 40 && (r.Bottom - r.Top) >= 20;
    }

    public static string ProcessName(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
        {
            return string.Empty;
        }

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string Title(IntPtr hwnd)
    {
        int len = NativeMethods.GetWindowTextLength(hwnd);
        if (len <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(len + 2);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
