using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;
using Xunit;

namespace AuraToggle.Tests;

public sealed class AuraSettingsTests : PersistenceTest
{
    private const int CtrlA = (HotKey.ModControl << 8) | 0x41;

    private static AuraSettings Load(string json)
    {
        TestData.WriteRaw(AuraSettings.FileName, json);
        return AuraSettings.Load();
    }

    private static AuraSettings Filled() => new(
        MinimiseOnClose: true, StartAction: "rainbow", CloseAfterStart: true, Animate: false,
        AlwaysOnTop: true, Language: "pt-BR", HotkeyEnabled: true, Hotkey: CtrlA,
        WindowX: -1200, WindowY: 340, WindowWidth: 777);

    // ---- defaults and constants ----

    [Fact]
    public void TheDefaultsAreTheQuietFirstRunOnes()
    {
        AuraSettings d = AuraSettings.Default;

        Assert.False(d.MinimiseOnClose);
        Assert.Equal("", d.StartAction);
        Assert.False(d.CloseAfterStart);
        Assert.True(d.Animate);
        Assert.False(d.AlwaysOnTop);
        Assert.Equal("", d.Language);
        Assert.False(d.HotkeyEnabled);
        Assert.Equal(HotKey.Default, d.Hotkey);
        Assert.Null(d.WindowX);
        Assert.Null(d.WindowY);
        Assert.Null(d.WindowWidth);
    }

    [Fact]
    public void StoredNamesAndMarkersStayAsShipped()
    {
        Assert.Equal("settings.json", AuraSettings.FileName);
        Assert.Equal("", AuraSettings.StartActionNone);
        Assert.Equal("off", AuraSettings.StartActionOff);
        Assert.Equal("custom:", AuraSettings.StartActionCustomPrefix);
        Assert.Equal("", AuraSettings.LanguageAuto);
        Assert.Equal("-autostart", AuraSettings.AutoStartArgument);
    }

    // ---- custom start actions ----

    [Theory]
    [InlineData("Calm", "custom:Calm")]
    [InlineData("", "custom:")]
    [InlineData("a:b", "custom:a:b")]
    [InlineData("Café 😀", "custom:Café 😀")]
    public void CustomStartActionPutsThePrefixInFront(string preset, string action)
    {
        Assert.Equal(action, AuraSettings.CustomStartAction(preset));
        Assert.Equal(preset, AuraSettings.CustomStartPreset(action));
    }

    [Theory]
    [InlineData("")]
    [InlineData("off")]
    [InlineData("rainbow")]
    [InlineData("Custom:Calm")]
    [InlineData("CUSTOM:Calm")]
    [InlineData(" custom:Calm")]
    [InlineData("custom")]
    [InlineData("customs:x")]
    public void CustomStartPresetIsNullForAnyOtherAction(string action)
    {
        Assert.Null(AuraSettings.CustomStartPreset(action));
    }

    [Fact]
    public void CustomStartPresetKeepsTheNameExactly()
    {
        Assert.Equal("  Calm  ", AuraSettings.CustomStartPreset("custom:  Calm  "));
        Assert.Equal("a:b:c", AuraSettings.CustomStartPreset("custom:a:b:c"));
    }

    // ---- Load ----

    [Fact]
    public void LoadWithoutAFileGivesTheDefaults()
    {
        Assert.Equal(AuraSettings.Default, AuraSettings.Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"animate\":false,")]
    [InlineData("garbage")]
    [InlineData("[]")]
    [InlineData("[{\"animate\":false}]")]
    [InlineData("\"x\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("7")]
    public void LoadOfADamagedFileGivesTheDefaults(string json)
    {
        Assert.Equal(AuraSettings.Default, Load(json));
    }

    [Fact]
    public void LoadOfAnEmptyObjectGivesTheDefaults()
    {
        Assert.Equal(AuraSettings.Default, Load("{}"));
    }

    [Fact]
    public void LoadReadsEveryField()
    {
        AuraSettings loaded = Load("""
            {"minimiseOnClose":true,"startAction":"rainbow","closeAfterStart":true,"animate":false,
             "alwaysOnTop":true,"language":"pt-BR","hotkeyEnabled":true,"hotkey":577,
             "windowX":-1200,"windowY":340,"windowWidth":777}
            """);

        Assert.Equal(Filled(), loaded);
    }

    [Theory]
    [InlineData("minimiseOnClose")]
    [InlineData("closeAfterStart")]
    [InlineData("alwaysOnTop")]
    [InlineData("hotkeyEnabled")]
    public void LoadReadsOnlyARealBooleanForTheOffSwitches(string name)
    {
        foreach (string wrong in new[] { "\"true\"", "1", "null", "[]", "{}" })
        {
            AuraSettings loaded = Load($"{{\"{name}\":{wrong}}}");

            Assert.Equal(AuraSettings.Default, loaded);
        }

        Assert.NotEqual(AuraSettings.Default, Load($"{{\"{name}\":true}}"));
    }

    [Fact]
    public void LoadReadsAnAnimateThatIsNotABooleanAsOn()
    {
        Assert.True(Load("{\"animate\":\"no\"}").Animate);
        Assert.True(Load("{\"animate\":0}").Animate);
        Assert.False(Load("{\"animate\":false}").Animate);
    }

    [Fact]
    public void LoadFallsBackFieldByField()
    {
        AuraSettings loaded = Load("""{"animate":false,"language":5,"hotkey":"x","windowX":"left","windowWidth":600}""");

        Assert.False(loaded.Animate);
        Assert.Equal("", loaded.Language);
        Assert.Equal(HotKey.Default, loaded.Hotkey);
        Assert.Null(loaded.WindowX);
        Assert.Equal(600, loaded.WindowWidth);
    }

    [Fact]
    public void LoadIgnoresFieldNamesInTheWrongCase()
    {
        Assert.Equal(AuraSettings.Default, Load("""{"Animate":false,"MinimiseOnClose":true,"STARTACTION":"off"}"""));
    }

    [Fact]
    public void LoadIgnoresUnknownFields()
    {
        Assert.Equal(AuraSettings.Default with { AlwaysOnTop = true },
            Load("""{"theme":"dark","list":[1,2],"alwaysOnTop":true}"""));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("pt-BR")]
    [InlineData("zh")]
    [InlineData("xx-unknown")]
    [InlineData("Café ゲ")]
    public void LoadKeepsAnyLanguageTextForTheStringsTableToJudge(string language)
    {
        Assert.Equal(language, Load("{\"language\":\"" + language + "\"}").Language);
    }

    [Theory]
    [InlineData("windowX", "-32000", -32000)]
    [InlineData("windowX", "0", 0)]
    [InlineData("windowX", "2147483647", int.MaxValue)]
    [InlineData("windowY", "-1", -1)]
    [InlineData("windowWidth", "420", 420)]
    [InlineData("windowWidth", "0", 0)]
    public void LoadKeepsAWholeWindowNumber(string name, string value, int expected)
    {
        AuraSettings loaded = Load($"{{\"{name}\":{value}}}");

        int? actual = name switch { "windowX" => loaded.WindowX, "windowY" => loaded.WindowY, _ => loaded.WindowWidth };
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("100.5")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("\"100\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[100]")]
    public void LoadReadsAWindowNumberThatIsNotAnIntAsNeverSet(string value)
    {
        AuraSettings loaded = Load($"{{\"windowX\":{value},\"windowY\":{value},\"windowWidth\":{value}}}");

        Assert.Null(loaded.WindowX);
        Assert.Null(loaded.WindowY);
        Assert.Null(loaded.WindowWidth);
    }

    // ---- start action validation ----

    [Theory]
    [InlineData("")]
    [InlineData("off")]
    [InlineData("static")]
    [InlineData("breathing")]
    [InlineData("flashing")]
    [InlineData("spectrum-cycle")]
    [InlineData("rainbow")]
    [InlineData("rainbow-breathing")]
    [InlineData("chase-fade")]
    [InlineData("chase")]
    [InlineData("wave")]
    [InlineData("custom:Calm")]
    [InlineData("custom:")]
    [InlineData("custom:a name that is not there")]
    public void LoadKeepsEveryStartActionThePanelCanStore(string action)
    {
        Assert.Equal(action, Load("{\"startAction\":\"" + action + "\"}").StartAction);
    }

    [Fact]
    public void LoadKeepsEveryBuiltInEffectKeyAsAStartAction()
    {
        foreach (AuraPreset preset in AuraPresets.All)
        {
            Assert.Equal(preset.Key, Load("{\"startAction\":\"" + preset.Key + "\"}").StartAction);
        }
    }

    [Theory]
    [InlineData("Rainbow")]
    [InlineData("RAINBOW")]
    [InlineData("rainbow ")]
    [InlineData(" off")]
    [InlineData("Off")]
    [InlineData("spectrum cycle")]
    [InlineData("spectrum_cycle")]
    [InlineData("on")]
    [InlineData("toggle")]
    [InlineData("Regenbogen")]
    [InlineData("Custom:Calm")]
    [InlineData("custom")]
    [InlineData("nonsense")]
    public void LoadReadsATypoedStartActionAsLeaveTheLightingAlone(string action)
    {
        // A start action runs at logon with nobody looking: no quiet match to a nearby effect.
        Assert.Equal(AuraSettings.StartActionNone, Load("{\"startAction\":\"" + action + "\"}").StartAction);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[\"off\"]")]
    [InlineData("{}")]
    public void LoadReadsAStartActionThatIsNotTextAsLeaveTheLightingAlone(string value)
    {
        Assert.Equal("", Load("{\"startAction\":" + value + "}").StartAction);
    }

    // ---- hotkey validation ----

    [Theory]
    [InlineData(HotKey.ModControl, 0x41)]
    [InlineData(HotKey.ModControl | HotKey.ModAlt, 0x4C)]
    [InlineData(HotKey.ModShift, 0x70)]
    [InlineData(HotKey.ModWin | HotKey.ModShift, 0x70)]
    [InlineData(HotKey.ModControl | HotKey.ModAlt | HotKey.ModShift | HotKey.ModWin, 0x7B)]
    [InlineData(HotKey.ModAlt, 0x08)]
    [InlineData(HotKey.ModAlt, 0x30)]
    [InlineData(HotKey.ModControl, 0x20)]
    public void LoadKeepsAUsableHotkey(int modifiers, int key)
    {
        int packed = HotKey.Pack(modifiers, key);

        Assert.Equal(packed, Load("{\"hotkey\":" + packed + "}").Hotkey);
    }

    [Theory]
    [InlineData(0x41)]
    [InlineData(0x4C)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(0x0200)]
    [InlineData(0x0100)]
    [InlineData(0x10000)]
    public void LoadReplacesAHotkeyWithoutAModifierOrAKey(int packed)
    {
        // A bare key registered globally would swallow ordinary typing system-wide.
        Assert.Equal(HotKey.Default, Load("{\"hotkey\":" + packed + "}").Hotkey);
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    [InlineData(0x04)]
    [InlineData(0x05)]
    [InlineData(0x06)]
    [InlineData(0x07)]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    [InlineData(0xA0)]
    [InlineData(0xA1)]
    [InlineData(0xA2)]
    [InlineData(0xA3)]
    [InlineData(0xA4)]
    [InlineData(0xA5)]
    [InlineData(0xFF)]
    [InlineData(0xE8)]
    [InlineData(0x0E)]
    public void LoadReplacesAHotkeyWhoseKeyNobodyCanPress(int key)
    {
        int packed = HotKey.Pack(HotKey.ModControl, key);

        Assert.Equal(HotKey.Default, Load("{\"hotkey\":" + packed + "}").Hotkey);
    }

    [Fact]
    public void LoadDropsModifierBitsThatAreNotRealModifiers()
    {
        // Bit 4 of the modifier byte is no known modifier: it goes, Control stays.
        int packed = ((0x10 | HotKey.ModControl) << 8) | 0x41;

        Assert.Equal(HotKey.Pack(HotKey.ModControl, 0x41), Load("{\"hotkey\":" + packed + "}").Hotkey);
    }

    [Fact]
    public void LoadReplacesAHotkeyWhoseOnlyModifierBitsAreUnknown()
    {
        int packed = (0xF0 << 8) | 0x41;

        Assert.Equal(HotKey.Default, Load("{\"hotkey\":" + packed + "}").Hotkey);
    }

    [Theory]
    [InlineData("\"577\"")]
    [InlineData("57.7")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("99999999999")]
    public void LoadReplacesAHotkeyThatIsNotAnInt(string value)
    {
        Assert.Equal(HotKey.Default, Load("{\"hotkey\":" + value + "}").Hotkey);
    }

    // ---- Save ----

    [Fact]
    public void SaveThenLoadGivesTheSameSettings()
    {
        Filled().Save();

        Assert.Equal(Filled(), AuraSettings.Load());
    }

    [Fact]
    public void SaveThenLoadOfTheDefaultsGivesTheDefaults()
    {
        AuraSettings.Default.Save();

        Assert.Equal(AuraSettings.Default, AuraSettings.Load());
    }

    [Theory]
    [InlineData("custom:Calm")]
    [InlineData("custom:Café 😀")]
    [InlineData("off")]
    [InlineData("wave")]
    public void SaveThenLoadKeepsAStartAction(string action)
    {
        var settings = AuraSettings.Default with { StartAction = action };

        settings.Save();

        Assert.Equal(settings, AuraSettings.Load());
    }

    [Fact]
    public void SaveThenLoadKeepsAFullLengthCustomPresetStartAction()
    {
        string action = AuraSettings.CustomStartAction(new string('p', AuraFiles.MaxPresetName));
        var settings = AuraSettings.Default with { StartAction = action };

        settings.Save();

        Assert.Equal(action, AuraSettings.Load().StartAction);
    }

    [Fact]
    public void SaveWritesTheWindowPlaceOnlyAsAPair()
    {
        (AuraSettings.Default with { WindowX = 10 }).Save();
        using (JsonDocument document = JsonDocument.Parse(TestData.ReadRaw(AuraSettings.FileName)!))
        {
            Assert.False(document.RootElement.TryGetProperty("windowX", out _));
            Assert.False(document.RootElement.TryGetProperty("windowY", out _));
        }

        (AuraSettings.Default with { WindowY = 10 }).Save();
        using (JsonDocument document = JsonDocument.Parse(TestData.ReadRaw(AuraSettings.FileName)!))
        {
            Assert.False(document.RootElement.TryGetProperty("windowX", out _));
        }

        (AuraSettings.Default with { WindowX = 10, WindowY = 20 }).Save();
        AuraSettings loaded = AuraSettings.Load();
        Assert.Equal(10, loaded.WindowX);
        Assert.Equal(20, loaded.WindowY);
    }

    [Fact]
    public void SaveWritesTheWidthOnItsOwn()
    {
        (AuraSettings.Default with { WindowWidth = 500 }).Save();

        AuraSettings loaded = AuraSettings.Load();
        Assert.Equal(500, loaded.WindowWidth);
        Assert.Null(loaded.WindowX);
    }

    [Fact]
    public void SaveWritesNoWindowFieldsWhenNothingIsKnown()
    {
        AuraSettings.Default.Save();

        using JsonDocument document = JsonDocument.Parse(TestData.ReadRaw(AuraSettings.FileName)!);
        string[] names = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(
            new[]
            {
                "minimiseOnClose", "startAction", "closeAfterStart", "animate", "alwaysOnTop", "language",
                "hotkeyEnabled", "hotkey",
            },
            names);
    }

    [Fact]
    public void SaveDoesNotValidateButLoadDoes()
    {
        (AuraSettings.Default with { StartAction = "bogus", Hotkey = 0x41 }).Save();

        Assert.Contains("bogus", TestData.ReadRaw(AuraSettings.FileName));
        AuraSettings loaded = AuraSettings.Load();
        Assert.Equal("", loaded.StartAction);
        Assert.Equal(HotKey.Default, loaded.Hotkey);
    }

    [Fact]
    public void SaveLeavesNoTempFileAndReplacesTheOldContent()
    {
        for (int i = 0; i < 10; i++)
        {
            (AuraSettings.Default with { WindowWidth = i }).Save();
        }

        Assert.Empty(TempFiles());
        Assert.Equal(9, AuraSettings.Load().WindowWidth);
    }

    [Fact]
    public void SaveOverADamagedFileKeepsACopyOfIt()
    {
        // A hand edit that broke the file is copied aside before the first save replaces it.
        TestData.WriteRaw(AuraSettings.FileName, "{\"animate\": nope");
        Assert.Equal(AuraSettings.Default, AuraSettings.Load());

        (AuraSettings.Default with { Animate = false }).Save();

        Assert.Equal("{\"animate\": nope", File.ReadAllText(Assert.Single(BadCopies("settings"))));
        Assert.False(AuraSettings.Load().Animate);
    }

    // ---- FollowPresetRename ----

    [Fact]
    public void ARenamedPresetStaysTheStartAction()
    {
        (Filled() with { StartAction = AuraSettings.CustomStartAction("Old") }).Save();

        AuraSettings.FollowPresetRename("Old", "New");

        AuraSettings after = AuraSettings.Load();
        Assert.Equal("custom:New", after.StartAction);
        Assert.Equal(Filled() with { StartAction = "custom:New" }, after);
    }

    [Fact]
    public void ADeletedPresetFallsBackToLeavingTheLightingAlone()
    {
        (Filled() with { StartAction = AuraSettings.CustomStartAction("Old") }).Save();

        AuraSettings.FollowPresetRename("Old", null);

        Assert.Equal(Filled() with { StartAction = "" }, AuraSettings.Load());
    }

    [Fact]
    public void AnotherPresetsRenameLeavesTheSettingsFileUntouched()
    {
        (Filled() with { StartAction = AuraSettings.CustomStartAction("Mine") }).Save();
        string before = TestData.ReadRaw(AuraSettings.FileName)!;

        AuraSettings.FollowPresetRename("Other", "Renamed");
        AuraSettings.FollowPresetRename("Other", null);

        Assert.Equal(before, TestData.ReadRaw(AuraSettings.FileName));
    }

    [Fact]
    public void ABuiltInStartActionIsNeverTouchedByARename()
    {
        (Filled() with { StartAction = "wave" }).Save();

        AuraSettings.FollowPresetRename("wave", "x");
        AuraSettings.FollowPresetRename("", "x");

        Assert.Equal("wave", AuraSettings.Load().StartAction);
    }

    [Fact]
    public void ARenameToTheSameNameWritesNothing()
    {
        AuraSettings.FollowPresetRename("Same", "Same");

        Assert.Null(TestData.ReadRaw(AuraSettings.FileName));
    }

    [Fact]
    public void ARenameWithoutASettingsFileCreatesNone()
    {
        AuraSettings.FollowPresetRename("Old", "New");
        AuraSettings.FollowPresetRename("Old", null);

        Assert.Null(TestData.ReadRaw(AuraSettings.FileName));
    }

    [Fact]
    public void ARenameMatchesTheNameExactlyNotByCase()
    {
        (AuraSettings.Default with { StartAction = "custom:calm" }).Save();

        AuraSettings.FollowPresetRename("Calm", "Other");

        Assert.Equal("custom:calm", AuraSettings.Load().StartAction);
    }

    [Fact]
    public void ARenameLeavesADamagedSettingsFileAlone()
    {
        TestData.WriteRaw(AuraSettings.FileName, "{broken");

        AuraSettings.FollowPresetRename("Old", "New");

        Assert.Equal("{broken", TestData.ReadRaw(AuraSettings.FileName));
        Assert.Empty(BadCopies("settings"));
    }

    [Fact]
    public void ARenameWaitsForTheFilesLockInsteadOfRacingASave()
    {
        // A read-modify-write without the lock loses whichever of two concurrent changes saved first.
        (AuraSettings.Default with { StartAction = "custom:Old" }).Save();
        var holder = new LockHolder(AuraFiles.Lock);
        var rename = new Thread(() => AuraSettings.FollowPresetRename("Old", "New")) { IsBackground = true };
        rename.Start();

        Assert.True(Poll.Until(() => rename.ThreadState.HasFlag(ThreadState.WaitSleepJoin)));
        Assert.Equal("custom:Old", AuraSettings.Load().StartAction);

        holder.Dispose();
        rename.Join();

        Assert.Equal("custom:New", AuraSettings.Load().StartAction);
    }

    [Fact]
    public void ARenamedPresetKeepsEveryOtherSettingThroughManySaves()
    {
        (Filled() with { StartAction = "custom:A" }).Save();

        for (int i = 0; i < 20; i++)
        {
            AuraSettings.FollowPresetRename("A", "B");
            AuraSettings.FollowPresetRename("B", "A");
        }

        Assert.Equal(Filled() with { StartAction = "custom:A" }, AuraSettings.Load());
        Assert.Empty(TempFiles());
    }

    // ---- AutoStart in the review store ----

    [Fact]
    public void AutoStartInTheReviewStoreNeverReachesTheRegistry()
    {
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        object? before = ReadRunValue(runKey);
        bool original = AuraSettings.AutoStart;
        try
        {
            AuraSettings.AutoStart = false;
            Assert.False(AuraSettings.AutoStart);

            AuraSettings.AutoStart = true;
            Assert.True(AuraSettings.AutoStart);
        }
        finally
        {
            AuraSettings.AutoStart = original;
        }

        Assert.Equal(before, ReadRunValue(runKey));
    }

    private static object? ReadRunValue(string keyPath)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue("AuraToggle");
    }
}
