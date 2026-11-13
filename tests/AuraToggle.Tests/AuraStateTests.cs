using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AuraToggle.Tests;

public sealed class AuraStateTests : PersistenceTest
{
    private static AuraState Load(string json)
    {
        TestData.WriteRaw(AuraState.FileName, json);
        return AuraState.Load();
    }

    // ---- defaults and constants ----

    [Fact]
    public void TheDefaultIsWhiteRainbowAtFullBrightness()
    {
        AuraState d = AuraState.Default;

        Assert.True(d.On);
        Assert.Equal(AuraState.ModeRainbow, d.Mode);
        Assert.Equal((byte)0xFF, d.Red);
        Assert.Equal((byte)0xFF, d.Green);
        Assert.Equal((byte)0xFF, d.Blue);
        Assert.Equal("", d.CustomPreset);
        Assert.Equal(AuraState.MaxBrightness, d.Brightness);
    }

    [Fact]
    public void ModeNumbersAndBrightnessRangeStayAsShipped()
    {
        // These numbers go to the controller and into stored files.
        Assert.Equal(0x00, AuraState.ModeOff);
        Assert.Equal(0x01, AuraState.ModeStatic);
        Assert.Equal(0x05, AuraState.ModeRainbow);
        Assert.Equal(10, AuraState.MinBrightness);
        Assert.Equal(100, AuraState.MaxBrightness);
        Assert.Equal("state.json", AuraState.FileName);
    }

    // ---- Dim ----

    [Fact]
    public void DimMatchesTheFormulaOverTheWholeRange()
    {
        for (int brightness = 0; brightness <= 255; brightness++)
        {
            int percent = Math.Clamp(brightness, 10, 100);

            for (int c = 0; c <= 255; c++)
            {
                (byte r, byte g, byte b) = AuraState.Dim((byte)c, (byte)(255 - c), (byte)(c / 2), (byte)brightness);

                Assert.Equal(c * percent / 100, r);
                Assert.Equal((255 - c) * percent / 100, g);
                Assert.Equal(c / 2 * percent / 100, b);
            }
        }
    }

    [Fact]
    public void DimNeverBrightensAndNeverGoesDark()
    {
        for (int brightness = 0; brightness <= 100; brightness++)
        {
            (byte r, byte g, byte b) = AuraState.Dim(255, 255, 255, (byte)brightness);

            Assert.True(r <= 255 && r >= 25, $"{brightness}: {r}");
            Assert.Equal(r, g);
            Assert.Equal(r, b);
        }
    }

    [Fact]
    public void DimIsSteadilyBrighterWithTheSlider()
    {
        int previous = -1;

        for (int brightness = 0; brightness <= 255; brightness++)
        {
            int now = AuraState.Dim(200, 200, 200, (byte)brightness).Red;

            Assert.True(now >= previous, $"{brightness}: {now} after {previous}");
            previous = now;
        }
    }

    [Theory]
    [InlineData(100, 255, 255, 255)]
    [InlineData(50, 127, 127, 127)]
    [InlineData(10, 25, 25, 25)]
    [InlineData(0, 25, 25, 25)]
    [InlineData(9, 25, 25, 25)]
    [InlineData(101, 255, 255, 255)]
    [InlineData(255, 255, 255, 255)]
    public void DimOfWhiteAtKnownSteps(int brightness, int r, int g, int b)
    {
        Assert.Equal(((byte)r, (byte)g, (byte)b), AuraState.Dim(255, 255, 255, (byte)brightness));
    }

    [Fact]
    public void DimOfBlackStaysBlackAndKeepsEachChannelApart()
    {
        Assert.Equal(((byte)0, (byte)0, (byte)0), AuraState.Dim(0, 0, 0, 55));
        Assert.Equal(((byte)100, (byte)0, (byte)50), AuraState.Dim(200, 0, 100, 50));
    }

    // ---- Load ----

    [Fact]
    public void LoadWithoutAFileGivesTheDefault()
    {
        Assert.Equal(AuraState.Default, AuraState.Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("{\"on\":false,")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("[{\"on\":false}]")]
    [InlineData("\"on\"")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("12")]
    public void LoadOfADamagedFileGivesTheDefault(string json)
    {
        Assert.Equal(AuraState.Default, Load(json));
    }

    [Fact]
    public void LoadOfBinaryGarbageGivesTheDefault()
    {
        File.WriteAllBytes(AuraFiles.PathTo(AuraState.FileName), new byte[] { 0x00, 0xFF, 0xFE, 0x7B, 0x80 });

        Assert.Equal(AuraState.Default, AuraState.Load());
    }

    [Fact]
    public void LoadOfAnEmptyObjectGivesTheDefault()
    {
        Assert.Equal(AuraState.Default, Load("{}"));
    }

    [Fact]
    public void LoadReadsEveryField()
    {
        AuraState loaded = Load("""
            {"on":false,"mode":2,"red":10,"green":20,"blue":30,"customPreset":"Calm","brightness":55}
            """);

        Assert.Equal(new AuraState(false, 2, 10, 20, 30, "Calm", 55), loaded);
    }

    [Fact]
    public void LoadFallsBackFieldByField()
    {
        // One bad field must not throw the good ones away, and must not throw at all.
        AuraState loaded = Load("""{"on":false,"red":"zz","green":7,"blue":null,"brightness":[]}""");

        Assert.False(loaded.On);
        Assert.Equal(AuraState.Default.Mode, loaded.Mode);
        Assert.Equal(AuraState.Default.Red, loaded.Red);
        Assert.Equal((byte)7, loaded.Green);
        Assert.Equal(AuraState.Default.Blue, loaded.Blue);
        Assert.Equal(AuraState.Default.Brightness, loaded.Brightness);
    }

    [Theory]
    [InlineData("\"on\":\"yes\"")]
    [InlineData("\"on\":1")]
    [InlineData("\"on\":null")]
    [InlineData("\"on\":[]")]
    public void LoadReadsOnlyARealBooleanForOn(string field)
    {
        Assert.True(Load("{" + field + "}").On);
    }

    [Theory]
    [InlineData("256")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"128\"")]
    [InlineData("true")]
    [InlineData("99999999999999999999")]
    public void LoadFallsBackForAColourThatIsNotAByte(string value)
    {
        AuraState loaded = Load($"{{\"red\":{value},\"green\":{value},\"blue\":{value}}}");

        Assert.Equal((byte)0xFF, loaded.Red);
        Assert.Equal((byte)0xFF, loaded.Green);
        Assert.Equal((byte)0xFF, loaded.Blue);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(11)]
    public void LoadKeepsAnEffectNumberTheBuildKnows(int mode)
    {
        Assert.Equal((byte)mode, Load($"{{\"mode\":{mode}}}").Mode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("8")]
    [InlineData("10")]
    [InlineData("12")]
    [InlineData("99")]
    [InlineData("255")]
    [InlineData("256")]
    [InlineData("-1")]
    [InlineData("\"rainbow\"")]
    [InlineData("null")]
    public void LoadPutsAnUnknownEffectBackToTheDefault(string mode)
    {
        // The window must not name an effect the board is not running.
        Assert.Equal(AuraState.ModeRainbow, Load($"{{\"mode\":{mode}}}").Mode);
    }

    [Theory]
    [InlineData("0", 10)]
    [InlineData("1", 10)]
    [InlineData("9", 10)]
    [InlineData("10", 10)]
    [InlineData("11", 11)]
    [InlineData("55", 55)]
    [InlineData("100", 100)]
    [InlineData("101", 100)]
    [InlineData("255", 100)]
    [InlineData("256", 100)]
    [InlineData("-5", 100)]
    [InlineData("\"30\"", 100)]
    [InlineData("null", 100)]
    public void LoadHoldsBrightnessToTheSliderRange(string value, int expected)
    {
        Assert.Equal((byte)expected, Load($"{{\"brightness\":{value}}}").Brightness);
    }

    [Fact]
    public void LoadCutsAndCleansTheCustomPresetName()
    {
        Assert.Equal(new string('n', 40), Load("{\"customPreset\":\"" + new string('n', 5000) + "\"}").CustomPreset);
        Assert.Equal("a b", Load("{\"customPreset\":\"a\\nb\"}").CustomPreset);
        Assert.Equal("padded", Load("{\"customPreset\":\"  padded  \"}").CustomPreset);
        Assert.Equal("", Load("{\"customPreset\":42}").CustomPreset);
        Assert.Equal("", Load("{\"customPreset\":null}").CustomPreset);
        Assert.Equal("", Load("{\"customPreset\":[\"x\"]}").CustomPreset);
    }

    [Fact]
    public void LoadKeepsAUnicodePresetName()
    {
        Assert.Equal("Café ゲーム 😀", Load("{\"customPreset\":\"Café ゲーム 😀\"}").CustomPreset);
    }

    [Fact]
    public void LoadNeverLeavesHalfAnEmojiInTheName()
    {
        string name = new string('a', 39) + "😀";

        Assert.Equal(new string('a', 39), Load("{\"customPreset\":\"" + name + "\"}").CustomPreset);
    }

    [Fact]
    public void LoadIgnoresFieldNamesInTheWrongCase()
    {
        Assert.Equal(AuraState.Default, Load("""{"On":false,"MODE":2,"Red":1,"brightNess":20,"CustomPreset":"x"}"""));
    }

    [Fact]
    public void LoadIgnoresUnknownFields()
    {
        AuraState loaded = Load("""{"future":{"a":[1,2]},"on":false,"extra":"x"}""");

        Assert.Equal(AuraState.Default with { On = false }, loaded);
    }

    [Fact]
    public void LoadAcceptsAByteOrderMark()
    {
        byte[] bom = new UTF8Encoding(true).GetPreamble();
        byte[] body = Encoding.UTF8.GetBytes("{\"on\":false}");
        File.WriteAllBytes(AuraFiles.PathTo(AuraState.FileName), bom.Concat(body).ToArray());

        Assert.False(AuraState.Load().On);
    }

    [Fact]
    public void LoadOfAFileOverTheSizeCeilingGivesTheDefault()
    {
        string big = "{\"on\":false,\"customPreset\":\"" + new string('x', (int)AuraFiles.MaxPresetFileBytes) + "\"}";

        Assert.Equal(AuraState.Default, Load(big));
    }

    [Fact]
    public void LoadAfterAFileWasFixedByHandWorksAgain()
    {
        Assert.Equal(AuraState.Default, Load("{broken"));

        Assert.False(Load("{\"on\":false}").On);
    }

    [Fact]
    public void ReviewStateReplacesTheStoredState()
    {
        new AuraState(false, 2, 1, 2, 3, "stored", 40).Save();
        var shown = new AuraState(true, 7, 9, 9, 9, "shown", 70);
        AuraState.ReviewState = shown;
        try
        {
            Assert.Same(shown, AuraState.Load());
        }
        finally
        {
            AuraState.ReviewState = null;
        }

        Assert.Equal("stored", AuraState.Load().CustomPreset);
    }

    // ---- Save ----

    [Fact]
    public void SaveWritesEveryFieldUnderItsOwnName()
    {
        new AuraState(false, 2, 10, 20, 30, "Calm", 55).Save();

        using JsonDocument document = JsonDocument.Parse(TestData.ReadRaw(AuraState.FileName)!);
        JsonElement root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.False(root.GetProperty("on").GetBoolean());
        Assert.Equal(2, root.GetProperty("mode").GetInt32());
        Assert.Equal(10, root.GetProperty("red").GetInt32());
        Assert.Equal(20, root.GetProperty("green").GetInt32());
        Assert.Equal(30, root.GetProperty("blue").GetInt32());
        Assert.Equal("Calm", root.GetProperty("customPreset").GetString());
        Assert.Equal(55, root.GetProperty("brightness").GetInt32());
        Assert.Equal(7, root.EnumerateObject().Count());
    }

    [Fact]
    public void SaveLeavesNoTempFile()
    {
        for (int i = 0; i < 10; i++)
        {
            AuraState.Default.Save();
        }

        Assert.Empty(TempFiles());
    }

    [Fact]
    public void SaveReplacesTheEarlierState()
    {
        AuraState.Default.Save();
        (AuraState.Default with { On = false, Red = 1 }).Save();

        Assert.Equal(AuraState.Default with { On = false, Red = 1 }, AuraState.Load());
    }

    [Theory]
    [InlineData(true, 1, 0, 0, 0, "", 10)]
    [InlineData(false, 5, 255, 255, 255, "", 100)]
    [InlineData(true, 11, 1, 128, 254, "Calm", 55)]
    [InlineData(false, 9, 0, 255, 0, "a \"quoted\" \\ name", 99)]
    [InlineData(true, 2, 12, 34, 56, "Café ゲーム 😀", 10)]
    public void SaveThenLoadGivesTheSameState(bool on, int mode, int r, int g, int b, string preset, int brightness)
    {
        var state = new AuraState(on, (byte)mode, (byte)r, (byte)g, (byte)b, preset, (byte)brightness);

        state.Save();

        Assert.Equal(state, AuraState.Load());
    }

    [Fact]
    public void SaveThenLoadOfAFullLengthNameKeepsAllOfIt()
    {
        var state = AuraState.Default with { CustomPreset = new string('p', AuraFiles.MaxPresetName) };

        state.Save();

        Assert.Equal(state, AuraState.Load());
    }

    [Fact]
    public void EveryColourByteSurvivesARoundTrip()
    {
        for (int c = 0; c <= 255; c += 17)
        {
            var state = AuraState.Default with { Red = (byte)c, Green = (byte)(255 - c), Blue = (byte)(c ^ 0x55) };

            state.Save();

            Assert.Equal(state, AuraState.Load());
        }
    }

    [Fact]
    public void SaveOverADamagedFileKeepsACopyOfIt()
    {
        // A damaged file is copied aside before the first save replaces it, so nothing is lost for good.
        TestData.WriteRaw(AuraState.FileName, "{\"on\":false, oops");
        Assert.Equal(AuraState.Default, AuraState.Load());

        (AuraState.Default with { On = false }).Save();

        string copy = Assert.Single(BadCopies("state"));
        Assert.Equal("{\"on\":false, oops", File.ReadAllText(copy));
        Assert.False(AuraState.Load().On);
    }

    [Fact]
    public void SaveThatCannotBeWrittenIsSwallowed()
    {
        Directory.CreateDirectory(AuraFiles.PathTo(AuraState.FileName));
        File.WriteAllText(Path.Combine(AuraFiles.PathTo(AuraState.FileName), "x"), "x");

        AuraState.Default.Save();

        Assert.Empty(TempFiles());
        Assert.Equal(AuraState.Default, AuraState.Load());
    }
}
