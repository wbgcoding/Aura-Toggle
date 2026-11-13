using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static AuraToggle.Program;

namespace AuraToggle.Tests;

/// <summary>
/// The pure half of switching: which modes and colours go to a controller, which records follow,
/// and how the firmware effect engine is shared. No controller, no window.
/// </summary>
public sealed class ProgramEngineTests : DataFolderTest
{
    private const byte Off = 0;
    private const byte Static = 1;
    private const byte Rainbow = 5;
    private const byte Chase = 9;
    private const byte Wave = 11;

    private static readonly IReadOnlyList<int> Three = new[] { 0, 1, 2 };

    private static AuraState Board(byte mode = Static, byte brightness = 100, bool on = true,
        byte red = 255, byte green = 255, byte blue = 255) =>
        new(on, mode, red, green, blue, "", brightness);

    private static bool Firmware(byte mode) => AuraPresets.All.Any(p => p.Mode == mode && !p.UsesColour);

    /// <summary>The modes that go out, in channel order, for example "5,5,1".</summary>
    private static string Sent(DeviceMix mix) =>
        string.Join(",", mix.Looks.OrderBy(l => l.Channel).Select(l => l.Mode));

    /// <summary>The records that follow, as "channel:mode" in channel order, "off" appended for a record that stays off.</summary>
    private static string Recorded(DeviceMix mix) =>
        string.Join(" ", mix.Records.OrderBy(r => r.Channel).Select(r => $"{r.Channel}:{r.Look.Mode}{(r.Look.On ? "" : "off")}"));

    private static Func<int, ChannelLighting?> Records(Dictionary<int, ChannelLighting> table) =>
        index => table.TryGetValue(index, out ChannelLighting? look) ? look : null;

    private static ChannelLighting Coloured(byte mode, bool on = true, byte brightness = 0) =>
        new(mode, 0, 0, 255, on, brightness);

    private static ChannelLook LookOf(DeviceMix mix, int channel) => mix.Looks.Single(l => l.Channel == channel);

    // ---- BoardWide and Brightness ----------------------------------------------------------

    public static TheoryData<byte> EveryMode()
    {
        var data = new TheoryData<byte> { Off };
        foreach (AuraPreset preset in AuraPresets.All)
        {
            data.Add(preset.Mode);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryMode))]
    public void BoardWideTakesTheModeAndColourOfTheBoardAndIsAlwaysOnWithoutOwnBrightness(byte mode)
    {
        AuraState state = Board(mode, brightness: 40, on: false, red: 1, green: 2, blue: 3);

        ChannelLighting look = Program.BoardWide(state);

        Assert.Equal(new ChannelLighting(mode, 1, 2, 3, On: true, Brightness: 0), look);
    }

    [Theory]
    [InlineData(null, 70, 70)]
    [InlineData(0, 70, 70)]
    [InlineData(1, 70, 1)]
    [InlineData(10, 100, 10)]
    [InlineData(55, 20, 55)]
    [InlineData(100, 10, 100)]
    public void BrightnessIsTheChannelsOwnOrElseTheBoards(int? own, int board, int expected)
    {
        ChannelLighting? look = own.HasValue ? new ChannelLighting(Static, 0, 0, 0, Brightness: (byte)own.Value) : null;

        Assert.Equal((byte)expected, Program.Brightness(look, Board(brightness: (byte)board)));
    }

    // ---- ResolveEntry ----------------------------------------------------------------------

    private static CustomPresetEntry Entry(string device, int channel, byte mode = Static, byte brightness = 0) =>
        new(device, channel, "label", mode, 1, 2, 3, brightness);

    [Fact]
    public void NoEntriesResolveToNothing()
    {
        Assert.Null(Program.ResolveEntry(new List<CustomPresetEntry>(), "dev-a", 0));
        Assert.Null(Program.ResolveEntry(new List<CustomPresetEntry>(), "dev-a", -1));
    }

    [Fact]
    public void AnEntryForTheChannelBeatsOneForTheWholeController()
    {
        CustomPresetEntry whole = Entry("dev-a", -1, Wave);
        CustomPresetEntry own = Entry("dev-a", 2, Rainbow);

        // Whichever comes first in the list, the channel's own entry decides.
        Assert.Same(own, Program.ResolveEntry(new List<CustomPresetEntry> { whole, own }, "dev-a", 2));
        Assert.Same(own, Program.ResolveEntry(new List<CustomPresetEntry> { own, whole }, "dev-a", 2));
    }

    [Fact]
    public void AChannelWithoutAnEntryOfItsOwnFallsBackToTheWholeController()
    {
        CustomPresetEntry whole = Entry("dev-a", -1, Wave);
        CustomPresetEntry other = Entry("dev-a", 2, Rainbow);
        var entries = new List<CustomPresetEntry> { other, whole };

        Assert.Same(whole, Program.ResolveEntry(entries, "dev-a", 0));
        Assert.Same(whole, Program.ResolveEntry(entries, "dev-a", 1));
        Assert.Same(whole, Program.ResolveEntry(entries, "dev-a", 7));
    }

    [Fact]
    public void ThePositionOfAnEntryDecidesBetweenTwoOfTheSameKind()
    {
        CustomPresetEntry first = Entry("dev-a", 1, Static);
        CustomPresetEntry second = Entry("dev-a", 1, Wave);
        CustomPresetEntry wholeFirst = Entry("dev-a", -1, Static);
        CustomPresetEntry wholeSecond = Entry("dev-a", -1, Rainbow);

        Assert.Same(second, Program.ResolveEntry(new List<CustomPresetEntry> { first, second }, "dev-a", 1));
        Assert.Same(first, Program.ResolveEntry(new List<CustomPresetEntry> { second, first }, "dev-a", 1));
        Assert.Same(wholeSecond, Program.ResolveEntry(new List<CustomPresetEntry> { wholeFirst, wholeSecond }, "dev-a", 0));
    }

    [Fact]
    public void EntriesOfAnotherControllerNeverMatch()
    {
        var entries = new List<CustomPresetEntry> { Entry("dev-b", 1), Entry("dev-b", -1) };

        Assert.Null(Program.ResolveEntry(entries, "dev-a", 1));
        Assert.Null(Program.ResolveEntry(entries, "DEV-B", 1));
        Assert.Null(Program.ResolveEntry(entries, "", 1));
    }

    [Fact]
    public void AnEntryForAnotherChannelOfThisControllerIsNotAWholeControllerEntry()
    {
        Assert.Null(Program.ResolveEntry(new List<CustomPresetEntry> { Entry("dev-a", 2) }, "dev-a", 1));
        Assert.Null(Program.ResolveEntry(new List<CustomPresetEntry> { Entry("dev-a", 0) }, "dev-a", 1));
    }

    [Fact]
    public void AskingForTheWholeControllerFindsOnlyAWholeControllerEntry()
    {
        CustomPresetEntry whole = Entry("dev-a", -1, Wave);
        var entries = new List<CustomPresetEntry> { Entry("dev-a", 0), Entry("dev-a", 1), whole };

        Assert.Same(whole, Program.ResolveEntry(entries, "dev-a", -1));
        Assert.Null(Program.ResolveEntry(new List<CustomPresetEntry> { Entry("dev-a", 0) }, "dev-a", -1));
    }

    [Fact]
    public void ChannelZeroIsAChannelAndNotTheWholeController()
    {
        CustomPresetEntry zero = Entry("dev-a", 0, Rainbow);
        CustomPresetEntry whole = Entry("dev-a", -1, Wave);

        Assert.Same(zero, Program.ResolveEntry(new List<CustomPresetEntry> { whole, zero }, "dev-a", 0));
        Assert.Same(whole, Program.ResolveEntry(new List<CustomPresetEntry> { whole, zero }, "dev-a", 1));
    }

    [Fact]
    public void TheEntryReturnedIsTheOneInTheListWithItsBrightness()
    {
        CustomPresetEntry own = Entry("dev-a", 1, Static, brightness: 35);

        CustomPresetEntry? found = Program.ResolveEntry(new List<CustomPresetEntry> { own }, "dev-a", 1);

        Assert.NotNull(found);
        Assert.Equal(35, found.Brightness);
    }

    // ---- SplitControllers ------------------------------------------------------------------

    private static CustomPreset PresetOn(params string[] devices) =>
        new("p", devices.Select((key, i) => Entry(key, i)).ToList());

    [Fact]
    public void ControllersAreSplitIntoNamedAndLeftInTheirOwnOrder()
    {
        (List<string> named, List<string> left) =
            Program.SplitControllers(PresetOn("c", "a"), new[] { "a", "b", "c", "d" });

        Assert.Equal(new[] { "a", "c" }, named);
        Assert.Equal(new[] { "b", "d" }, left);
    }

    [Fact]
    public void NoAttachedControllersGiveTwoEmptyLists()
    {
        (List<string> named, List<string> left) = Program.SplitControllers(PresetOn("a"), Array.Empty<string>());

        Assert.Empty(named);
        Assert.Empty(left);
    }

    [Fact]
    public void APresetWithoutEntriesLeavesEveryControllerOut()
    {
        (List<string> named, List<string> left) =
            Program.SplitControllers(new CustomPreset("empty", new List<CustomPresetEntry>()), new[] { "a", "b" });

        Assert.Empty(named);
        Assert.Equal(new[] { "a", "b" }, left);
    }

    [Fact]
    public void ADetachedControllerThePresetNamesIsInNeitherList()
    {
        // A key is a device path: when the header lands on another port, nothing is named.
        (List<string> named, List<string> left) =
            Program.SplitControllers(PresetOn("old-port"), new[] { "new-port" });

        Assert.Empty(named);
        Assert.Equal(new[] { "new-port" }, left);
    }

    [Fact]
    public void ControllerKeysAreComparedExactly()
    {
        (List<string> named, List<string> left) = Program.SplitControllers(PresetOn("Dev-A"), new[] { "dev-a" });

        Assert.Empty(named);
        Assert.Equal(new[] { "dev-a" }, left);
    }

    [Fact]
    public void AControllerNamedByManyEntriesIsListedOnceAndAWholeControllerEntryCounts()
    {
        var preset = new CustomPreset("p", new List<CustomPresetEntry>
        {
            Entry("a", 0), Entry("a", 1), Entry("a", 2), Entry("b", -1),
        });

        (List<string> named, List<string> left) = Program.SplitControllers(preset, new[] { "a", "b", "c" });

        Assert.Equal(new[] { "a", "b" }, named);
        Assert.Equal(new[] { "c" }, left);
    }

    // ---- ControllerIsOn --------------------------------------------------------------------

    private static List<AuraDeviceSummary> Listed(params string[] keys) =>
        keys.Select(key => Layouts.Mainboard(key)).ToList();

    [Fact]
    public void AnUnlistedControllerFollowsTheBoardFlag()
    {
        Stored.State(Board(on: true));
        Assert.True(Program.ControllerIsOn("gone", Listed("dev-a")));

        Stored.State(Board(on: false));
        Assert.False(Program.ControllerIsOn("gone", Listed("dev-a")));
        Assert.False(Program.ControllerIsOn("gone", new List<AuraDeviceSummary>()));
    }

    [Fact]
    public void WithoutRecordsAControllerIsOnWhileTheBoardIs()
    {
        Stored.State(Board(on: true));
        Assert.True(Program.ControllerIsOn("dev-a", Listed("dev-a")));

        Stored.State(Board(on: false));
        Assert.False(Program.ControllerIsOn("dev-a", Listed("dev-a")));
    }

    [Fact]
    public void ADarkControllerIsOffEvenWhileAnotherOneKeepsTheBoardFlagOn()
    {
        // Every channel of dev-a is off by its own record; the board flag says on because dev-b is lit.
        Stored.State(Board(on: true));
        foreach (int index in new[] { 0, 1, 2, 3 })
        {
            Stored.Look("dev-a", index, Coloured(Static, on: false));
        }

        Assert.False(Program.ControllerIsOn("dev-a", Listed("dev-a", "dev-b")));
        Assert.True(Program.ControllerIsOn("dev-b", Listed("dev-a", "dev-b")));
    }

    [Fact]
    public void OneChannelOnIsEnoughForTheControllerToCountAsOn()
    {
        Stored.State(Board(on: false));
        Stored.Look("dev-a", 2, Coloured(Static, on: true));

        Assert.True(Program.ControllerIsOn("dev-a", Listed("dev-a")));
    }

    [Fact]
    public void AChannelWithoutARecordFollowsTheBoardNextToChannelsThatAreOff()
    {
        Stored.State(Board(on: true));
        Stored.Look("dev-a", 0, Coloured(Static, on: false));

        // Channels 1..3 have no record, so they count as on while the board is.
        Assert.True(Program.ControllerIsOn("dev-a", Listed("dev-a")));
    }

    [Fact]
    public void AControllerWithoutChannelsIsOff()
    {
        Stored.State(Board(on: true));
        var listed = new List<AuraDeviceSummary> { new("dev-a", "Empty", new List<AuraChannel>()) };

        Assert.False(Program.ControllerIsOn("dev-a", listed));
    }

    // ---- ShareEngine -----------------------------------------------------------------------

    private static EngineChannel Ch(int index, bool chosen = true, ChannelLighting? record = null) =>
        new(index, chosen, record);

    private static ChannelLook Lk(int channel, byte mode, byte red = 0, byte green = 0, byte blue = 255) =>
        new(channel, mode, red, green, blue);

    private static readonly ChannelLighting BoardLook = new(Static, 10, 20, 30);

    [Fact]
    public void NothingToShareReturnsNothing()
    {
        EngineShare share = Program.ShareEngine(Array.Empty<EngineChannel>(), Array.Empty<ChannelLook>(),
            spreadToAll: true, BoardLook, 100);

        Assert.Empty(share.Looks);
        Assert.Empty(share.Records);
    }

    [Fact]
    public void NoChosenChannelMeansNoSharing()
    {
        ChannelLook[] looks = { Lk(0, Wave), Lk(1, Rainbow), Lk(2, Static) };
        EngineChannel[] channels = { Ch(0, false), Ch(1, false), Ch(2, false) };

        EngineShare share = Program.ShareEngine(channels, looks, spreadToAll: true, BoardLook, 100);

        Assert.Equal(looks, share.Looks);
        Assert.Empty(share.Records);
    }

    [Fact]
    public void AChosenColourModeNeverSharesAnything()
    {
        ChannelLook[] looks = { Lk(0, Rainbow), Lk(1, Chase), Lk(2, Wave) };
        EngineChannel[] channels = { Ch(0, false), Ch(1), Ch(2, false) };

        EngineShare share = Program.ShareEngine(channels, looks, spreadToAll: true, BoardLook, 100);

        Assert.Equal(looks, share.Looks);
        Assert.Empty(share.Records);
    }

    [Fact]
    public void TheLookListHandedInIsNotChanged()
    {
        ChannelLook[] looks = { Lk(0, Wave), Lk(1, Static), Lk(2, Rainbow) };
        ChannelLook[] copy = looks.ToArray();

        _ = Program.ShareEngine(new[] { Ch(0), Ch(1), Ch(2) }, looks, spreadToAll: false, BoardLook, 100);

        Assert.Equal(copy, looks);
    }

    [Fact]
    public void TheLowestChosenFirmwareChannelDecidesWhateverTheOrderOfTheLooks()
    {
        EngineChannel[] channels = { Ch(0), Ch(1), Ch(2) };
        ChannelLook[] forward = { Lk(0, Wave), Lk(1, Static), Lk(2, Rainbow) };
        ChannelLook[] backward = { forward[2], forward[1], forward[0] };

        foreach (ChannelLook[] looks in new[] { forward, backward })
        {
            EngineShare share = Program.ShareEngine(channels, looks, spreadToAll: false, BoardLook, 100);

            Assert.Equal("11,1,11", string.Join(",", share.Looks.OrderBy(l => l.Channel).Select(l => l.Mode)));
            Assert.Equal(new[] { 2 }, share.Records.Select(r => r.Channel));
        }
    }

    [Fact]
    public void AFirmwareChannelThatWasNotChosenStillFollowsTheEngine()
    {
        // A second firmware mode in the same burst would be sent last and win, so it is aligned.
        ChannelLook[] looks = { Lk(0, Wave), Lk(1, Rainbow) };

        EngineShare share = Program.ShareEngine(new[] { Ch(0), Ch(1, chosen: false) }, looks, spreadToAll: false,
            BoardLook, 100);

        Assert.Equal(new byte[] { Wave, Wave }, share.Looks.Select(l => l.Mode));
    }

    [Fact]
    public void WithoutSpreadAColourChannelKeepsItsModeAndAnOffChannelStaysOff()
    {
        ChannelLook[] looks = { Lk(0, Wave), Lk(1, Static), Lk(2, Off), Lk(3, Rainbow) };
        EngineChannel[] channels = { Ch(0), Ch(1), Ch(2), Ch(3) };

        EngineShare share = Program.ShareEngine(channels, looks, spreadToAll: false, BoardLook, 100);

        Assert.Equal(new byte[] { Wave, Static, Off, Wave }, share.Looks.Select(l => l.Mode));
        Assert.Equal(new[] { 3 }, share.Records.Select(r => r.Channel));
    }

    [Fact]
    public void WithSpreadEveryChannelThatIsNotOffTakesTheModeAndADarkOneOnlyItsRecord()
    {
        ChannelLook[] looks = { Lk(0, Wave), Lk(1, Static), Lk(2, Off), Lk(3, Chase) };
        var offRecord = new ChannelLighting(Static, 1, 2, 3, On: false);
        EngineChannel[] channels = { Ch(0), Ch(1), Ch(2, record: offRecord), Ch(3) };

        EngineShare share = Program.ShareEngine(channels, looks, spreadToAll: true, BoardLook, 100);

        Assert.Equal(new byte[] { Wave, Wave, Off, Wave }, share.Looks.Select(l => l.Mode));
        Assert.Equal(new[] { 1, 2, 3 }, share.Records.Select(r => r.Channel));

        // The dark channel keeps its own record and its own power, only the mode follows.
        ChannelLighting dark = share.Records.Single(r => r.Channel == 2).Look;
        Assert.Equal(offRecord with { Mode = Wave }, dark);
        Assert.False(dark.On);
    }

    [Fact]
    public void AChannelNeverSetOnItsOwnGetsTheBoardColourWhenAModeIsPicked()
    {
        ChannelLook[] looks = { Lk(1, Rainbow) };
        EngineChannel[] channels = { Ch(0), Ch(1), Ch(2) };
        var board = new ChannelLighting(Static, 200, 100, 50);

        EngineShare share = Program.ShareEngine(channels, looks, spreadToAll: true, board, boardBrightness: 50);

        Assert.Equal(new[] { 1, 0, 2 }, share.Looks.Select(l => l.Channel));
        Assert.All(share.Looks, l => Assert.Equal(Rainbow, l.Mode));
        ChannelLook added = share.Looks.Single(l => l.Channel == 0);
        Assert.Equal((100, 50, 25), (added.Red, added.Green, added.Blue));
        Assert.Equal(new[] { 0, 2 }, share.Records.Select(r => r.Channel));
        Assert.All(share.Records, r => Assert.Equal(board with { Mode = Rainbow }, r.Look));
    }

    [Fact]
    public void AChannelNeverSetOnItsOwnIsLeftAloneWithoutSpread()
    {
        ChannelLook[] looks = { Lk(1, Rainbow) };

        EngineShare share = Program.ShareEngine(new[] { Ch(0), Ch(1), Ch(2) }, looks, spreadToAll: false, BoardLook, 100);

        Assert.Equal(looks, share.Looks);
        Assert.Empty(share.Records);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(5, 25)]
    [InlineData(10, 25)]
    [InlineData(100, 255)]
    [InlineData(200, 255)]
    public void TheBoardBrightnessIsClampedWhenAColourIsAddedForAChannelWithoutARecord(int brightness, int expected)
    {
        var board = new ChannelLighting(Static, 255, 255, 255);

        EngineShare share = Program.ShareEngine(new[] { Ch(0), Ch(1) }, new[] { Lk(1, Wave) }, spreadToAll: true, board,
            (byte)brightness);

        ChannelLook added = share.Looks.Single(l => l.Channel == 0);
        Assert.Equal(((byte)expected, (byte)expected, (byte)expected), (added.Red, added.Green, added.Blue));
    }

    [Fact]
    public void AChannelThatAlreadyHasALookInTheBurstIsNotAddedTwice()
    {
        ChannelLook[] looks = { Lk(0, Static), Lk(1, Wave) };

        EngineShare share = Program.ShareEngine(new[] { Ch(0), Ch(1) }, looks, spreadToAll: true, BoardLook, 100);

        Assert.Equal(2, share.Looks.Count);
    }

    [Fact]
    public void TheRecordOfAChannelThatChangedKeepsItsColourAndBrightness()
    {
        var own = new ChannelLighting(Static, 9, 8, 7, On: true, Brightness: 33);

        EngineShare share = Program.ShareEngine(new[] { Ch(0), Ch(1, record: own) },
            new[] { Lk(0, Wave), Lk(1, Static) }, spreadToAll: true, BoardLook, 100);

        Assert.Equal((1, own with { Mode = Wave }), Assert.Single(share.Records));
    }

    // ---- MixDevice: the firmware engine across every mode ----------------------------------

    /// <summary>Every mode picked on channel 1, every mode (and "never set") on the others.</summary>
    public static TheoryData<byte, byte> PickAgainstOthers()
    {
        var data = new TheoryData<byte, byte>();
        foreach (AuraPreset picked in AuraPresets.All)
        {
            data.Add(picked.Mode, Off);
            foreach (AuraPreset other in AuraPresets.All)
            {
                data.Add(picked.Mode, other.Mode);
            }
        }

        data.Add(Off, Static);
        data.Add(Off, Rainbow);
        return data;
    }

    [Theory]
    [MemberData(nameof(PickAgainstOthers))]
    public void APickedModeOnOneChannelAgainstChannelsOnAnyOtherMode(byte picked, byte other)
    {
        // "other" 0 means the other channels carry no record at all.
        var table = new Dictionary<int, ChannelLighting> { [1] = Coloured(Static) };
        if (other != Off)
        {
            table[0] = Coloured(other);
            table[2] = Coloured(other);
        }

        DeviceMix mix = Program.MixDevice(Three, Records(table), 1, Board(), new ChannelLighting(picked, 0, 0, 255), null,
            keepPower: false);

        Assert.Equal(new[] { 1 }, mix.Named);

        if (Firmware(picked))
        {
            // One engine per controller: the pick takes every channel, a record-less one included.
            Assert.Equal($"{picked},{picked},{picked}", Sent(mix));
            Assert.Equal(other == picked ? "" : $"0:{picked} 2:{picked}", Recorded(mix));
            return;
        }

        string expected = other == Off ? picked.ToString() : $"{other},{picked},{other}";
        Assert.Equal(expected, Sent(mix));
        Assert.Equal("", Recorded(mix));
        Assert.Equal(other == Off ? 1 : 3, mix.Looks.Count);
    }

    [Theory]
    [MemberData(nameof(EveryMode))]
    public void ReplayingRecordsOfAnyMixKeepsEachChannelsOwnModeUnlessFirmwareModesDisagree(byte mode)
    {
        // Channel 0 on "mode", channel 1 on static, channel 2 on rainbow, nothing picked (look null).
        var table = new Dictionary<int, ChannelLighting>
        {
            [0] = Coloured(mode), [1] = Coloured(Static), [2] = Coloured(Rainbow),
        };

        DeviceMix mix = Program.MixDevice(Three, Records(table), -1, Board(), null, null, keepPower: false);

        // The lowest firmware channel decides; a non-firmware mode on channel 0 leaves channel 2 alone.
        string expected = Firmware(mode) ? $"{mode},{Static},{mode}" : $"{mode},{Static},{Rainbow}";
        Assert.Equal(expected, Sent(mix));
        Assert.Equal(new[] { 0, 1, 2 }, mix.Named);
        Assert.Equal(Firmware(mode) && mode != Rainbow ? $"2:{mode}" : "", Recorded(mix));
    }

    // ---- MixDevice: the cases the review build does not reach ------------------------------

    [Fact]
    public void NoChannelsSendNothingAndNameNothing()
    {
        DeviceMix mix = Program.MixDevice(Array.Empty<int>(), _ => null, -1, Board(), new ChannelLighting(Rainbow, 1, 2, 3),
            null, keepPower: false);

        Assert.Empty(mix.Looks);
        Assert.Empty(mix.Named);
        Assert.Empty(mix.Records);
    }

    [Fact]
    public void OneChannelWithoutARecordTakesTheBoardLook()
    {
        DeviceMix mix = Program.MixDevice(new[] { 0 }, _ => null, -1, Board(Static, 100, red: 10, green: 20, blue: 30),
            null, null, keepPower: false);

        Assert.Equal(new[] { new ChannelLook(0, Static, 10, 20, 30) }, mix.Looks);
        Assert.Equal(new[] { 0 }, mix.Named);
        Assert.Empty(mix.Records);
    }

    [Fact]
    public void OneChannelPickedFirmwareModeNeedsNoRecordForItself()
    {
        DeviceMix mix = Program.MixDevice(new[] { 0 }, _ => null, 0, Board(), new ChannelLighting(Wave, 1, 2, 3), null,
            keepPower: false);

        Assert.Equal("11", Sent(mix));
        Assert.Empty(mix.Records);
    }

    [Fact]
    public void ANamedChannelThatIsOffComesOnUnlessOnlyTheBrightnessChanges()
    {
        var table = new Dictionary<int, ChannelLighting>
        {
            [0] = Coloured(Static, on: false), [1] = Coloured(Chase, on: false), [2] = Coloured(Static, on: false),
        };

        DeviceMix named = Program.MixDevice(Three, Records(table), -1, Board(), null, null, keepPower: false);
        DeviceMix dimmer = Program.MixDevice(Three, Records(table), -1, Board(), null, 50, keepPower: true);

        Assert.Equal("1,9,1", Sent(named));
        Assert.Equal("0,0,0", Sent(dimmer));
        Assert.Equal(new[] { 0, 1, 2 }, dimmer.Named);
    }

    [Fact]
    public void AnUntouchedChannelThatIsOffStaysOffWhenTheOthersAreNamed()
    {
        var table = new Dictionary<int, ChannelLighting>
        {
            [0] = Coloured(Static, on: false), [1] = Coloured(Static), [2] = Coloured(Static),
        };

        DeviceMix mix = Program.MixDevice(Three, Records(table), 1, Board(), new ChannelLighting(Chase, 0, 0, 255), null,
            keepPower: false);

        Assert.Equal("0,9,1", Sent(mix));
    }

    [Fact]
    public void ANamedChannelOutsideTheListNamesNothingAndOnlyReplaysTheRecords()
    {
        var table = new Dictionary<int, ChannelLighting> { [0] = Coloured(Static), [2] = Coloured(Chase) };

        DeviceMix mix = Program.MixDevice(Three, Records(table), 7, Board(), new ChannelLighting(Rainbow, 1, 2, 3), null,
            keepPower: false);

        Assert.Empty(mix.Named);
        Assert.Equal("1,9", Sent(mix));
        Assert.Empty(mix.Records);
    }

    [Fact]
    public void ALookForANamedChannelDoesNotTouchTheColourOfTheOthers()
    {
        var table = new Dictionary<int, ChannelLighting>
        {
            [0] = new(Static, 1, 2, 3), [1] = new(Static, 4, 5, 6), [2] = new(Static, 7, 8, 9),
        };

        DeviceMix mix = Program.MixDevice(Three, Records(table), 1, Board(), new ChannelLighting(Chase, 100, 100, 100), null,
            keepPower: false);

        Assert.Equal(new ChannelLook(0, Static, 1, 2, 3), LookOf(mix, 0));
        Assert.Equal(new ChannelLook(1, 9, 100, 100, 100), LookOf(mix, 1));
        Assert.Equal(new ChannelLook(2, Static, 7, 8, 9), LookOf(mix, 2));
    }

    [Fact]
    public void TheOrderOfTheChannelListDoesNotChangeWhatIsSent()
    {
        var table = new Dictionary<int, ChannelLighting>
        {
            [0] = Coloured(Static), [1] = Coloured(Rainbow), [2] = Coloured(Wave),
        };

        DeviceMix forward = Program.MixDevice(new[] { 0, 1, 2 }, Records(table), -1, Board(), null, null, false);
        DeviceMix backward = Program.MixDevice(new[] { 2, 1, 0 }, Records(table), -1, Board(), null, null, false);

        Assert.Equal(Sent(forward), Sent(backward));
        Assert.Equal(Recorded(forward), Recorded(backward));
        Assert.Equal("1,5,5", Sent(forward));
    }

    [Fact]
    public void AllChannelsOffAndNamedByAWholeBoardSwitchComeOnWithTheirOwnModes()
    {
        var table = new Dictionary<int, ChannelLighting>
        {
            [0] = Coloured(Static, on: false), [1] = Coloured(Rainbow, on: false), [2] = Coloured(Wave, on: false),
        };

        DeviceMix mix = Program.MixDevice(Three, Records(table), -1, Board(), null, null, keepPower: false);

        // Rainbow and wave are both firmware modes: the lowest one decides, so wave joins rainbow.
        Assert.Equal("1,5,5", Sent(mix));
        Assert.Equal("2:5off", Recorded(mix));
    }

    // ---- MixDevice: brightness -------------------------------------------------------------

    [Theory]
    [InlineData(100, 255)]
    [InlineData(80, 204)]
    [InlineData(50, 127)]
    [InlineData(10, 25)]
    public void TheBoardBrightnessScalesAChannelWithoutAnOwn(int board, int expected)
    {
        DeviceMix mix = Program.MixDevice(new[] { 0 }, _ => null, -1, Board(brightness: (byte)board), null, null, false);

        Assert.Equal(((byte)expected, (byte)expected, (byte)expected), (LookOf(mix, 0).Red, LookOf(mix, 0).Green, LookOf(mix, 0).Blue));
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 25)]
    [InlineData(9, 25)]
    [InlineData(10, 25)]
    [InlineData(40, 102)]
    [InlineData(99, 252)]
    [InlineData(100, 255)]
    public void AnExplicitBrightnessScalesTheNamedChannelAndIsClampedToTheFloor(int percent, int expected)
    {
        var table = new Dictionary<int, ChannelLighting> { [0] = new(Static, 255, 255, 255, Brightness: 77) };

        DeviceMix mix = Program.MixDevice(new[] { 0 }, Records(table), 0, Board(brightness: 90), null, (byte)percent,
            keepPower: true);

        Assert.Equal((byte)expected, LookOf(mix, 0).Red);
    }

    [Fact]
    public void AnExplicitBrightnessLeavesTheChannelsThatWereNotNamedAlone()
    {
        var table = new Dictionary<int, ChannelLighting>
        {
            [0] = new(Static, 255, 255, 255), [1] = new(Static, 255, 255, 255, Brightness: 60),
        };

        DeviceMix mix = Program.MixDevice(new[] { 0, 1 }, Records(table), 0, Board(brightness: 50), null, 20, keepPower: true);

        Assert.Equal((byte)51, LookOf(mix, 0).Red);
        Assert.Equal((byte)153, LookOf(mix, 1).Red);
    }

    [Fact]
    public void ARecordsOwnBrightnessBeatsTheBoardsAndZeroMeansFollowTheBoard()
    {
        var table = new Dictionary<int, ChannelLighting>
        {
            [0] = new(Static, 255, 255, 255, Brightness: 80), [1] = new(Static, 255, 255, 255, Brightness: 0),
        };

        DeviceMix mix = Program.MixDevice(new[] { 0, 1 }, Records(table), -1, Board(brightness: 50), null, null, false);

        Assert.Equal((byte)204, LookOf(mix, 0).Red);
        Assert.Equal((byte)127, LookOf(mix, 1).Red);
    }

    [Fact]
    public void ALookWithoutBrightnessKeepsTheBrightnessTheChannelHad()
    {
        var table = new Dictionary<int, ChannelLighting> { [0] = new(Static, 0, 0, 0, Brightness: 60) };

        DeviceMix mix = Program.MixDevice(new[] { 0 }, Records(table), 0, Board(brightness: 100), new ChannelLighting(Static, 255, 255, 255),
            null, false);

        Assert.Equal((byte)153, LookOf(mix, 0).Red);
    }

    [Fact]
    public void ALookWithItsOwnBrightnessBeatsTheOneTheChannelHad()
    {
        var table = new Dictionary<int, ChannelLighting> { [0] = new(Static, 0, 0, 0, Brightness: 60) };

        DeviceMix mix = Program.MixDevice(new[] { 0 }, Records(table), 0, Board(brightness: 100),
            new ChannelLighting(Static, 255, 255, 255, Brightness: 30), null, false);

        Assert.Equal((byte)76, LookOf(mix, 0).Red);
    }

    [Fact]
    public void ALookWithoutBrightnessOnAChannelWithoutARecordUsesTheBoardBrightness()
    {
        DeviceMix mix = Program.MixDevice(new[] { 0 }, _ => null, 0, Board(brightness: 50),
            new ChannelLighting(Static, 255, 255, 255), null, false);

        Assert.Equal((byte)127, LookOf(mix, 0).Red);
    }

    [Fact]
    public void BrightnessIsAppliedToEachColourComponentSeparately()
    {
        DeviceMix mix = Program.MixDevice(new[] { 0 }, _ => null, 0, Board(brightness: 100),
            new ChannelLighting(Static, 200, 100, 3), 50, false);

        ChannelLook look = LookOf(mix, 0);
        Assert.Equal((100, 50, 1), (look.Red, look.Green, look.Blue));
    }

    [Theory]
    [MemberData(nameof(EveryMode))]
    public void ABrightnessChangeNeverTurnsAColourChannelIntoAnotherMode(byte mode)
    {
        // Channels 1 and 2 are plain colour channels; the change must leave their modes alone
        // whatever the first channel runs.
        var table = new Dictionary<int, ChannelLighting>
        {
            [0] = Coloured(mode), [1] = Coloured(Static), [2] = Coloured(Chase),
        };

        DeviceMix mix = Program.MixDevice(Three, Records(table), -1, Board(), null, 50, keepPower: true);

        Assert.Equal(mode, LookOf(mix, 0).Mode);
        Assert.Equal(Static, LookOf(mix, 1).Mode);
        Assert.Equal(Chase, LookOf(mix, 2).Mode);
    }

    [Fact]
    public void ABrightnessChangeKeepsTheRecordOfAChannelThatIsOffDark()
    {
        var table = new Dictionary<int, ChannelLighting>
        {
            [0] = Coloured(Static, on: false), [1] = Coloured(Static),
        };

        DeviceMix mix = Program.MixDevice(new[] { 0, 1 }, Records(table), -1, Board(), null, 40, keepPower: true);

        Assert.Equal("0,1", Sent(mix));
    }
}
