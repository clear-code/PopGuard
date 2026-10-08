using PopGuard;
using Xunit;

namespace PopGuard.Tests;

/// <summary>
/// Verifies the "act on a given window only once" dedup behavior via LogOnce's return value
/// (true = logged this time, false = suppressed as a repeat). This does not depend on log output,
/// so it holds under both Debug and Release.
/// </summary>
public class OncePerWindowLogTests
{
    [Fact]
    public void LogOnce_SameHandle_ReturnsTrueOnlyFirstTime()
    {
        var log = new OncePerWindowLog();
        var hwnd = new IntPtr(1234);

        Assert.True(log.LogOnce(hwnd, "first"));
        Assert.False(log.LogOnce(hwnd, "second"));
        Assert.False(log.LogOnce(hwnd, "third"));
    }

    [Fact]
    public void LogOnce_DifferentHandles_EachReturnsTrueOnce()
    {
        var log = new OncePerWindowLog();
        var a = new IntPtr(1);
        var b = new IntPtr(2);

        Assert.True(log.LogOnce(a, "a"));
        Assert.True(log.LogOnce(b, "b"));   // a different handle logs independently
        Assert.False(log.LogOnce(a, "a"));  // repeat of the first handle is suppressed
    }

    [Fact]
    public void LogOnce_AfterClear_LogsAgain()
    {
        var log = new OncePerWindowLog();
        var hwnd = new IntPtr(42);

        Assert.True(log.LogOnce(hwnd, "x"));
        log.Clear();
        Assert.True(log.LogOnce(hwnd, "x")); // allowed again after Clear
    }
}
