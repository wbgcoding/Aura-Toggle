using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace AuraToggle.Tests;

public sealed class AuraChannelStatesTests : PersistenceTest
{
    private static readonly string[] None = Array.Empty<string>();

    private static int Today() => (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400);

    /// <summary>Runs a day-sensitive body again when the UTC day turned over while it ran.</summary>
    private static void OnOneDay(Action<int> body)
    {
        for (int attempt = 0; ; attempt++)
        {
            int day = Today();
            TestData.Clear();
            body(day);
            if (Today() == day || attempt >= 3)
            {
                return;
            }
        }
    }

    private static Dictionary<string, ChannelLighting> Load(string json)
    {
        TestData.WriteRaw(AuraChannelStates.FileName, json);
        return AuraChannelStates.All();
    }

    private static ChannelLighting? Get(string deviceKey, int channel) =>
        AuraChannelStates.Get(AuraChannelStates.All(), deviceKey, channel);

    private static (string, int)[] Targets(string deviceKey, params int[] channels) =>
        channels.Select(c => (deviceKey, c)).ToArray();

    private static string Record(int seen, int mode = 1) =>
        "{\"mode\":" + mode + ",\"red\":1,\"green\":2,\"blue\":3,\"on\":true,\"brightness\":50,\"seen\":" + seen + "}";

    private static JsonDocument Stored() => JsonDocument.Parse(TestData.ReadRaw(AuraChannelStates.FileName)!);

    private static readonly ChannelLighting Blue = new(AuraState.ModeStatic, 0, 0, 255);
    private static readonly ChannelLighting Board = new(5, 9, 8, 7, On: true, Brightness: 0);

    // ---- constants and the empty states ----

    [Fact]
    public void TheStoredFileNameStaysAsShipped()
    {
        Assert.Equal("channel-state.json", AuraChannelStates.FileName);
    }

    [Fact]
    public void WithoutAFileThereAreNoStates()
    {
        Assert.Empty(AuraChannelStates.All());
        Assert.Null(Get("dev", 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"dev|0\":{\"mode\":1")]
    [InlineData("garbage")]
    [InlineData("[]")]
    [InlineData("[{\"mode\":1}]")]
    [InlineData("\"x\"")]
    [InlineData("null")]
    [InlineData("7")]
    [InlineData("true")]
    public void ADamagedFileOrAWrongRootGivesNoStates(string json)
    {
        Assert.Empty(Load(json));
    }

    [Fact]
    public void AnEmptyObjectGivesNoStates()
    {
        Assert.Empty(Load("{}"));
    }

    [Fact]
    public void ARecordDefaultsAreTheOnesBeforeTheFieldsExisted()
    {
        var look = new ChannelLighting(1, 2, 3, 4);

        Assert.True(look.On);
        Assert.Equal(0, look.Brightness);
        Assert.Equal(0, look.Seen);
    }

    // ---- reading ----

    [Fact]
    public void LoadReadsEveryFieldOfARecord()
    {
        Dictionary<string, ChannelLighting> states = Load(
            """{"dev#1|2":{"mode":5,"red":10,"green":20,"blue":30,"on":false,"brightness":75,"seen":20000}}""");

        Assert.Equal(new ChannelLighting(5, 10, 20, 30, On: false, Brightness: 75, Seen: 20000),
            AuraChannelStates.Get(states, "dev#1", 2));
    }

    [Fact]
    public void LoadOfAnEmptyRecordGivesTheOldFormatDefaults()
    {
        ChannelLighting look = Assert.Single(Load("""{"dev|0":{}}""")).Value;

        Assert.Equal(new ChannelLighting(AuraState.ModeOff, 0, 0, 0, On: true, Brightness: 0, Seen: 0), look);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("\"x\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[]")]
    public void LoadSkipsAValueThatIsNotARecordButKeepsTheRest(string value)
    {
        Dictionary<string, ChannelLighting> states = Load("{\"bad|0\":" + value + ",\"ok|0\":{\"mode\":1}}");

        Assert.Equal(new[] { "ok|0" }, states.Keys.ToArray());
    }

    [Theory]
    [InlineData("red")]
    [InlineData("green")]
    [InlineData("blue")]
    [InlineData("brightness")]
    [InlineData("mode")]
    public void LoadReadsAByteFieldOfAnotherKindAsItsFallback(string field)
    {
        foreach (string wrong in new[] { "\"7\"", "true", "null", "[]", "{}", "1.5", "-1", "256" })
        {
            ChannelLighting look = Assert.Single(Load("{\"d|0\":{\"" + field + "\":" + wrong + "}}")).Value;

            // Mode falls back to static, never to "off"; a whole brightness out of range is held
            // to the slider (-1 the dimmest, 256 the brightest), anything else reads as 0.
            int expected = field switch
            {
                "mode" => AuraState.ModeStatic,
                "brightness" when wrong == "-1" => AuraState.MinBrightness,
                "brightness" when wrong == "256" => AuraState.MaxBrightness,
                _ => 0,
            };
            Assert.Equal(expected, field switch
            {
                "red" => look.Red,
                "green" => look.Green,
                "blue" => look.Blue,
                "brightness" => look.Brightness,
                _ => look.Mode,
            });
        }
    }

    [Theory]
    [InlineData("\"false\"")]
    [InlineData("0")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void LoadReadsAnOnFlagThatIsNotABooleanAsOn(string wrong)
    {
        Assert.True(Assert.Single(Load("{\"d|0\":{\"on\":" + wrong + "}}")).Value.On);
    }

    [Fact]
    public void LoadKeepsAnExplicitOff()
    {
        Assert.False(Assert.Single(Load("""{"d|0":{"on":false}}""")).Value.On);
        Assert.True(Assert.Single(Load("""{"d|0":{"on":true}}""")).Value.On);
    }

    [Theory]
    [InlineData("\"5\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void LoadReadsASeenThatIsNotAnIntAsNeverStamped(string wrong)
    {
        Assert.Equal(0, Assert.Single(Load("{\"d|0\":{\"seen\":" + wrong + "}}")).Value.Seen);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(255)]
    public void LoadKeepsTheFullByteRangeForEachColour(int value)
    {
        ChannelLighting look = Assert.Single(Load(
            "{\"d|0\":{\"red\":" + value + ",\"green\":" + value + ",\"blue\":" + value + "}}")).Value;

        Assert.Equal((byte)value, look.Red);
        Assert.Equal((byte)value, look.Green);
        Assert.Equal((byte)value, look.Blue);
    }

    [Fact]
    public void LoadKeepsEveryModeThisBuildKnowsAndOff()
    {
        foreach (byte mode in AuraPresets.All.Select(p => p.Mode).Append(AuraState.ModeOff))
        {
            Assert.Equal(mode, Assert.Single(Load("{\"d|0\":{\"mode\":" + mode + "}}")).Value.Mode);
        }
    }

    [Fact]
    public void LoadReplacesEveryUnknownModeWithStatic()
    {
        // The window must never name an effect the board does not run while sending something else.
        byte[] known = AuraPresets.All.Select(p => p.Mode).Append(AuraState.ModeOff).ToArray();

        foreach (int mode in Enumerable.Range(0, 256).Where(m => !known.Contains((byte)m)))
        {
            Assert.Equal(AuraState.ModeStatic, Assert.Single(Load("{\"d|0\":{\"mode\":" + mode + "}}")).Value.Mode);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(11, 11)]
    [InlineData(55, 55)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(255, 100)]
    public void LoadKeepsZeroAsFollowTheBoardAndHoldsTheRestToTheSliderRange(int stored, int expected)
    {
        Assert.Equal(expected, Assert.Single(Load("{\"d|0\":{\"brightness\":" + stored + "}}")).Value.Brightness);
    }

    [Fact]
    public void LoadKeepsKeysItDoesNotRecogniseForALaterSave()
    {
        Dictionary<string, ChannelLighting> states = Load("""{"anything at all":{"mode":1},"|":{"mode":1}}""");

        Assert.Equal(2, states.Count);
    }

    [Fact]
    public void AKeyThatRepeatsInTheFileTakesTheLastRecord()
    {
        Dictionary<string, ChannelLighting> states = Load("""{"d|0":{"red":1},"d|0":{"red":2}}""");

        Assert.Equal(2, Assert.Single(states).Value.Red);
    }

    [Fact]
    public void LoadDoesNotAgeAnythingOut()
    {
        Dictionary<string, ChannelLighting> states = Load(
            """{"gone|0":{"mode":1,"seen":1},"gone|1":{"mode":1,"seen":2}}""");

        Assert.Equal(2, states.Count);
    }

    [Fact]
    public void GetMatchesTheKeyExactly()
    {
        Dictionary<string, ChannelLighting> states = Load("""{"Dev#1|0":{"mode":1}}""");

        Assert.NotNull(AuraChannelStates.Get(states, "Dev#1", 0));
        Assert.Null(AuraChannelStates.Get(states, "dev#1", 0));
        Assert.Null(AuraChannelStates.Get(states, "Dev#1", 1));
        Assert.Null(AuraChannelStates.Get(new Dictionary<string, ChannelLighting>(), "Dev#1", 0));
    }

    [Fact]
    public void AllGivesAFreshDictionaryEachCall()
    {
        TestData.WriteRaw(AuraChannelStates.FileName, """{"d|0":{"mode":1}}""");

        AuraChannelStates.All().Clear();

        Assert.Single(AuraChannelStates.All());
    }

    [Fact]
    public void AStatesFileOverTheReadCapGivesNoStates()
    {
        Assert.Empty(Load("{\"d|" + new string('x', 256 * 1024) + "\":{\"mode\":1}}"));
    }

    // ---- Remember with one look ----

    [Fact]
    public void RememberRecordsTheLookForEveryTarget()
    {
        AuraChannelStates.Remember(Targets("dev", 0, 1, 2), Blue, new[] { "dev" });

        foreach (int channel in new[] { 0, 1, 2 })
        {
            Assert.Equal(Blue with { Seen = Today() }, Get("dev", channel));
        }
    }

    [Fact]
    public void RememberStampsTheRecordWithTodayAsDaysSinceTheEpoch()
    {
        OnOneDay(day =>
        {
            AuraChannelStates.Remember(Targets("dev", 0), Blue, new[] { "dev" });

            Assert.Equal(day, Get("dev", 0)!.Seen);
            using JsonDocument document = Stored();
            Assert.Equal(day, document.RootElement.GetProperty("dev|0").GetProperty("seen").GetInt32());
        });
    }

    [Fact]
    public void RememberWritesEveryFieldOfARecordUnderItsName()
    {
        AuraChannelStates.Remember(Targets("dev", 0), new ChannelLighting(5, 10, 20, 30, false, 60), new[] { "dev" });

        using JsonDocument document = Stored();
        JsonElement record = document.RootElement.GetProperty("dev|0");
        Assert.Equal(
            new[] { "mode", "red", "green", "blue", "on", "brightness", "seen" },
            record.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(5, record.GetProperty("mode").GetInt32());
        Assert.False(record.GetProperty("on").GetBoolean());
        Assert.Equal(60, record.GetProperty("brightness").GetInt32());
    }

    [Fact]
    public void RememberKeepsEveryOtherRecord()
    {
        AuraChannelStates.Remember(Targets("a", 0), Blue, new[] { "a", "b" });
        AuraChannelStates.Remember(Targets("b", 1), Board, new[] { "a", "b" });

        Assert.Equal(Blue with { Seen = Today() }, Get("a", 0));
        Assert.Equal(Board with { Seen = Today() }, Get("b", 1));
        Assert.Equal(2, AuraChannelStates.All().Count);
    }

    [Fact]
    public void RememberReplacesAnExistingRecordOfTheChannel()
    {
        AuraChannelStates.Remember(Targets("dev", 0), Blue, new[] { "dev" });

        AuraChannelStates.Remember(Targets("dev", 0), Board, new[] { "dev" });

        Assert.Equal(Board with { Seen = Today() }, Get("dev", 0));
    }

    [Fact]
    public void RememberWithoutABrightnessKeepsTheChannelsOwnOne()
    {
        // A new effect or colour carries no brightness; the slider value of that channel must survive.
        AuraChannelStates.Remember(Targets("dev", 0), Blue with { Brightness = 40 }, new[] { "dev" });

        AuraChannelStates.Remember(Targets("dev", 0), Board, new[] { "dev" });

        Assert.Equal(40, Get("dev", 0)!.Brightness);
        Assert.Equal(Board.Mode, Get("dev", 0)!.Mode);
    }

    [Fact]
    public void RememberWithABrightnessReplacesTheOldOne()
    {
        AuraChannelStates.Remember(Targets("dev", 0), Blue with { Brightness = 40 }, new[] { "dev" });

        AuraChannelStates.Remember(Targets("dev", 0), Board with { Brightness = 80 }, new[] { "dev" });

        Assert.Equal(80, Get("dev", 0)!.Brightness);
    }

    [Fact]
    public void RememberOfAChannelWithoutARecordKeepsTheLooksOwnBrightnessAtZero()
    {
        AuraChannelStates.Remember(Targets("dev", 0), Board, new[] { "dev" });

        Assert.Equal(0, Get("dev", 0)!.Brightness);
    }

    [Fact]
    public void RememberWithoutTargetsWritesNothing()
    {
        AuraChannelStates.Remember(Array.Empty<(string, int)>(), Blue, new[] { "dev" });

        Assert.Null(TestData.ReadRaw(AuraChannelStates.FileName));
    }

    [Fact]
    public void RememberWithoutTargetsLeavesAnExistingFileUntouched()
    {
        TestData.WriteRaw(AuraChannelStates.FileName, """{"gone|0":{"mode":1,"seen":1}}""");

        AuraChannelStates.Remember(Array.Empty<(string, int)>(), Blue, None);

        Assert.Equal("""{"gone|0":{"mode":1,"seen":1}}""", TestData.ReadRaw(AuraChannelStates.FileName));
    }

    // ---- Remember with one look per channel ----

    [Fact]
    public void RememberPerChannelRecordsEachLook()
    {
        AuraChannelStates.Remember(
            new[] { ("dev", 0, Blue), ("dev", 1, Board), ("other", -1, Blue with { On = false }) },
            new[] { "dev", "other" });

        Assert.Equal(Blue with { Seen = Today() }, Get("dev", 0));
        Assert.Equal(Board with { Seen = Today() }, Get("dev", 1));
        Assert.Equal(Blue with { On = false, Seen = Today() }, Get("other", -1));
    }

    [Fact]
    public void RememberPerChannelKeepsAnOwnBrightnessByDefault()
    {
        AuraChannelStates.Remember(Targets("dev", 0), Blue with { Brightness = 40 }, new[] { "dev" });

        AuraChannelStates.Remember(new[] { ("dev", 0, Board) }, new[] { "dev" });

        Assert.Equal(40, Get("dev", 0)!.Brightness);
    }

    [Fact]
    public void RememberPerChannelWithoutKeepingBrightnessLetsZeroStick()
    {
        // The board-wide slider hands every channel back to "follow the board": zero must not be read as unset.
        AuraChannelStates.Remember(Targets("dev", 0), Blue with { Brightness = 40 }, new[] { "dev" });

        AuraChannelStates.Remember(new[] { ("dev", 0, Board) }, new[] { "dev" }, keepBrightness: false);

        Assert.Equal(0, Get("dev", 0)!.Brightness);
    }

    [Fact]
    public void RememberPerChannelWithoutKeepingBrightnessStoresTheGivenOne()
    {
        AuraChannelStates.Remember(new[] { ("dev", 0, Board with { Brightness = 70 }) }, new[] { "dev" }, keepBrightness: false);

        Assert.Equal(70, Get("dev", 0)!.Brightness);
    }

    [Fact]
    public void RememberPerChannelTakesTheLastLookOfARepeatedChannel()
    {
        AuraChannelStates.Remember(
            new[] { ("dev", 0, Blue), ("dev", 0, Board) }, new[] { "dev" }, keepBrightness: false);

        Assert.Equal(Board with { Seen = Today() }, Get("dev", 0));
    }

    [Fact]
    public void RememberPerChannelWithoutLooksWritesNothing()
    {
        AuraChannelStates.Remember(Array.Empty<(string, int, ChannelLighting)>(), new[] { "dev" });

        Assert.Null(TestData.ReadRaw(AuraChannelStates.FileName));
    }

    // ---- SetPower ----

    [Fact]
    public void SetPowerKeepsTheEffectColourAndBrightnessOfARecordedChannel()
    {
        AuraChannelStates.Remember(Targets("dev", 0), Blue with { Brightness = 40 }, new[] { "dev" });

        AuraChannelStates.SetPower(Targets("dev", 0), on: false, Board, new[] { "dev" });

        Assert.Equal(Blue with { Brightness = 40, On = false, Seen = Today() }, Get("dev", 0));
    }

    [Fact]
    public void SwitchingAChannelOffAndOnReturnsItToItsOwnLook()
    {
        AuraChannelStates.Remember(Targets("dev", 0), Blue, new[] { "dev" });

        AuraChannelStates.SetPower(Targets("dev", 0), false, Board, new[] { "dev" });
        AuraChannelStates.SetPower(Targets("dev", 0), true, Board, new[] { "dev" });

        Assert.Equal(Blue with { Seen = Today() }, Get("dev", 0));
    }

    [Fact]
    public void SetPowerOfAChannelNeverSetRecordsTheBoardLookWithThePowerState()
    {
        AuraChannelStates.SetPower(Targets("dev", 3), on: false, Board, new[] { "dev" });

        Assert.Equal(Board with { On = false, Seen = Today() }, Get("dev", 3));
    }

    [Fact]
    public void SetPowerTouchesOnlyTheNamedChannels()
    {
        AuraChannelStates.Remember(Targets("dev", 0, 1), Blue, new[] { "dev" });

        AuraChannelStates.SetPower(Targets("dev", 1), false, Board, new[] { "dev" });

        Assert.True(Get("dev", 0)!.On);
        Assert.False(Get("dev", 1)!.On);
    }

    [Fact]
    public void SetPowerWithoutTargetsWritesNothing()
    {
        AuraChannelStates.SetPower(Array.Empty<(string, int)>(), true, Board, new[] { "dev" });

        Assert.Null(TestData.ReadRaw(AuraChannelStates.FileName));
    }

    // ---- SetBrightness ----

    [Fact]
    public void SetBrightnessKeepsTheEffectColourAndPowerOfARecordedChannel()
    {
        AuraChannelStates.Remember(Targets("dev", 0), Blue with { On = false }, new[] { "dev" });

        AuraChannelStates.SetBrightness(Targets("dev", 0), 65, Board, new[] { "dev" });

        Assert.Equal(Blue with { On = false, Brightness = 65, Seen = Today() }, Get("dev", 0));
    }

    [Fact]
    public void SetBrightnessOfAChannelNeverSetStartsFromTheBoardLook()
    {
        AuraChannelStates.SetBrightness(Targets("dev", 0), 65, Board, new[] { "dev" });

        Assert.Equal(Board with { Brightness = 65, Seen = Today() }, Get("dev", 0));
    }

    [Fact]
    public void ABrightnessOfZeroHandsAChannelBackToTheBoard()
    {
        AuraChannelStates.Remember(Targets("dev", 0), Blue with { Brightness = 40 }, new[] { "dev" });

        AuraChannelStates.SetBrightness(Targets("dev", 0), 0, Board, new[] { "dev" });

        Assert.Equal(0, Get("dev", 0)!.Brightness);
    }

    [Fact]
    public void SetBrightnessTouchesOnlyTheNamedChannels()
    {
        AuraChannelStates.Remember(Targets("dev", 0, 1), Blue, new[] { "dev" });

        AuraChannelStates.SetBrightness(Targets("dev", 0), 30, Board, new[] { "dev" });

        Assert.Equal(30, Get("dev", 0)!.Brightness);
        Assert.Equal(0, Get("dev", 1)!.Brightness);
    }

    [Fact]
    public void SetBrightnessSavedAboveTheRangeIsHeldToTheSliderRangeOnTheNextLoad()
    {
        AuraChannelStates.SetBrightness(Targets("dev", 0), 250, Board, new[] { "dev" });

        Assert.Equal(AuraState.MaxBrightness, Get("dev", 0)!.Brightness);
    }

    [Fact]
    public void SetBrightnessWithoutTargetsWritesNothing()
    {
        AuraChannelStates.SetBrightness(Array.Empty<(string, int)>(), 50, Board, new[] { "dev" });

        Assert.Null(TestData.ReadRaw(AuraChannelStates.FileName));
    }

    // ---- ageing ----

    [Fact]
    public void ARecordOfAnAbsentControllerOlderThanThirtyDaysIsDroppedOnTheNextSave()
    {
        OnOneDay(day =>
        {
            TestData.WriteRaw(AuraChannelStates.FileName,
                "{\"gone|0\":" + Record(day - 31) + ",\"gone|1\":" + Record(day - 365) + ",\"gone|2\":" + Record(1) + "}");

            AuraChannelStates.Remember(Targets("dev", 0), Blue, new[] { "dev" });

            Assert.Equal(new[] { "dev|0" }, AuraChannelStates.All().Keys.ToArray());
        });
    }

    [Fact]
    public void ARecordOfAnAbsentControllerExactlyThirtyDaysOldIsKept()
    {
        OnOneDay(day =>
        {
            TestData.WriteRaw(AuraChannelStates.FileName,
                "{\"gone|0\":" + Record(day - 30) + ",\"gone|1\":" + Record(day - 31) + "}");

            AuraChannelStates.Remember(Targets("dev", 0), Blue, new[] { "dev" });

            Assert.NotNull(Get("gone", 0));
            Assert.Null(Get("gone", 1));
        });
    }

    [Fact]
    public void ARecordOfAnAbsentControllerYoungerThanThirtyDaysIsKept()
    {
        OnOneDay(day =>
        {
            TestData.WriteRaw(AuraChannelStates.FileName,
                "{\"gone|0\":" + Record(day) + ",\"gone|1\":" + Record(day - 1) + ",\"gone|2\":" + Record(day - 29) + "}");

            AuraChannelStates.Remember(Targets("dev", 0), Blue, new[] { "dev" });

            Assert.Equal(4, AuraChannelStates.All().Count);
        });
    }

    [Fact]
    public void AnOldRecordOfAnAttachedControllerIsKeptHoweverOldItIs()
    {
        // A header never switched on its own must keep its look.
        OnOneDay(day =>
        {
            TestData.WriteRaw(AuraChannelStates.FileName,
                "{\"here|0\":" + Record(day - 400) + ",\"here|5\":" + Record(1) + "}");

            AuraChannelStates.Remember(Targets("dev", 0), Blue, new[] { "dev", "here" });

            Assert.NotNull(Get("here", 0));
            Assert.NotNull(Get("here", 5));
        });
    }

    [Fact]
    public void AnOldRecordIsDroppedOnlyWhenItsOwnControllerIsAbsent()
    {
        OnOneDay(day =>
        {
            TestData.WriteRaw(AuraChannelStates.FileName,
                "{\"here|0\":" + Record(day - 90) + ",\"gone|0\":" + Record(day - 90) + "}");

            AuraChannelStates.Remember(Targets("here", 1), Blue, new[] { "here" });

            Assert.NotNull(Get("here", 0));
            Assert.Null(Get("gone", 0));
        });
    }

    [Fact]
    public void AttachedControllersAreMatchedByTheDeviceHalfOfTheKeyNotByPrefix()
    {
        OnOneDay(day =>
        {
            TestData.WriteRaw(AuraChannelStates.FileName,
                "{\"dev2|0\":" + Record(day - 90) + ",\"dev|0\":" + Record(day - 90) + "}");

            AuraChannelStates.Remember(Targets("x", 0), Blue, new[] { "dev" });

            Assert.Null(Get("dev2", 0));
            Assert.NotNull(Get("dev", 0));
        });
    }

    [Fact]
    public void ADeviceKeyThatContainsTheSeparatorIsMatchedAtTheLastOne()
    {
        OnOneDay(day =>
        {
            TestData.WriteRaw(AuraChannelStates.FileName, "{\"a|b|0\":" + Record(day - 90) + "}");

            AuraChannelStates.Remember(Targets("x", 0), Blue, new[] { "a|b" });

            Assert.NotNull(Get("a|b", 0));
        });
    }

    [Fact]
    public void AttachedKeysAreMatchedExactlyNotByCase()
    {
        OnOneDay(day =>
        {
            TestData.WriteRaw(AuraChannelStates.FileName, "{\"Dev|0\":" + Record(day - 90) + "}");

            AuraChannelStates.Remember(Targets("x", 0), Blue, new[] { "dev" });

            Assert.Null(Get("Dev", 0));
        });
    }

    [Fact]
    public void ARecordFromBeforeTheFileCarriedADayStartsAgeingFromNow()
    {
        // Upgrading must not throw away what was remembered: a record without a stamp counts as today.
        OnOneDay(day =>
        {
            TestData.WriteRaw(AuraChannelStates.FileName, """{"gone|0":{"mode":1,"red":1}}""");

            AuraChannelStates.Remember(Targets("dev", 0), Blue, new[] { "dev" });

            ChannelLighting kept = Get("gone", 0)!;
            Assert.Equal(day, kept.Seen);
            Assert.Equal(1, kept.Red);
        });
    }

    [Fact]
    public void ARecordTheCallJustWroteIsNeverAgedOutEvenWithNoControllerAttached()
    {
        AuraChannelStates.Remember(Targets("dev", 0), Blue, None);

        Assert.NotNull(Get("dev", 0));
    }

    [Fact]
    public void NothingAgesOutWhenNothingIsSaved()
    {
        OnOneDay(day =>
        {
            string old = "{\"gone|0\":" + Record(1) + "}";
            TestData.WriteRaw(AuraChannelStates.FileName, old);

            AuraChannelStates.Remember(Array.Empty<(string, int)>(), Blue, None);

            Assert.Equal(old, TestData.ReadRaw(AuraChannelStates.FileName));
        });
    }

    [Fact]
    public void AgeingAppliesToEverySaveKind()
    {
        OnOneDay(day =>
        {
            foreach (int kind in new[] { 0, 1, 2, 3 })
            {
                TestData.Clear();
                TestData.WriteRaw(AuraChannelStates.FileName, "{\"gone|0\":" + Record(day - 90) + "}");
                var target = Targets("dev", 0);

                switch (kind)
                {
                    case 0:
                        AuraChannelStates.Remember(target, Blue, new[] { "dev" });
                        break;
                    case 1:
                        AuraChannelStates.Remember(new[] { ("dev", 0, Blue) }, new[] { "dev" });
                        break;
                    case 2:
                        AuraChannelStates.SetPower(target, false, Board, new[] { "dev" });
                        break;
                    default:
                        AuraChannelStates.SetBrightness(target, 50, Board, new[] { "dev" });
                        break;
                }

                Assert.Null(Get("gone", 0));
                Assert.NotNull(Get("dev", 0));
            }
        });
    }

    // ---- persistence behaviour ----

    [Fact]
    public void SaveThenLoadKeepsEveryRecordAcrossManyChannels()
    {
        var targets = Enumerable.Range(0, 16).Select(i => ("dev", i)).ToArray();

        AuraChannelStates.Remember(targets, new ChannelLighting(7, 11, 22, 33, false, 45), new[] { "dev" });

        Dictionary<string, ChannelLighting> states = AuraChannelStates.All();
        Assert.Equal(16, states.Count);
        Assert.All(states.Values, look => Assert.Equal(new ChannelLighting(7, 11, 22, 33, false, 45, Today()), look));
    }

    [Fact]
    public void ARememberedWholeControllerChannelIsStoredUnderMinusOne()
    {
        AuraChannelStates.Remember(Targets("dev", -1), Blue, new[] { "dev" });

        using JsonDocument document = Stored();
        Assert.True(document.RootElement.TryGetProperty("dev|-1", out _));
    }

    [Fact]
    public void ADeviceKeyWithPathCharactersRoundTrips()
    {
        string key = @"\\?\hid#vid_0b05&pid_19af#7&abc&0&0000#{4d1e55b2}";

        AuraChannelStates.Remember(Targets(key, 3), Blue, new[] { key });

        Assert.Equal(Blue with { Seen = Today() }, Get(key, 3));
    }

    [Fact]
    public void SaveLeavesNoTempFileAndReplacesTheOldContent()
    {
        for (int i = 0; i < 10; i++)
        {
            AuraChannelStates.Remember(Targets("dev", 0), Blue with { Red = (byte)i }, new[] { "dev" });
        }

        Assert.Empty(TempFiles());
        Assert.Equal(9, Get("dev", 0)!.Red);
    }

    [Fact]
    public void ADamagedFileKeepsACopyBeforeTheFirstSaveReplacesIt()
    {
        TestData.WriteRaw(AuraChannelStates.FileName, "{\"dev|0\": oops");

        AuraChannelStates.Remember(Targets("dev", 1), Blue, new[] { "dev" });

        Assert.Equal("{\"dev|0\": oops", File.ReadAllText(Assert.Single(BadCopies("channel-state"))));
        Assert.Single(AuraChannelStates.All());
    }

    [Fact]
    public void ASaveOnAFileThatCannotBeReplacedDoesNotThrow()
    {
        TestData.WriteRaw(AuraChannelStates.FileName, "{}");
        using var hold = new FileStream(AuraFiles.PathTo(AuraChannelStates.FileName), FileMode.Open,
            FileAccess.Read, FileShare.Read);

        AuraChannelStates.Remember(Targets("dev", 0), Blue, new[] { "dev" });

        Assert.Empty(TempFiles());
    }

    [Fact]
    public void AChangeWaitsForTheFilesLockInsteadOfRacingAnotherChange()
    {
        // A read-modify-write without the lock loses whichever of two concurrent changes saved first.
        var holder = new LockHolder(AuraFiles.Lock);
        var changer = new Thread(() => AuraChannelStates.Remember(Targets("dev", 0), Blue, new[] { "dev" }))
        {
            IsBackground = true,
        };
        changer.Start();

        Assert.True(Poll.Until(() => changer.ThreadState.HasFlag(ThreadState.WaitSleepJoin)));
        Assert.Null(Get("dev", 0));

        holder.Dispose();
        changer.Join();

        Assert.NotNull(Get("dev", 0));
    }

    [Fact]
    public void ConcurrentChangesEachKeepTheirOwnChannel()
    {
        const int threads = 6;
        Thread[] changers = Enumerable.Range(0, threads).Select(n => new Thread(() =>
        {
            for (int i = 0; i < 8; i++)
            {
                AuraChannelStates.Remember(Targets("dev", n), Blue with { Red = (byte)i }, new[] { "dev" });
            }
        })).ToArray();

        foreach (Thread changer in changers)
        {
            changer.Start();
        }

        foreach (Thread changer in changers)
        {
            changer.Join();
        }

        Dictionary<string, ChannelLighting> states = AuraChannelStates.All();
        Assert.Equal(threads, states.Count);
        Assert.All(states.Values, look => Assert.Equal(7, look.Red));
        Assert.Empty(TempFiles());
        Assert.Empty(BadCopies("channel-state"));
    }

    [Fact]
    public void AResetDeletesTheStatesAndTheNextReadIsEmpty()
    {
        AuraChannelStates.Remember(Targets("dev", 0), Blue, new[] { "dev" });

        Assert.Equal(AuraFiles.ResetResult.Done, AuraFiles.ResetAll());

        Assert.Empty(AuraChannelStates.All());
        Assert.False(File.Exists(AuraFiles.PathTo(AuraChannelStates.FileName)));
    }

    // An escaped lone surrogate in a key is valid JSON but cannot become a .NET string. Reading it
    // threw out of the loop and every channel after it was forgotten.
    [Fact]
    public void AKeyThatCannotBecomeTextDropsNothingElse()
    {
        Dictionary<string, ChannelLighting> states = Load(
            "{\"dev|0\":{\"mode\":1},\"dev|\\ud800\":{\"mode\":1},\"dev|2\":{\"mode\":1}}");

        Assert.Equal(new[] { "dev|0", "dev|2" }, states.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }
}
