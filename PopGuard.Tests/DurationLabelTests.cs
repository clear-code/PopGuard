using System.Globalization;
using System.Text.Json;
using PopGuard;
using PopGuard.Resources;
using Xunit;

namespace PopGuard.Tests;

public class DurationLabelTests : IDisposable
{
    public void Dispose() => Strings.Culture = null; // reset shared state after each test

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static DurationEntry Parse(string json) =>
        JsonSerializer.Deserialize<DurationEntry>(json, Options)!;

    // ---- JSON shapes the converter must accept ----

    [Fact]
    public void PlainStringLabel_IsCommonToEveryLanguage()
    {
        var e = Parse("""{ "label": "90分", "minutes": 90 }""");
        Assert.Equal("90分", e.Label!.Resolve(japanese: true));
        Assert.Equal("90分", e.Label!.Resolve(japanese: false));
    }

    [Fact]
    public void ObjectLabel_ResolvesPerLanguage()
    {
        var e = Parse("""{ "label": { "ja": "2時間", "en": "2 hours" }, "minutes": 120 }""");
        Assert.Equal("2時間", e.Label!.Resolve(japanese: true));
        Assert.Equal("2 hours", e.Label!.Resolve(japanese: false));
    }

    [Fact]
    public void MissingLabel_IsNull()
    {
        var e = Parse("""{ "minutes": 30 }""");
        Assert.Null(e.Label);
    }

    // ---- Case A: a single language only applies to that language ----

    [Fact]
    public void SingleLanguageObject_DoesNotBorrowForOtherLanguage()
    {
        var e = Parse("""{ "label": { "ja": "のみ" }, "minutes": 5 }""");
        Assert.Equal("のみ", e.Label!.Resolve(japanese: true));
        Assert.Null(e.Label!.Resolve(japanese: false)); // en not given -> null (caller auto-generates)
    }

    // ---- ResolveDurationLabel (language selection + auto-generation fallback) ----

    [Fact]
    public void ResolveDurationLabel_PrefersCurrentLanguage()
    {
        var e = Parse("""{ "label": { "ja": "2時間", "en": "2 hours" }, "minutes": 120 }""");

        Strings.Culture = new CultureInfo("ja");
        Assert.Equal("2時間", RulesStore.ResolveDurationLabel(e));

        Strings.Culture = new CultureInfo("en");
        Assert.Equal("2 hours", RulesStore.ResolveDurationLabel(e));
    }

    [Fact]
    public void ResolveDurationLabel_AutoGeneratesWhenLanguageMissing()
    {
        var e = Parse("""{ "label": { "ja": "2時間" }, "minutes": 120 }""");

        Strings.Culture = new CultureInfo("ja");
        Assert.Equal("2時間", RulesStore.ResolveDurationLabel(e)); // ja given

        Strings.Culture = new CultureInfo("en");
        Assert.Equal("2 h", RulesStore.ResolveDurationLabel(e)); // en missing -> auto (localized)
    }

    [Fact]
    public void ResolveDurationLabel_AutoGeneratesWhenNoLabel()
    {
        var e = Parse("""{ "minutes": 0 }""");

        Strings.Culture = new CultureInfo("ja");
        Assert.Equal("無制限", RulesStore.ResolveDurationLabel(e));

        Strings.Culture = new CultureInfo("en");
        Assert.Equal("Unlimited", RulesStore.ResolveDurationLabel(e));
    }
}
