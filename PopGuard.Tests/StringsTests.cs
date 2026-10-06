using System.Globalization;
using PopGuard.Resources;
using Xunit;

namespace PopGuard.Tests;

public class StringsTests : IDisposable
{
    public void Dispose() => Strings.Culture = null; // reset shared state after each test

    [Fact]
    public void English_ReturnsNeutralValues()
    {
        Strings.Culture = new CultureInfo("en");
        Assert.Equal("Suppress windows", Strings.MenuSuppress);
        Assert.Equal("Stop suppression", Strings.MenuStop);
        Assert.Equal("Not suppressing", Strings.StatusNotSuppressing);
        Assert.Equal("Unlimited", Strings.DurUnlimited);
    }

    [Fact]
    public void Japanese_ReturnsJaSatelliteValues()
    {
        Strings.Culture = new CultureInfo("ja");
        Assert.Equal("ウィンドウを抑止する", Strings.MenuSuppress);
        Assert.Equal("抑止を停止", Strings.MenuStop);
        Assert.Equal("抑止していません", Strings.StatusNotSuppressing);
        Assert.Equal("無制限", Strings.DurUnlimited);
    }

    [Fact]
    public void StatusRemaining_FormatsArgument()
    {
        Strings.Culture = new CultureInfo("en");
        Assert.Equal("Suppressing (1:23 left)", Strings.StatusRemaining("1:23"));

        Strings.Culture = new CultureInfo("ja");
        Assert.Equal("抑止中（残り 1:23）", Strings.StatusRemaining("1:23"));
    }

    [Theory]
    [InlineData("ja", "ja")]
    [InlineData("en", "en")]
    [InlineData("JA", "ja")]
    public void ApplyOverride_SetsCulture(string input, string expectedTwoLetter)
    {
        Strings.ApplyOverride(input);
        Assert.NotNull(Strings.Culture);
        Assert.Equal(expectedTwoLetter, Strings.Culture!.TwoLetterISOLanguageName);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData(null)]
    [InlineData("fr")]
    public void ApplyOverride_AutoOrUnknown_ClearsCulture(string? input)
    {
        Strings.Culture = new CultureInfo("ja");
        Strings.ApplyOverride(input);
        Assert.Null(Strings.Culture); // null = follow CurrentUICulture
    }
}
