using PopGuard;
using Xunit;

namespace PopGuard.Tests;

public class TargetRuleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_EmptyProcess_IsInvalid(string? process)
    {
        var rule = TargetRule.TryCreate(new RuleEntry { Process = process }, out string? reason);
        Assert.Null(rule);
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("?")]
    [InlineData("**")]
    public void TryCreate_WildcardOnlyProcess_MatchesAnyProcess(string process)
    {
        var rule = TargetRule.TryCreate(new RuleEntry { Process = process }, out string? reason);
        Assert.NotNull(rule);
        Assert.Null(reason);
        // "*" means "suppress every popup": any process name matches.
        Assert.True(rule!.MatchesProcess("ToastTester"));
        Assert.True(rule.MatchesProcess("anything.else"));
        Assert.True(rule.Matches("whatever", "AnyClass", "Any Title"));
    }

    [Fact]
    public void TryCreate_ValidProcess_MatchesProcessCaseInsensitively()
    {
        var rule = TargetRule.TryCreate(new RuleEntry { Process = "ToastTester" }, out _);
        Assert.NotNull(rule);
        Assert.True(rule!.MatchesProcess("ToastTester"));
        Assert.True(rule.MatchesProcess("toasttester"));
        Assert.False(rule.MatchesProcess("Other"));
    }

    [Fact]
    public void Matches_EmptyTitleAndClass_MatchAnything()
    {
        var rule = TargetRule.TryCreate(new RuleEntry { Process = "app", Title = "", Class = "" }, out _)!;
        Assert.True(rule.Matches("app", "AnyClass", "Any Title"));
        Assert.False(rule.Matches("other", "AnyClass", "Any Title"));
    }

    [Fact]
    public void Matches_TitleWildcard_FiltersOnTitle()
    {
        var rule = TargetRule.TryCreate(new RuleEntry { Process = "app", Title = "*Toast*" }, out _)!;
        Assert.True(rule.Matches("app", "", "My Toast Window"));
        Assert.False(rule.Matches("app", "", "Something Else"));
    }

    [Fact]
    public void Matches_ClassWildcard_FiltersOnClass()
    {
        var rule = TargetRule.TryCreate(new RuleEntry { Process = "app", Class = "#32770" }, out _)!;
        Assert.True(rule.Matches("app", "#32770", "title"));
        Assert.False(rule.Matches("app", "OtherClass", "title"));
    }

    [Theory]
    [InlineData("Bottom", "Bottom")]
    [InlineData("Minimize", "Minimize")]
    [InlineData("minimize", "Minimize")]
    [InlineData("Hide", "Hide")]
    [InlineData("HIDE", "Hide")]
    [InlineData("", "Bottom")]
    [InlineData(null, "Bottom")]
    [InlineData("unknown", "Bottom")]
    public void TryCreate_ParsesHideMethod(string? hide, string expected)
    {
        var rule = TargetRule.TryCreate(new RuleEntry { Process = "app", Hide = hide }, out _)!;
        Assert.Equal(expected, rule.Hide.ToString());
    }

    [Fact]
    public void TryCreate_KeepsTopMostOnlyFlag()
    {
        var on = TargetRule.TryCreate(new RuleEntry { Process = "app", TopMostOnly = true }, out _)!;
        var off = TargetRule.TryCreate(new RuleEntry { Process = "app", TopMostOnly = false }, out _)!;
        Assert.True(on.TopMostOnly);
        Assert.False(off.TopMostOnly);
    }
}
