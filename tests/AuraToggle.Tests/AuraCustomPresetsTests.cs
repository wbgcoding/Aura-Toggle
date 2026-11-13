using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace AuraToggle.Tests;

public sealed class AuraCustomPresetsTests : PersistenceTest
{
    private static List<CustomPreset> Load(string json)
    {
        TestData.WriteRaw(AuraCustomPresets.FileName, json);
        return AuraCustomPresets.Load();
    }

    private static string One(string entryFields, string name = "P") =>
        "[{\"name\":\"" + name + "\",\"entries\":[{" + entryFields + "}]}]";

    private static CustomPresetEntry Entry(string key = "dev#1", int channel = 0, string label = "Board",
        byte mode = 1, byte r = 1, byte g = 2, byte b = 3, byte brightness = 50) =>
        new(key, channel, label, mode, r, g, b, brightness);

    // ---- constants and empty states ----

    [Fact]
    public void TheStoredFileNameStaysAsShipped()
    {
        Assert.Equal("presets.json", AuraCustomPresets.FileName);
    }

    [Fact]
    public void LoadWithoutAFileGivesAnEmptyList()
    {
        Assert.Empty(AuraCustomPresets.Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("[")]
    [InlineData("[{\"name\":\"P\",")]
    [InlineData("garbage")]
    [InlineData("{}")]
    [InlineData("{\"name\":\"P\",\"entries\":[]}")]
    [InlineData("\"x\"")]
    [InlineData("null")]
    [InlineData("7")]
    [InlineData("true")]
    public void LoadOfADamagedFileOrAWrongRootGivesAnEmptyList(string json)
    {
        Assert.Empty(Load(json));
    }

    [Fact]
    public void LoadOfAnEmptyArrayGivesAnEmptyList()
    {
        Assert.Empty(Load("[]"));
    }

    [Fact]
    public void EachLoadGivesAFreshList()
    {
        TestData.WriteRaw(AuraCustomPresets.FileName, One("\"mode\":1"));

        List<CustomPreset> first = AuraCustomPresets.Load();
        first.Clear();

        Assert.Single(AuraCustomPresets.Load());
    }

    // ---- elements and properties of the wrong kind ----

    [Theory]
    [InlineData("5")]
    [InlineData("\"x\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("[{\"name\":\"X\",\"entries\":[{\"mode\":1}]}]")]
    public void LoadSkipsAnArrayElementThatIsNotAPresetObject(string element)
    {
        List<CustomPreset> presets = Load("[" + element + ",{\"name\":\"Kept\",\"entries\":[{\"mode\":1}]}]");

        CustomPreset kept = Assert.Single(presets);
        Assert.Equal("Kept", kept.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"name\":\"\"")]
    [InlineData("\"name\":\"   \"")]
    [InlineData("\"name\":\"\\t\\r\\n\"")]
    [InlineData("\"name\":5")]
    [InlineData("\"name\":null")]
    [InlineData("\"name\":[\"A\"]")]
    [InlineData("\"name\":{}")]
    public void LoadSkipsAPresetWithoutAUsableName(string nameField)
    {
        string comma = nameField.Length == 0 ? "" : ",";

        Assert.Empty(Load("[{" + nameField + comma + "\"entries\":[{\"mode\":1}]}]"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"entries\":null")]
    [InlineData(",\"entries\":{}")]
    [InlineData(",\"entries\":\"x\"")]
    [InlineData(",\"entries\":5")]
    [InlineData(",\"entries\":[]")]
    [InlineData(",\"entries\":[5,\"x\",null,[],true]")]
    public void LoadSkipsAPresetWithoutAnyUsableEntry(string entriesField)
    {
        Assert.Empty(Load("[{\"name\":\"P\"" + entriesField + "}]"));
    }

    [Fact]
    public void LoadSkipsEntriesThatAreNotObjectsButKeepsTheRest()
    {
        List<CustomPreset> presets = Load("[{\"name\":\"P\",\"entries\":[5,null,{\"mode\":2,\"channel\":1},\"x\",[]]}]");

        CustomPresetEntry only = Assert.Single(Assert.Single(presets).Entries);
        Assert.Equal(2, only.Mode);
        Assert.Equal(1, only.Channel);
    }

    [Fact]
    public void LoadKeepsAnEntryWhoseEveryFieldIsMissing()
    {
        CustomPresetEntry entry = Assert.Single(Assert.Single(Load("[{\"name\":\"P\",\"entries\":[{}]}]")).Entries);

        Assert.Equal("", entry.DeviceKey);
        Assert.Equal(-1, entry.Channel);
        Assert.Equal("", entry.Label);
        Assert.Equal(AuraState.ModeOff, entry.Mode);
        Assert.Equal(0, entry.Red);
        Assert.Equal(0, entry.Green);
        Assert.Equal(0, entry.Blue);
        Assert.Equal(0, entry.Brightness);
    }

    [Fact]
    public void LoadReadsEveryFieldOfAFullEntry()
    {
        CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One(
            "\"deviceKey\":\"dev#1\",\"channel\":2,\"label\":\"Strip\",\"mode\":5,\"red\":10,\"green\":20,\"blue\":30,\"brightness\":75",
            "Calm"))).Entries);

        Assert.Equal(new CustomPresetEntry("dev#1", 2, "Strip", 5, 10, 20, 30, 75), entry);
    }

    [Fact]
    public void LoadIgnoresFieldNamesInTheWrongCase()
    {
        CustomPresetEntry entry = Assert.Single(Assert.Single(Load(
            "[{\"Name\":\"P\",\"name\":\"P\",\"entries\":[{\"Mode\":5,\"DeviceKey\":\"x\",\"RED\":9}]}]")).Entries);

        Assert.Equal("", entry.DeviceKey);
        Assert.Equal(0, entry.Mode);
        Assert.Equal(0, entry.Red);
    }

    [Fact]
    public void LoadIgnoresUnknownFields()
    {
        CustomPresetEntry entry = Assert.Single(Assert.Single(Load(
            "[{\"name\":\"P\",\"colour\":\"red\",\"entries\":[{\"mode\":1,\"speed\":9,\"nested\":{\"a\":[1,2]}}]}]")).Entries);

        Assert.Equal(1, entry.Mode);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("green")]
    [InlineData("blue")]
    [InlineData("brightness")]
    [InlineData("mode")]
    public void LoadReadsAByteFieldOfAnotherKindAsItsFallback(string field)
    {
        foreach (string wrong in new[] { "\"7\"", "true", "null", "[]", "{}", "1.5", "-1", "256", "1e3" })
        {
            CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One("\"" + field + "\":" + wrong))).Entries);

            // Mode falls back to 0 (off) which is a valid channel mode; the colours and brightness to 0.
            Assert.Equal(0, field switch
            {
                "red" => entry.Red,
                "green" => entry.Green,
                "blue" => entry.Blue,
                "brightness" => entry.Brightness,
                _ => entry.Mode,
            });
        }
    }

    [Theory]
    [InlineData("deviceKey")]
    [InlineData("label")]
    public void LoadReadsATextFieldOfAnotherKindAsEmpty(string field)
    {
        foreach (string wrong in new[] { "5", "true", "null", "[]", "{}" })
        {
            CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One("\"" + field + "\":" + wrong))).Entries);

            Assert.Equal("", field == "label" ? entry.Label : entry.DeviceKey);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(255)]
    public void LoadKeepsTheFullByteRangeForEachColour(int value)
    {
        CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One(
            $"\"red\":{value},\"green\":{value},\"blue\":{value}"))).Entries);

        Assert.Equal((byte)value, entry.Red);
        Assert.Equal((byte)value, entry.Green);
        Assert.Equal((byte)value, entry.Blue);
    }

    // ---- mode validation ----

    [Fact]
    public void LoadKeepsEveryModeThisBuildKnowsAndOff()
    {
        foreach (byte mode in AuraPresets.All.Select(p => p.Mode).Append(AuraState.ModeOff))
        {
            CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One("\"mode\":" + mode))).Entries);

            Assert.Equal(mode, entry.Mode);
        }
    }

    [Fact]
    public void LoadReplacesEveryUnknownModeWithStatic()
    {
        // The window must never name an effect the board does not run while sending something else.
        byte[] known = AuraPresets.All.Select(p => p.Mode).Append(AuraState.ModeOff).ToArray();

        foreach (int mode in Enumerable.Range(0, 256).Where(m => !known.Contains((byte)m)))
        {
            CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One("\"mode\":" + mode))).Entries);

            Assert.Equal(AuraState.ModeStatic, entry.Mode);
        }
    }

    // ---- brightness ----

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(11, 11)]
    [InlineData(55, 55)]
    [InlineData(99, 99)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(200, 100)]
    [InlineData(255, 100)]
    public void LoadKeepsZeroAsLeaveAloneAndHoldsTheRestToTheSliderRange(int stored, int expected)
    {
        CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One("\"brightness\":" + stored))).Entries);

        Assert.Equal(expected, entry.Brightness);
    }

    [Fact]
    public void AnEntryWrittenBeforePresetsCarriedABrightnessLeavesTheChannelAlone()
    {
        CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One("\"mode\":1,\"red\":9"))).Entries);

        Assert.Equal(0, entry.Brightness);
    }

    // ---- channel ----

    [Theory]
    [InlineData("-1", -1)]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("7", 7)]
    [InlineData("2147483647", int.MaxValue)]
    public void LoadKeepsAChannelFromTheWholeControllerUp(string stored, int expected)
    {
        CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One("\"channel\":" + stored))).Entries);

        Assert.Equal(expected, entry.Channel);
    }

    [Fact]
    public void AnEntryWithoutAChannelMeansTheWholeController()
    {
        // Written before presets went per channel.
        Assert.Equal(-1, Assert.Single(Assert.Single(Load(One("\"mode\":1"))).Entries).Channel);
    }

    [Theory]
    [InlineData("-2")]
    [InlineData("-3")]
    [InlineData("-100")]
    [InlineData("-2147483648")]
    public void LoadDropsAnEntryBelowTheWholeControllerChannel(string stored)
    {
        // The code downstream reads any channel below zero as the whole controller.
        List<CustomPreset> presets = Load(
            "[{\"name\":\"P\",\"entries\":[{\"channel\":" + stored + ",\"mode\":2},{\"channel\":3,\"mode\":3}]}]");

        CustomPresetEntry only = Assert.Single(Assert.Single(presets).Entries);
        Assert.Equal(3, only.Channel);
    }

    [Fact]
    public void ADroppedEntryTakesItsPresetOutWhenItWasTheOnlyOne()
    {
        Assert.Empty(Load(One("\"channel\":-5,\"mode\":2")));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    [InlineData("1e20")]
    public void LoadReadsAChannelThatIsNotAnIntNumberAsTheWholeController(string stored)
    {
        Assert.Equal(-1, Assert.Single(Assert.Single(Load(One("\"channel\":" + stored))).Entries).Channel);
    }

    // ---- names and labels ----

    [Fact]
    public void LoadCutsALongNameToTheCap()
    {
        string name = new string('n', AuraFiles.MaxPresetName + 50);

        Assert.Equal(new string('n', AuraFiles.MaxPresetName), Assert.Single(Load(One("\"mode\":1", name))).Name);
    }

    [Fact]
    public void LoadCutsALongLabelToItsCap()
    {
        string label = new string('l', AuraFiles.MaxChannelLabel + 50);

        CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One("\"label\":\"" + label + "\""))).Entries);

        Assert.Equal(new string('l', AuraFiles.MaxChannelLabel), entry.Label);
    }

    [Fact]
    public void LoadTurnsLineBreaksInANameIntoSpaces()
    {
        Assert.Equal("a b c", Assert.Single(Load(One("\"mode\":1", "a\\nb\\tc"))).Name);
    }

    [Fact]
    public void LoadTurnsLineBreaksInALabelIntoSpaces()
    {
        CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One("\"label\":\"x\\r\\ny\""))).Entries);

        Assert.Equal("x  y", entry.Label);
    }

    [Fact]
    public void LoadTrimsANameAndALabel()
    {
        CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One("\"label\":\"  Strip  \"", "  Calm  "))).Entries);

        Assert.Equal("Strip", entry.Label);
        Assert.Equal("Calm", Assert.Single(Load(One("\"label\":\"  Strip  \"", "  Calm  "))).Name);
    }

    [Fact]
    public void LoadDoesNotCutAnEmojiInHalf()
    {
        string name = new string('x', AuraFiles.MaxPresetName - 1) + "\uD83D\uDE00";

        string loaded = Assert.Single(Load(One("\"mode\":1", name))).Name;

        Assert.Equal(new string('x', AuraFiles.MaxPresetName - 1), loaded);
        Assert.DoesNotContain(loaded, char.IsSurrogate);
    }

    [Fact]
    public void LoadKeepsUnicodeNamesAndLabels()
    {
        CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One(
            "\"label\":\"Caf\\u00E9 \\uD83D\\uDE00\"", "\\u30B2\\u30FC\\u30E0"))).Entries);

        Assert.Equal("Caf\u00E9 \uD83D\uDE00", entry.Label);
        Assert.Equal("\u30B2\u30FC\u30E0", Assert.Single(Load(One("\"mode\":1", "\\u30B2\\u30FC\\u30E0"))).Name);
    }

    [Fact]
    public void LoadKeepsTheDeviceKeyExactlyAsStored()
    {
        string key = @"\\\\?\\hid#vid_0b05&pid_19af#7&abc&0&0000#{4d1e55b2}";

        CustomPresetEntry entry = Assert.Single(Assert.Single(Load(One("\"deviceKey\":\"" + key + "\""))).Entries);

        Assert.Equal(@"\\?\hid#vid_0b05&pid_19af#7&abc&0&0000#{4d1e55b2}", entry.DeviceKey);
    }

    // ---- UniqueName ----

    [Fact]
    public void UniqueNameKeepsAFreeNameAndRemembersIt()
    {
        var taken = new HashSet<string>();

        Assert.Equal("Calm", AuraCustomPresets.UniqueName("Calm", taken));

        Assert.Contains("Calm", taken);
    }

    [Fact]
    public void UniqueNameCountsUpFromTwo()
    {
        var taken = new HashSet<string>();

        Assert.Equal("A", AuraCustomPresets.UniqueName("A", taken));
        Assert.Equal("A (2)", AuraCustomPresets.UniqueName("A", taken));
        Assert.Equal("A (3)", AuraCustomPresets.UniqueName("A", taken));
        Assert.Equal("A (4)", AuraCustomPresets.UniqueName("A", taken));
        Assert.Equal(4, taken.Count);
    }

    [Fact]
    public void UniqueNameSkipsASuffixedNameAlreadyTaken()
    {
        var taken = new HashSet<string> { "A", "A (2)" };

        Assert.Equal("A (3)", AuraCustomPresets.UniqueName("A", taken));
    }

    [Fact]
    public void UniqueNameTreatsCaseAsDifferent()
    {
        var taken = new HashSet<string> { "calm" };

        Assert.Equal("Calm", AuraCustomPresets.UniqueName("Calm", taken));
    }

    [Fact]
    public void UniqueNameStaysInsideTheCapForAFullLengthName()
    {
        string name = new string('x', AuraFiles.MaxPresetName);
        var taken = new HashSet<string>();

        string first = AuraCustomPresets.UniqueName(name, taken);
        string second = AuraCustomPresets.UniqueName(name, taken);
        string third = AuraCustomPresets.UniqueName(name, taken);

        Assert.Equal(name, first);
        Assert.Equal(new string('x', AuraFiles.MaxPresetName - 4) + " (2)", second);
        Assert.Equal(new string('x', AuraFiles.MaxPresetName - 4) + " (3)", third);
        Assert.All(new[] { first, second, third }, n => Assert.True(n.Length <= AuraFiles.MaxPresetName));
    }

    [Fact]
    public void UniqueNameKeepsTenAndMoreDuplicatesApartInsideTheCap()
    {
        string name = new string('y', AuraFiles.MaxPresetName);
        var taken = new HashSet<string>();

        var names = Enumerable.Range(0, 120).Select(_ => AuraCustomPresets.UniqueName(name, taken)).ToList();

        Assert.Equal(120, names.Distinct().Count());
        Assert.All(names, n => Assert.True(n.Length <= AuraFiles.MaxPresetName));
        Assert.EndsWith(" (120)", names[^1]);
    }

    [Fact]
    public void UniqueNameNeverLeavesAHalfEmojiWhenTheSuffixCutsThroughOne()
    {
        // 35 characters, an emoji (two chars) and three more: the cut for " (2)" lands inside the emoji.
        string name = new string('x', 35) + "\uD83D\uDE00" + "zzz";
        var taken = new HashSet<string> { name };

        string unique = AuraCustomPresets.UniqueName(name, taken);

        Assert.Equal(new string('x', 35) + " (2)", unique);
        Assert.DoesNotContain(unique, char.IsSurrogate);
    }

    [Fact]
    public void UniqueNameResultCanBeWrittenAsJson()
    {
        string name = new string('x', 35) + "\uD83D\uDE00" + "zzz";
        var taken = new HashSet<string> { name };
        string unique = AuraCustomPresets.UniqueName(name, taken);

        var presets = new List<CustomPreset> { new(unique, new List<CustomPresetEntry> { Entry() }) };

        Assert.True(AuraCustomPresets.Save(presets));
    }

    [Fact]
    public void UniqueNameForAShortNameNeverCutsIt()
    {
        var taken = new HashSet<string> { "Hi" };

        Assert.Equal("Hi (2)", AuraCustomPresets.UniqueName("Hi", taken));
    }

    // ---- duplicate names in a stored file ----

    [Fact]
    public void LoadGivesTwoPresetsWithTheSameNameDifferentNames()
    {
        List<CustomPreset> presets = Load(
            "[{\"name\":\"A\",\"entries\":[{\"mode\":1}]},{\"name\":\"A\",\"entries\":[{\"mode\":2}]},{\"name\":\"A\",\"entries\":[{\"mode\":3}]}]");

        Assert.Equal(new[] { "A", "A (2)", "A (3)" }, presets.Select(p => p.Name).ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, presets.Select(p => p.Entries[0].Mode).ToArray());
    }

    [Fact]
    public void LoadGivesTwoLongNamesThatCutToTheSameTextDifferentNames()
    {
        string a = new string('q', AuraFiles.MaxPresetName) + "AAA";
        string b = new string('q', AuraFiles.MaxPresetName) + "BBB";

        List<CustomPreset> presets = Load(
            "[{\"name\":\"" + a + "\",\"entries\":[{\"mode\":1}]},{\"name\":\"" + b + "\",\"entries\":[{\"mode\":2}]}]");

        Assert.Equal(2, presets.Select(p => p.Name).Distinct().Count());
        Assert.All(presets, p => Assert.True(p.Name.Length <= AuraFiles.MaxPresetName));
    }

    [Fact]
    public void ASkippedPresetDoesNotUseUpItsName()
    {
        List<CustomPreset> presets = Load(
            "[{\"name\":\"A\",\"entries\":[]},{\"name\":\"A\",\"entries\":[{\"mode\":2}]}]");

        Assert.Equal("A", Assert.Single(presets).Name);
    }

    [Fact]
    public void LoadKeepsThePresetOrderOfTheFile()
    {
        List<CustomPreset> presets = Load(
            "[{\"name\":\"Z\",\"entries\":[{\"mode\":1}]},{\"name\":\"A\",\"entries\":[{\"mode\":1}]},{\"name\":\"M\",\"entries\":[{\"mode\":1}]}]");

        Assert.Equal(new[] { "Z", "A", "M" }, presets.Select(p => p.Name).ToArray());
    }

    // ---- Save ----

    [Fact]
    public void SaveThenLoadGivesTheSamePresets()
    {
        var presets = new List<CustomPreset>
        {
            new("Calm", new List<CustomPresetEntry>
            {
                Entry("dev#1", 0, "Board", 1, 10, 20, 30, 80),
                Entry("dev#1", 2, "Strip \uD83D\uDE00", 5, 0, 0, 0, 0),
                Entry("dev#2", -1, "Fan", AuraState.ModeOff, 255, 255, 255, 100),
            }),
            new("Caf\u00E9 \u30B2", new List<CustomPresetEntry> { Entry("dev#3", 4) }),
        };

        Assert.True(AuraCustomPresets.Save(presets));

        List<CustomPreset> loaded = AuraCustomPresets.Load();
        Assert.Equal(2, loaded.Count);
        for (int i = 0; i < presets.Count; i++)
        {
            Assert.Equal(presets[i].Name, loaded[i].Name);
            Assert.Equal(presets[i].Entries, loaded[i].Entries);
        }
    }

    [Fact]
    public void SaveOfAnEmptyListWritesAnEmptyArray()
    {
        Assert.True(AuraCustomPresets.Save(new List<CustomPreset>()));

        Assert.Equal("[]", TestData.ReadRaw(AuraCustomPresets.FileName));
        Assert.Empty(AuraCustomPresets.Load());
    }

    [Fact]
    public void SaveOfAPresetWithoutEntriesIsDroppedOnTheNextLoad()
    {
        Assert.True(AuraCustomPresets.Save(new List<CustomPreset>
        {
            new("Empty", new List<CustomPresetEntry>()),
            new("Full", new List<CustomPresetEntry> { Entry() }),
        }));

        Assert.Equal("Full", Assert.Single(AuraCustomPresets.Load()).Name);
    }

    [Fact]
    public void SaveWritesEveryFieldOfAnEntryUnderItsName()
    {
        Assert.True(AuraCustomPresets.Save(new List<CustomPreset>
        {
            new("Calm", new List<CustomPresetEntry> { Entry("dev#1", 2, "Strip", 5, 10, 20, 30, 75) }),
        }));

        using JsonDocument document = JsonDocument.Parse(TestData.ReadRaw(AuraCustomPresets.FileName)!);
        JsonElement preset = document.RootElement[0];
        Assert.Equal("Calm", preset.GetProperty("name").GetString());
        JsonElement entry = preset.GetProperty("entries")[0];
        Assert.Equal(
            new[] { "deviceKey", "channel", "label", "mode", "red", "green", "blue", "brightness" },
            entry.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("dev#1", entry.GetProperty("deviceKey").GetString());
        Assert.Equal(2, entry.GetProperty("channel").GetInt32());
        Assert.Equal(75, entry.GetProperty("brightness").GetInt32());
    }

    [Fact]
    public void SaveDoesNotValidateButLoadDoes()
    {
        Assert.True(AuraCustomPresets.Save(new List<CustomPreset>
        {
            new("P", new List<CustomPresetEntry> { Entry(mode: 200, brightness: 250), Entry(channel: -9) }),
        }));

        CustomPresetEntry only = Assert.Single(Assert.Single(AuraCustomPresets.Load()).Entries);
        Assert.Equal(AuraState.ModeStatic, only.Mode);
        Assert.Equal(AuraState.MaxBrightness, only.Brightness);
    }

    [Fact]
    public void SaveReplacesTheOldContentAndLeavesNoTempFile()
    {
        for (int i = 0; i < 10; i++)
        {
            Assert.True(AuraCustomPresets.Save(new List<CustomPreset>
            {
                new("P" + i, new List<CustomPresetEntry> { Entry() }),
            }));
        }

        Assert.Empty(TempFiles());
        Assert.Equal("P9", Assert.Single(AuraCustomPresets.Load()).Name);
    }

    [Fact]
    public void SaveFailsWithoutAnExceptionWhenTheFileCannotBeReplaced()
    {
        TestData.WriteRaw(AuraCustomPresets.FileName, "[]");
        using var hold = new FileStream(AuraFiles.PathTo(AuraCustomPresets.FileName), FileMode.Open,
            FileAccess.Read, FileShare.Read);

        bool saved = AuraCustomPresets.Save(new List<CustomPreset>
        {
            new("P", new List<CustomPresetEntry> { Entry() }),
        });

        Assert.False(saved);
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void SaveOverADamagedFileKeepsACopyOfIt()
    {
        TestData.WriteRaw(AuraCustomPresets.FileName, "[{\"name\": oops");
        Assert.Empty(AuraCustomPresets.Load());

        Assert.True(AuraCustomPresets.Save(new List<CustomPreset>
        {
            new("P", new List<CustomPresetEntry> { Entry() }),
        }));

        Assert.Equal("[{\"name\": oops", File.ReadAllText(Assert.Single(BadCopies("presets"))));
        Assert.Single(AuraCustomPresets.Load());
    }

    [Fact]
    public void SaveOfAFullFileStaysBelowTheReadCap()
    {
        // A fully used preset list must still be small enough for Load to read back.
        var entries = Enumerable.Range(0, 16)
            .Select(i => Entry(new string('k', 120), i, new string('l', AuraFiles.MaxChannelLabel)))
            .ToList();
        var presets = Enumerable.Range(0, 20)
            .Select(i => new CustomPreset(new string((char)('a' + i), AuraFiles.MaxPresetName), entries))
            .ToList();

        Assert.True(AuraCustomPresets.Save(presets));

        Assert.True(new FileInfo(AuraFiles.PathTo(AuraCustomPresets.FileName)).Length < 256 * 1024);
        Assert.Equal(20, AuraCustomPresets.Load().Count);
    }

    [Fact]
    public void LoadOfAFileOverTheReadCapGivesAnEmptyList()
    {
        string padding = new string('x', 256 * 1024);

        Assert.Empty(Load(One("\"label\":\"" + padding + "\"")));
    }

    // ---- reset and concurrency ----

    [Fact]
    public void AResetDeletesThePresetsAndTheNextLoadIsEmpty()
    {
        Assert.True(AuraCustomPresets.Save(new List<CustomPreset>
        {
            new("P", new List<CustomPresetEntry> { Entry() }),
        }));

        Assert.Equal(AuraFiles.ResetResult.Done, AuraFiles.ResetAll());

        Assert.Empty(AuraCustomPresets.Load());
        Assert.False(File.Exists(AuraFiles.PathTo(AuraCustomPresets.FileName)));
    }

    [Fact]
    public void WritersAtOnceLeaveOneWholeValidFile()
    {
        const int threads = 6;
        Thread[] writers = Enumerable.Range(0, threads).Select(n => new Thread(() =>
        {
            for (int i = 0; i < 15; i++)
            {
                AuraCustomPresets.Save(new List<CustomPreset>
                {
                    new("W" + n, new List<CustomPresetEntry> { Entry(label: "w" + n + "-" + i) }),
                });
            }
        })).ToArray();

        foreach (Thread writer in writers)
        {
            writer.Start();
        }

        foreach (Thread writer in writers)
        {
            writer.Join();
        }

        CustomPreset loaded = Assert.Single(AuraCustomPresets.Load());
        Assert.Matches(@"^W\d$", loaded.Name);
        Assert.Empty(TempFiles());
        Assert.Empty(BadCopies("presets"));
    }
}
