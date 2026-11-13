using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace AuraToggle.Tests;

public class AuraPresetsTests
{
    private static readonly byte[] KnownModes = { 1, 2, 3, 4, 5, 6, 7, 9, 11 };

    public static IEnumerable<object[]> AllModes() => Enumerable.Range(0, 256).Select(mode => new object[] { (byte)mode });

    public static IEnumerable<object[]> AllKeys() => AuraPresets.All.Select(preset => new object[] { preset.Key });

    private static AuraPreset ByKey(string key) => AuraPresets.All.Single(preset => preset.Key == key);

    [Fact]
    public void AllHoldsTheNineKnownEffects()
    {
        Assert.Equal(KnownModes, AuraPresets.All.Select(preset => preset.Mode).ToArray());
        Assert.Equal(
            new[] { "static", "breathing", "flashing", "spectrum-cycle", "rainbow", "rainbow-breathing", "chase-fade", "chase", "wave" },
            AuraPresets.All.Select(preset => preset.Key).ToArray());
    }

    [Fact]
    public void KeysModesAndResourceKeysAreUnique()
    {
        Assert.Equal(AuraPresets.All.Count, AuraPresets.All.Select(p => p.Key).Distinct().Count());
        Assert.Equal(AuraPresets.All.Count, AuraPresets.All.Select(p => p.Mode).Distinct().Count());
        Assert.Equal(AuraPresets.All.Count, AuraPresets.All.Select(p => p.ResourceKey).Distinct().Count());
    }

    [Fact]
    public void NormalisedKeysAreUnique()
    {
        Assert.Equal(AuraPresets.All.Count, AuraPresets.All.Select(p => AuraPresets.Normalise(p.Key)).Distinct().Count());
    }

    [Fact]
    public void NamesListsEveryKeyInOrder()
    {
        Assert.Equal(
            "static, breathing, flashing, spectrum-cycle, rainbow, rainbow-breathing, chase-fade, chase, wave",
            AuraPresets.Names);
    }

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void EveryResourceKeyResolvesToText(string key)
    {
        AuraPreset preset = ByKey(key);
        string name = Strings.InLanguage(preset.ResourceKey, "en");
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.NotEqual(preset.ResourceKey, name);

        string hintKey = "EffectHint" + preset.ResourceKey["Preset".Length..];
        string hint = Strings.InLanguage(hintKey, "en");
        Assert.False(string.IsNullOrWhiteSpace(hint));
        Assert.NotEqual(hintKey, hint);
    }

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void DisplayNameHintAndToStringAgree(string key)
    {
        AuraPreset preset = ByKey(key);
        string saved = Strings.Override;
        try
        {
            Strings.Override = "en";
            Assert.False(string.IsNullOrWhiteSpace(preset.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(preset.HintText));
            Assert.Equal(preset.DisplayName, preset.ToString());
        }
        finally
        {
            Strings.Override = saved;
        }
    }

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void FindAcceptsTheKeyInAnySpelling(string key)
    {
        AuraPreset preset = ByKey(key);
        foreach (string spelling in new[]
        {
            key, key.ToUpperInvariant(), key.Replace('-', ' '), key.Replace('-', '_'), key.Replace("-", ""),
            "  " + key + "  ", key.Replace("-", " - "),
        })
        {
            Assert.Same(preset, AuraPresets.Find(spelling));
        }
    }

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void FindAcceptsTheDisplayNameInEveryLanguage(string key)
    {
        AuraPreset preset = ByKey(key);
        foreach (string language in Strings.Codes)
        {
            string display = Strings.InLanguage(preset.ResourceKey, language);
            Assert.Same(preset, AuraPresets.Find(display));
            Assert.Same(preset, AuraPresets.Find(display.ToUpperInvariant()));
        }
    }

    [Fact]
    public void FindDoesNotDependOnTheInterfaceLanguageOrCulture()
    {
        // A script written with the German name keeps working after the interface is switched.
        string savedOverride = Strings.Override;
        CultureInfo savedCulture = CultureInfo.CurrentCulture;
        try
        {
            string german = Strings.InLanguage("PresetRainbow", "de");
            foreach (string language in new[] { "en", "de", "tr", "ja" })
            {
                Strings.Override = language;
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
                Assert.Equal((byte)5, AuraPresets.Find(german)?.Mode);
                Assert.Equal((byte)1, AuraPresets.Find("STATIC")?.Mode);
            }
        }
        finally
        {
            Strings.Override = savedOverride;
            CultureInfo.CurrentCulture = savedCulture;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("stat1c")]
    [InlineData("static extra")]
    [InlineData("---")]
    [InlineData("__")]
    [InlineData("0")]
    public void FindReturnsNullForUnknownOrEmpty(string name)
    {
        Assert.Null(AuraPresets.Find(name));
    }

    [Theory]
    [InlineData("Static", "static")]
    [InlineData("Spectrum Cycle", "spectrumcycle")]
    [InlineData("rainbow_breathing", "rainbowbreathing")]
    [InlineData("chase-fade", "chasefade")]
    [InlineData("  A b-C_d 1  ", "abcd1")]
    [InlineData("", "")]
    [InlineData("!?.,", "")]
    public void NormaliseKeepsLowerCaseLettersAndDigitsOnly(string input, string expected)
    {
        Assert.Equal(expected, AuraPresets.Normalise(input));
    }

    [Fact]
    public void NormaliseIgnoresTheCurrentCulture()
    {
        // Under Turkish casing "I" would lower-case to a dotless i; the helper must not.
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("irisi", AuraPresets.Normalise("IRISI"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void ByModeFindsKnownModesOnly(byte mode)
    {
        AuraPreset? found = AuraPresets.ByMode(mode);
        if (KnownModes.Contains(mode))
        {
            Assert.NotNull(found);
            Assert.Equal(mode, found.Mode);
        }
        else
        {
            Assert.Null(found);
        }
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void ValidChannelModeKeepsOffAndKnownModesElseStatic(byte mode)
    {
        byte expected = mode == 0 || KnownModes.Contains(mode) ? mode : (byte)1;
        Assert.Equal(expected, AuraPresets.ValidChannelMode(mode));
    }

    [Fact]
    public void ValidChannelModeNeverFallsBackToRainbow()
    {
        // Rainbow pulls every channel of the controller along, so an unknown mode must become static.
        Assert.Equal(AuraState.ModeStatic, AuraPresets.ValidChannelMode(0xFF));
        Assert.NotEqual(AuraState.ModeRainbow, AuraPresets.ValidChannelMode(0x7F));
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void IsFirmwareModeIsTrueForTheFourColourlessEffects(byte mode)
    {
        bool expected = mode is 4 or 5 or 6 or 11;
        Assert.Equal(expected, AuraPresets.IsFirmwareMode(mode));
    }

    [Fact]
    public void UsesColourMatchesTheEffects()
    {
        Assert.Equal(
            new[] { "static", "breathing", "flashing", "chase-fade", "chase" },
            AuraPresets.All.Where(p => p.UsesColour).Select(p => p.Key).ToArray());
    }
}
