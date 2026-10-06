using PopGuard;
using Xunit;

namespace PopGuard.Tests;

public class GuardEngineTests
{
    private static GuardEngine NewEngine() => new(new List<TargetRule>());

    [Fact]
    public void New_IsInactive()
    {
        var e = NewEngine();
        Assert.False(e.IsActive);
        Assert.Null(e.ActiveUntilUtc);
        Assert.False(e.IsAutoActive);
    }

    [Fact]
    public void Activate_WithDuration_SetsFiniteExpiry()
    {
        var e = NewEngine();
        var before = DateTime.UtcNow;
        e.Activate(TimeSpan.FromMinutes(30));

        Assert.True(e.IsActive);
        Assert.False(e.IsAutoActive);
        Assert.NotNull(e.ActiveUntilUtc);
        Assert.InRange(e.ActiveUntilUtc!.Value,
            before.AddMinutes(30), DateTime.UtcNow.AddMinutes(30).AddSeconds(5));
    }

    [Fact]
    public void Activate_Null_MeansUnlimited()
    {
        var e = NewEngine();
        e.Activate(null);
        Assert.True(e.IsActive);
        Assert.Equal(DateTime.MaxValue, e.ActiveUntilUtc);
    }

    [Fact]
    public void Deactivate_Clears()
    {
        var e = NewEngine();
        e.Activate(null);
        e.Deactivate("test");
        Assert.False(e.IsActive);
        Assert.Null(e.ActiveUntilUtc);
    }

    [Fact]
    public void OnFocusChanged_True_AutoActivates()
    {
        var e = NewEngine();
        e.OnFocusChanged(true);
        Assert.True(e.IsActive);
        Assert.True(e.IsAutoActive);

        e.OnFocusChanged(false);
        Assert.False(e.IsActive);
        Assert.False(e.IsAutoActive);
    }

    [Fact]
    public void FocusEnd_DoesNotCancelManualSuppression()
    {
        var e = NewEngine();
        e.Activate(TimeSpan.FromHours(1)); // manual
        e.OnFocusChanged(false);           // focus ended
        Assert.True(e.IsActive);           // manual kept
        Assert.False(e.IsAutoActive);
    }

    [Fact]
    public void FocusStart_WhileManualActive_StaysManual()
    {
        var e = NewEngine();
        e.Activate(TimeSpan.FromHours(1)); // manual
        e.OnFocusChanged(true);            // focus starts, but already active
        Assert.True(e.IsActive);
        Assert.False(e.IsAutoActive);      // not switched to auto
    }

    [Fact]
    public void CheckExpiry_ReleasesAfterExpiry()
    {
        var e = NewEngine();
        e.Activate(TimeSpan.FromMilliseconds(10));
        Thread.Sleep(60);
        e.CheckExpiry();
        Assert.False(e.IsActive);
    }

    [Fact]
    public void CheckExpiry_KeepsActiveBeforeExpiry()
    {
        var e = NewEngine();
        e.Activate(TimeSpan.FromHours(1));
        e.CheckExpiry();
        Assert.True(e.IsActive);
    }
}
