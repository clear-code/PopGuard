using System.Diagnostics;
using System.Text;

namespace PopGuard;

/// <summary>
/// Win32 helper for enumerating top-level windows and getting their info.
/// EnumWindows returns all top-level windows the OS knows about, so it also catches no-activate popups.
/// </summary>
internal static class Win32Windows
{
    // A top-level window smaller than this (in pixels) is treated as an auxiliary/hidden helper
    // window and not reported.
    private const int MinReasonableWidth = 40;
    private const int MinReasonableHeight = 20;

    // Win32 window class names are at most 256 characters.
    private const int MaxClassNameLength = 256;

    /// <summary>Enumerate the handles of visible top-level windows.</summary>
    public static List<IntPtr> EnumerateVisibleTopLevel()
    {
        var list = new List<IntPtr>();

        // EnumWindows is synchronous, so keeping the callback in a local is sufficient.
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

    /// <summary>Filter out very small / unsized windows (e.g. auxiliary hidden helper windows).</summary>
    public static bool IsReasonableSize(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT r))
        {
            return false;
        }
        return (r.Right - r.Left) >= MinReasonableWidth && (r.Bottom - r.Top) >= MinReasonableHeight;
    }

    public static string ProcessName(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return ProcessName((int)pid);
    }

    /// <summary>Process name for a known process id (callers that already have the pid).</summary>
    public static string ProcessName(int pid)
    {
        if (pid == 0)
        {
            return string.Empty;
        }

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

    public static string ClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(MaxClassNameLength);
        int n = NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return n > 0 ? sb.ToString() : string.Empty;
    }
}
