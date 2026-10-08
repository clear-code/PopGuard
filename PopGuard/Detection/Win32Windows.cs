using System.Diagnostics;
using System.Text;

namespace PopGuard;

/// <summary>
/// Win32 helper for enumerating top-level windows and getting their info.
/// EnumWindows returns all top-level windows the OS knows about, so it also catches no-activate popups.
/// </summary>
internal static class Win32Windows
{
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
