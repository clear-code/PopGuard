using PopGuard;
using Xunit;

namespace PopGuard.Tests;

public class WildcardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*")]
    [InlineData("?")]
    [InlineData("**")]
    [InlineData("*?*")]
    public void Compile_EmptyOrWildcardOnly_ReturnsNull(string? pattern)
    {
        // "no filter" (null) — these must not match-anything.
        Assert.Null(Wildcard.Compile(pattern));
    }

    [Fact]
    public void Compile_Literal_IsWholeMatchAndCaseInsensitive()
    {
        var re = Wildcard.Compile("Notepad");
        Assert.NotNull(re);
        Assert.Matches(re!, "Notepad");
        Assert.Matches(re!, "notepad");   // case-insensitive
        Assert.Matches(re!, "NOTEPAD");
        Assert.DoesNotMatch(re!, "Notepad2"); // whole match (not prefix)
        Assert.DoesNotMatch(re!, "MyNotepad");
    }

    [Fact]
    public void Compile_Star_MatchesZeroOrMore()
    {
        var re = Wildcard.Compile("a*c")!;
        Assert.Matches(re, "ac");
        Assert.Matches(re, "abc");
        Assert.Matches(re, "aXYZc");
        Assert.DoesNotMatch(re, "ab");
        Assert.DoesNotMatch(re, "abcd");
    }

    [Fact]
    public void Compile_Question_MatchesExactlyOne()
    {
        var re = Wildcard.Compile("a?c")!;
        Assert.Matches(re, "abc");
        Assert.DoesNotMatch(re, "ac");
        Assert.DoesNotMatch(re, "abbc");
    }

    [Fact]
    public void Compile_SurroundingStars_MatchesContains()
    {
        var re = Wildcard.Compile("*通知*")!;
        Assert.Matches(re, "新着通知があります");
        Assert.Matches(re, "通知");
        Assert.DoesNotMatch(re, "お知らせ");
    }

    [Fact]
    public void Compile_EscapesRegexMetacharacters()
    {
        var re = Wildcard.Compile("a.b+c")!;
        Assert.Matches(re, "a.b+c");
        Assert.DoesNotMatch(re, "aXbXc"); // '.' is literal, not "any char"
    }
}
