using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using Xunit;

namespace AuraToggle.Tests;

public partial class StringsTests
{
    private static readonly Regex Placeholder = PlaceholderPattern();

    public static IEnumerable<object[]> Languages() => Strings.Offered.Select(language => new object[] { language.Code });

    public static IEnumerable<object[]> OtherLanguages() =>
        Strings.Offered.Where(language => language.Code != "en").Select(language => new object[] { language.Code });

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    private static string BundleOf(string code) => Strings.Offered.Single(language => language.Code == code).Bundle;

    /// <summary>Every key and value a language file holds, read straight from the embedded resource.</summary>
    private static Dictionary<string, string> Table(string code)
    {
        var manager = new ResourceManager("AuraToggle." + BundleOf(code), typeof(Strings).Assembly);
        ResourceSet? set = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        var table = new Dictionary<string, string>();
        foreach (DictionaryEntry entry in set)
        {
            table.Add((string)entry.Key, (string)entry.Value!);
        }

        return table;
    }

    private static int[] Indices(string text) =>
        Placeholder.Matches(text).Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .OrderBy(index => index).ToArray();

    private static void WithLanguage(string language, string uiCulture, Action body)
    {
        string savedOverride = Strings.Override;
        CultureInfo savedUi = CultureInfo.CurrentUICulture;
        try
        {
            Strings.Override = language;
            CultureInfo.CurrentUICulture = new CultureInfo(uiCulture);
            body();
        }
        finally
        {
            Strings.Override = savedOverride;
            CultureInfo.CurrentUICulture = savedUi;
        }
    }

    [Fact]
    public void TheTableListsTenDistinctLanguagesStartingWithEnglish()
    {
        Assert.Equal(10, Strings.Offered.Length);
        Assert.Equal("en", Strings.Offered[0].Code);
        Assert.Equal(10, Strings.Offered.Select(language => language.Code).Distinct().Count());
        Assert.Equal(10, Strings.Offered.Select(language => language.Bundle).Distinct().Count());
        Assert.Equal(Strings.Offered.Select(language => language.Code), Strings.Codes);
        Assert.All(Strings.Offered, language => Assert.Matches("^[a-z]{2}$", language.Code));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLanguageLoads(string code)
    {
        Assert.NotEmpty(Table(code));
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void EveryLanguageHasExactlyTheKeysEnglishHas(string code)
    {
        HashSet<string> english = Table("en").Keys.ToHashSet();
        HashSet<string> other = Table(code).Keys.ToHashSet();
        Assert.Empty(english.Except(other));
        Assert.Empty(other.Except(english));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryValueIsNonEmptyAndTrimmed(string code)
    {
        foreach ((string key, string value) in Table(code))
        {
            Assert.False(string.IsNullOrWhiteSpace(value), $"{code}:{key} is empty");
            Assert.Equal(value.Trim(), value);
        }
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void PlaceholdersMatchEnglishInCountAndIndex(string code)
    {
        Dictionary<string, string> english = Table("en");
        foreach ((string key, string value) in Table(code))
        {
            Assert.True(
                Indices(english[key]).SequenceEqual(Indices(value)),
                $"{code}:{key} placeholders differ from English: '{value}'");
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryValueFormatsWithItsPlaceholders(string code)
    {
        foreach ((string key, string value) in Table(code))
        {
            int count = Indices(value).DefaultIfEmpty(-1).Max() + 1;
            object[] args = Enumerable.Repeat((object)"x", count).ToArray();
            string formatted = string.Format(CultureInfo.InvariantCulture, value, args);
            Assert.DoesNotContain('{', formatted);
            Assert.False(string.IsNullOrEmpty(formatted), $"{code}:{key}");
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryStringPropertyResolvesInEveryLanguage(string code)
    {
        // A property whose key is missing falls back to the key itself, which would show in the window.
        PropertyInfo[] properties = typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string) && property.Name != nameof(Strings.Override))
            .ToArray();
        Assert.True(properties.Length > 50);

        WithLanguage(code, "en-US", () =>
        {
            foreach (PropertyInfo property in properties)
            {
                string text = (string)property.GetValue(null)!;
                Assert.False(string.IsNullOrWhiteSpace(text), $"{code}:{property.Name}");
                Assert.NotEqual(property.Name, text);
            }
        });
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryPropertyKeyExistsInTheEnglishTable(string code)
    {
        Dictionary<string, string> table = Table(code);
        foreach (PropertyInfo property in typeof(Strings)
                     .GetProperties(BindingFlags.Public | BindingFlags.Static)
                     .Where(property => property.PropertyType == typeof(string) && property.Name != nameof(Strings.Override)))
        {
            Assert.True(table.ContainsKey(property.Name), $"{code} lacks {property.Name}");
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void InLanguageReadsThatLanguageWhateverTheOverrideIs(string code)
    {
        Dictionary<string, string> table = Table(code);
        WithLanguage("en", "en-US", () =>
        {
            Assert.Equal(table["ButtonRetry"], Strings.InLanguage("ButtonRetry", code));
            Assert.Equal(table["WindowTitle"], Strings.InLanguage("WindowTitle", code));
        });
    }

    [Fact]
    public void InLanguageFallsBackToEnglishForAnUnknownCodeAndToTheKeyForAnUnknownKey()
    {
        string english = Table("en")["ButtonRetry"];
        Assert.Equal(english, Strings.InLanguage("ButtonRetry", "xx"));
        Assert.Equal(english, Strings.InLanguage("ButtonRetry", ""));
        Assert.Equal(english, Strings.InLanguage("ButtonRetry", "DE"));
        Assert.Equal("NoSuchKey", Strings.InLanguage("NoSuchKey", "en"));
        Assert.Equal("NoSuchKey", Strings.InLanguage("NoSuchKey", "de"));
    }

    [Fact]
    public void SwitchingTheOverrideChangesTheTextAndEnglishComesBack()
    {
        string english = Table("en")["ButtonRetry"];
        string german = Table("de")["ButtonRetry"];
        Assert.NotEqual(english, german);

        WithLanguage("en", "en-US", () =>
        {
            Assert.Equal(english, Strings.ButtonRetry);
            Strings.Override = "de";
            Assert.Equal(german, Strings.ButtonRetry);
            Strings.Override = "en";
            Assert.Equal(english, Strings.ButtonRetry);
        });
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("fr")]
    [InlineData("EN")]
    [InlineData("deu")]
    [InlineData(" de")]
    public void AnUnknownOverrideFallsBackToEnglish(string code)
    {
        string english = Table("en")["ButtonRetry"];
        WithLanguage(code, "de-DE", () => Assert.Equal(english, Strings.ButtonRetry));
    }

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("pt-PT", "pt")]
    [InlineData("pt-BR", "pt")]
    [InlineData("zh-CN", "zh")]
    [InlineData("zh-TW", "zh")]
    [InlineData("tr-TR", "tr")]
    [InlineData("ja-JP", "ja")]
    [InlineData("en-GB", "en")]
    [InlineData("fr-FR", "en")]
    [InlineData("ru-RU", "en")]
    public void AnEmptyOverrideFollowsTheWindowsDisplayLanguage(string uiCulture, string expectedLanguage)
    {
        string expected = Table(expectedLanguage)["ButtonRetry"];
        WithLanguage("", uiCulture, () => Assert.Equal(expected, Strings.ButtonRetry));
    }

    [Fact]
    public void AnOverrideWinsOverTheWindowsDisplayLanguage()
    {
        string spanish = Table("es")["ButtonRetry"];
        WithLanguage("es", "de-DE", () => Assert.Equal(spanish, Strings.ButtonRetry));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void LanguageNamesDoNotDependOnTheThreadCulture(string code)
    {
        // Resource lookup follows the override, never the culture of the calling thread.
        string expected = Table(code)["ButtonRetry"];
        foreach (string culture in new[] { "en-US", "tr-TR", "ja-JP" })
        {
            WithLanguage(code, culture, () => Assert.Equal(expected, Strings.ButtonRetry));
        }
    }

    // Regression: the Turkish brightness read-out puts the percent sign in front.
    [Fact]
    public void TurkishBrightnessPutsThePercentSignFirst()
    {
        string text = string.Format(CultureInfo.InvariantCulture, Table("tr")["BrightnessValue"], 50);
        Assert.Equal("%50", text);
    }

    // Regression: the other languages keep the number first, and German keeps its space.
    [Theory]
    [InlineData("en", "50%")]
    [InlineData("de", "50 %")]
    [InlineData("es", "50%")]
    [InlineData("ja", "50%")]
    public void BrightnessReadOutFollowsEachLanguagesConvention(string code, string expected)
    {
        Assert.Equal(expected, string.Format(CultureInfo.InvariantCulture, Table(code)["BrightnessValue"], 50));
    }

    // Regression: the German reset-failure text speaks informally like the rest of the interface.
    [Fact]
    public void GermanBackupFailureUsesTheInformalRetryWording()
    {
        string text = Table("de")["ErrorResetBackupFailed"];
        Assert.EndsWith("versuch es noch mal.", text);
        Assert.DoesNotContain("versuche es erneut", text);
    }

    // Regression: the Italian minimise setting stays short enough for the settings panel.
    [Fact]
    public void ItalianMinimiseSettingStaysShort()
    {
        Assert.True(Table("it")["SettingMinimiseOnClose"].Length <= 32);
    }

    [Fact]
    public void ChannelHeaderAndQualifiedNameKeepTheirPlaceholders()
    {
        foreach ((string code, _) in Strings.Offered)
        {
            Dictionary<string, string> table = Table(code);
            Assert.Equal(new[] { 0 }, Indices(table["ChannelHeader"]));
            Assert.Equal(new[] { 0, 1 }, Indices(table["ChannelQualified"]));
            Assert.Equal(new[] { 0 }, Indices(table["BrightnessValue"]));
        }
    }
}
