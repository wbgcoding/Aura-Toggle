using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;
using static AuraToggle.Program;

namespace AuraToggle.Tests;

/// <summary>
/// Switching, effects and brightness end to end below the command line: the real files, the real
/// engine and the real protocol, with controllers that are files (<see cref="FakeController"/>).
/// </summary>
public sealed class ProgramSwitchTests : DataFolderTest
{
    private const byte Off = 0;
    private const byte Static = 1;
    private const byte Breathing = 2;
    private const byte Rainbow = 5;

    // Colour (200,100,50) at 100% is itself; the board runs static on it.
    private static AuraState Board(bool on = true, byte mode = Static, byte brightness = 100, string preset = "") =>
        new(on, mode, 200, 100, 50, preset, brightness);

    private static FakeController A() => FakeController.Addressable("a", headers: 2);

    private static FakeController B() => FakeController.Addressable("b", headers: 1);

    private static List<AuraDevice> Open(params FakeController[] controllers) =>
        controllers.Select(controller => controller.Device).ToList();

    /// <summary>
    /// What one burst sent, per channel in the order sent. Every burst is the same list twice, which
    /// is checked here once for all the tests.
    /// </summary>
    private static List<ChannelLook> Burst(FakeController controller)
    {
        List<ChannelLook> sent = controller.Reports().Select(report =>
        {
            Assert.Equal(0x3B, report[1]);
            return new ChannelLook(report[2], report[4], report[5], report[6], report[7]);
        }).ToList();

        Assert.True(sent.Count % 2 == 0, "a burst is two identical passes");
        int half = sent.Count / 2;
        Assert.Equal(sent.Take(half), sent.Skip(half));
        return sent.Take(half).ToList();
    }

    private static List<(int Channel, byte Mode)> Modes(FakeController controller) =>
        Burst(controller).Select(look => (look.Channel, look.Mode)).ToList();

    private static ChannelLighting? Record(string deviceKey, int channel) =>
        AuraChannelStates.Get(AuraChannelStates.All(), deviceKey, channel);

    /// <summary>A channel's stored record, which has to exist; the day it was last seen is not compared.</summary>
    private static ChannelLighting Own(string deviceKey, int channel)
    {
        ChannelLighting? record = Record(deviceKey, channel);
        Assert.NotNull(record);
        return record with { Seen = 0 };
    }

    private static ChannelLighting Look(byte mode, byte red, byte green, byte blue, bool on = true, byte brightness = 0) =>
        new(mode, red, green, blue, on, brightness);

    // ---- Switch off and on ----

    [Fact]
    public void SwitchingOffSendsOffToEveryChannelOfEveryController()
    {
        Stored.State(Board());
        using FakeController a = A();
        using FakeController b = B();

        AuraState result = Switch(false, null, -1, Open(a, b));

        Assert.False(result.On);
        Assert.Equal(new[] { (0, Off), (1, Off) }, Modes(a));
        Assert.Equal(new[] { (0, Off) }, Modes(b));
        Assert.Equal(result, AuraState.Load());
    }

    // Off only clears the power flag: the look stays on record so switching back on has one to return to.
    [Fact]
    public void SwitchingOffKeepsEachChannelsOwnLookOnRecord()
    {
        Stored.State(Board());
        Stored.Look("a", 1, Look(Breathing, 1, 2, 3));
        using FakeController a = A();

        Switch(false, null, -1, Open(a));

        Assert.Equal(Look(Breathing, 1, 2, 3, on: false), Own("a", 1));
        // A channel that was never set on its own is recorded as the board's look, switched off.
        Assert.Equal(Look(Static, 200, 100, 50, on: false), Own("a", 0));
    }

    [Fact]
    public void SwitchingOnReturnsEveryChannelToItsOwnLook()
    {
        Stored.State(Board(on: false));
        Stored.Look("a", 0, Look(Rainbow, 9, 9, 9, on: false));
        Stored.Look("a", 1, Look(Static, 255, 0, 0, on: false));
        using FakeController a = A();
        using FakeController b = B();

        AuraState result = Switch(true, null, -1, Open(a, b));

        Assert.True(result.On);
        Assert.Equal(new[] { new ChannelLook(0, Rainbow, 9, 9, 9), new ChannelLook(1, Static, 255, 0, 0) }, Burst(a));
        // The channel with no record follows the board.
        Assert.Equal(new[] { new ChannelLook(0, Static, 200, 100, 50) }, Burst(b));
        Assert.True(Record("a", 0)!.On);
        Assert.True(Record("a", 1)!.On);
        Assert.Equal(result, AuraState.Load());
    }

    // A preset deleted after it was applied stayed named in the state for good: the window and
    // "-status" kept calling it active although nothing could run it any more.
    [Fact]
    public void SwitchingOnForgetsAPresetThatNoLongerExists()
    {
        Stored.State(Board(on: false, preset: "Gone"));
        using FakeController a = A();

        AuraState result = Switch(true, null, -1, Open(a));

        Assert.True(result.On);
        Assert.Equal("", result.CustomPreset);
        Assert.Equal("", AuraState.Load().CustomPreset);
        Assert.Equal(new[] { new ChannelLook(0, Static, 200, 100, 50), new ChannelLook(1, Static, 200, 100, 50) }, Burst(a));
    }

    [Fact]
    public void SwitchingOffKeepsTheNameOfAPresetThatNoLongerExists()
    {
        // Only switching on looks the preset up; switching off leaves the state's name alone.
        Stored.State(Board(on: true, preset: "Gone"));
        using FakeController a = A();

        AuraState result = Switch(false, null, -1, Open(a));

        Assert.False(result.On);
        Assert.Equal("Gone", AuraState.Load().CustomPreset);
    }

    [Fact]
    public void OffThenOnLeavesTheRecordsExactlyAsTheyWere()
    {
        Stored.State(Board());
        Stored.Look("a", 0, Look(Breathing, 10, 20, 30, brightness: 60));
        Stored.Look("a", 1, Look(Static, 255, 0, 0));
        using FakeController a = A();
        List<AuraDevice> open = Open(a);

        Switch(false, null, -1, open);
        Switch(true, null, -1, open);

        Assert.Equal(Look(Breathing, 10, 20, 30, brightness: 60), Own("a", 0));
        Assert.Equal(Look(Static, 255, 0, 0), Own("a", 1));
    }

    // A channel with its own brightness comes back dimmed to it, the rest at the board's.
    [Fact]
    public void SwitchingOnDimsEachChannelToItsOwnBrightness()
    {
        Stored.State(Board(on: false, brightness: 100));
        Stored.Look("a", 0, Look(Static, 200, 100, 50, on: false, brightness: 50));
        using FakeController a = A();

        Switch(true, null, -1, Open(a));

        Assert.Equal(
            new[] { new ChannelLook(0, Static, 100, 50, 25), new ChannelLook(1, Static, 200, 100, 50) },
            Burst(a));
    }

    [Fact]
    public void SwitchingOneControllerOffLeavesTheOthersAloneAndTheBoardOn()
    {
        Stored.State(Board());
        using FakeController a = A();
        using FakeController b = B();

        AuraState result = Switch(false, "b", -1, Open(a, b));

        Assert.Empty(a.Reports());
        Assert.Equal(new[] { (0, Off) }, Modes(b));
        // Controller a still follows the board, so the board is on.
        Assert.True(result.On);
        Assert.False(Record("b", 0)!.On);
        Assert.Null(Record("a", 0));
    }

    [Fact]
    public void SwitchingTheLastLitChannelOffMakesTheBoardOff()
    {
        Stored.State(Board());
        Stored.Look("a", 0, Look(Static, 1, 1, 1, on: false));
        using FakeController a = FakeController.Addressable("a", headers: 2);

        Switch(false, "a", 1, Open(a));
        AuraState result = Switch(false, "a", 0, Open(a));

        Assert.False(result.On);
    }

    [Fact]
    public void SwitchingOneChannelOffTouchesOnlyThatChannelsRecord()
    {
        Stored.State(Board());
        using FakeController a = A();

        AuraState result = Switch(false, "a", 1, Open(a));

        // A channel that was never set on its own and is not named is left out of the burst.
        Assert.Equal(new[] { (1, Off) }, Modes(a));
        Assert.True(result.On);
        Assert.False(Record("a", 1)!.On);
        Assert.Null(Record("a", 0));
    }

    [Fact]
    public void SwitchingAControllerThatIsNotThereIsNotFoundAndChangesNothing()
    {
        Stored.State(Board());
        string? before = TestData.ReadRaw(AuraState.FileName);
        using FakeController a = A();

        foreach (bool on in new[] { true, false })
        {
            AuraNotFoundException error = Assert.Throws<AuraNotFoundException>(
                () => Switch(on, "missing", -1, Open(a)));

            Assert.Equal(3, error.ExitCode);
        }

        Assert.Empty(a.Reports());
        Assert.Equal(before, TestData.ReadRaw(AuraState.FileName));
        Assert.Null(TestData.ReadRaw(AuraChannelStates.FileName));
    }

    // ---- a controller that drops out ----

    // Nothing reached the hardware, so the state the user asked for is not saved over the real one.
    [Fact]
    public void AControllerThatTakesNothingFailsTheSwitchAndKeepsTheSavedState()
    {
        Stored.State(Board());
        string? before = TestData.ReadRaw(AuraState.FileName);
        using FakeController a = FakeController.Addressable("a", refuseWrites: true);

        Cli.Capture(() => Assert.ThrowsAny<IOException>(() => Switch(false, null, -1, Open(a))));

        Assert.Equal(before, TestData.ReadRaw(AuraState.FileName));
        Assert.True(AuraState.Load().On);
    }

    // One controller dropping a report must not undo what the others already received.
    [Fact]
    public void AControllerThatFailsDoesNotUndoTheOnesThatWork()
    {
        Stored.State(Board());
        using FakeController a = A();
        using FakeController b = FakeController.Addressable("b", headers: 1, refuseWrites: true);
        AuraState result = null!;

        Cli.Capture(() => result = Switch(false, null, -1, Open(a, b)));

        Assert.False(result.On);
        Assert.Equal(new[] { (0, Off), (1, Off) }, Modes(a));
        Assert.Empty(b.Reports());
        Assert.False(AuraState.Load().On);
        // What was named on the failing controller is recorded too: part of it may have arrived.
        Assert.False(Record("b", 0)!.On);
        Assert.False(Record("a", 1)!.On);
    }

    [Fact]
    public void ControllersAreWrittenIndependentlyWhateverOrderTheyFailIn()
    {
        Stored.State(Board());
        using FakeController bad = FakeController.Addressable("bad", headers: 1, refuseWrites: true);
        using FakeController good = A();
        AuraState result = null!;

        Cli.Capture(() => result = Switch(false, null, -1, Open(bad, good)));

        Assert.False(result.On);
        Assert.Equal(new[] { (0, Off), (1, Off) }, Modes(good));
    }

    // ---- ApplyMix directly ----

    [Fact]
    public void ApplyMixNamesEveryChannelItSentTo()
    {
        using FakeController a = A();
        using FakeController b = B();

        List<(string DeviceKey, int Channel)> named = ApplyMix(
            Open(a, b), null, -1, Board(), new ChannelLighting(Static, 1, 2, 3, On: true));

        Assert.Equal(new[] { ("a", 0), ("a", 1), ("b", 0) }, named);
    }

    [Fact]
    public void ApplyMixOnOneChannelStillSendsTheWholeControllerInOneBurst()
    {
        Stored.Look("a", 0, Look(Breathing, 9, 9, 9));
        using FakeController a = A();

        List<(string DeviceKey, int Channel)> named = ApplyMix(
            Open(a), "a", 1, Board(), new ChannelLighting(Static, 1, 2, 3, On: true));

        Assert.Equal(new[] { ("a", 1) }, named);
        Assert.Equal(new[] { new ChannelLook(0, Breathing, 9, 9, 9), new ChannelLook(1, Static, 1, 2, 3) }, Burst(a));
    }

    [Fact]
    public void ApplyMixHonoursTheOnlyFilter()
    {
        using FakeController a = A();
        using FakeController b = B();

        List<(string DeviceKey, int Channel)> named = ApplyMix(
            Open(a, b), null, -1, Board(), look: null, only: device => device.Key == "b");

        Assert.Empty(a.Reports());
        Assert.All(named, n => Assert.Equal("b", n.DeviceKey));
        Assert.NotEmpty(b.Reports());
    }

    [Fact]
    public void ApplyMixThrowsWhenNoControllerTookAnything()
    {
        using FakeController a = FakeController.Addressable("a", refuseWrites: true);
        using FakeController b = FakeController.Addressable("b", refuseWrites: true);

        Cli.Capture(() => Assert.ThrowsAny<IOException>(() =>
            ApplyMix(Open(a, b), null, -1, Board(), new ChannelLighting(Static, 1, 2, 3, On: true))));
    }

    [Fact]
    public void ApplyMixReturnsWhatWasNamedWhenOnlyOneControllerFailed()
    {
        using FakeController good = A();
        using FakeController bad = FakeController.Addressable("bad", headers: 1, refuseWrites: true);
        List<(string DeviceKey, int Channel)> named = null!;

        Cli.Capture(() => named = ApplyMix(
            Open(bad, good), null, -1, Board(), new ChannelLighting(Static, 1, 2, 3, On: true)));

        Assert.Equal(new[] { ("bad", 0), ("a", 0), ("a", 1) }, named);
    }

    [Fact]
    public void ApplyMixToAnUnknownControllerIsNotFound()
    {
        using FakeController a = A();

        AuraNotFoundException error = Assert.Throws<AuraNotFoundException>(
            () => ApplyMix(Open(a), "gone", -1, Board(), look: null));

        Assert.Equal(3, error.ExitCode);
        Assert.Empty(a.Reports());
    }

    // ---- brightness ----

    [Fact]
    public void BoardWideBrightnessDimsEveryChannelAndIsSavedInTheState()
    {
        Stored.State(Board());
        using FakeController a = A();
        using FakeController b = B();

        AuraState result = SetBrightness(40, null, -1, Open(a, b));

        Assert.Equal(40, result.Brightness);
        Assert.Equal(40, AuraState.Load().Brightness);
        Assert.All(Burst(a).Concat(Burst(b)), look =>
        {
            Assert.Equal(Static, look.Mode);
            Assert.Equal((80, 40, 20), (look.Red, look.Green, look.Blue));
        });
    }

    // Brightness is a percentage between 10 and 100; a request outside it is brought back in.
    [Theory]
    [InlineData(0, 10, 20)]
    [InlineData(1, 10, 20)]
    [InlineData(9, 10, 20)]
    [InlineData(10, 10, 20)]
    [InlineData(100, 100, 200)]
    [InlineData(150, 100, 200)]
    [InlineData(255, 100, 200)]
    public void BrightnessIsClampedIntoTheSliderRange(int asked, int stored, int red)
    {
        Stored.State(Board());
        using FakeController a = A();

        AuraState result = SetBrightness((byte)asked, null, -1, Open(a));

        Assert.Equal(stored, result.Brightness);
        Assert.Equal((byte)red, Burst(a)[0].Red);
    }

    // One channel's brightness is its own: the board and the other channels stay where they were.
    [Fact]
    public void ABrightnessForOneChannelIsRecordedForThatChannelOnly()
    {
        Stored.State(Board(brightness: 100));
        using FakeController a = A();

        AuraState result = SetBrightness(50, "a", 1, Open(a));

        Assert.Equal(100, result.Brightness);
        Assert.Equal(100, AuraState.Load().Brightness);
        Assert.Equal(new[] { new ChannelLook(1, Static, 100, 50, 25) }, Burst(a));
        Assert.Equal(50, Record("a", 1)!.Brightness);
        Assert.Null(Record("a", 0));
    }

    // The board-wide value hands every channel back to the board: their own values are cleared.
    [Fact]
    public void ABoardWideBrightnessClearsEveryChannelsOwn()
    {
        Stored.State(Board(brightness: 100));
        Stored.Look("a", 0, Look(Static, 200, 100, 50, brightness: 30));
        Stored.Look("a", 1, Look(Static, 200, 100, 50, brightness: 70));
        using FakeController a = A();

        SetBrightness(60, null, -1, Open(a));

        Assert.Equal(0, Record("a", 0)!.Brightness);
        Assert.Equal(0, Record("a", 1)!.Brightness);
        Assert.All(Burst(a), look => Assert.Equal((120, 60, 30), (look.Red, look.Green, look.Blue)));
    }

    // A dimmer must never light a header the user switched off.
    [Fact]
    public void ABrightnessChangeDoesNotSwitchAChannelBackOn()
    {
        Stored.State(Board());
        Stored.Look("a", 1, Look(Static, 200, 100, 50, on: false));
        using FakeController a = A();

        SetBrightness(40, null, -1, Open(a));

        Assert.Equal(new[] { (0, Static), (1, Off) }, Modes(a));
        Assert.False(Record("a", 1)!.On);
    }

    [Fact]
    public void ABrightnessForOneSwitchedOffChannelKeepsItOff()
    {
        Stored.State(Board());
        Stored.Look("a", 1, Look(Static, 200, 100, 50, on: false));
        using FakeController a = A();

        SetBrightness(40, "a", 1, Open(a));

        Assert.Equal(new[] { (1, Off) }, Modes(a));
        Assert.False(Record("a", 1)!.On);
        Assert.Equal(40, Record("a", 1)!.Brightness);
    }

    // With the board off nothing is sent; the value is stored for when it comes back on.
    [Fact]
    public void ABrightnessWhileTheBoardIsOffSendsNothingButIsKept()
    {
        Stored.State(Board(on: false));
        using FakeController a = A();

        AuraState result = SetBrightness(30, null, -1, Open(a));

        Assert.Empty(a.Reports());
        Assert.Equal(30, result.Brightness);
        Assert.Equal(30, AuraState.Load().Brightness);
        Assert.False(AuraState.Load().On);
    }

    // The value is recorded only once the controller has taken it.
    [Fact]
    public void ABrightnessTheControllerNeverReceivedIsNotRecorded()
    {
        Stored.State(Board(brightness: 100));
        Stored.Look("a", 1, Look(Static, 200, 100, 50, brightness: 30));
        string? before = TestData.ReadRaw(AuraState.FileName);
        using FakeController a = FakeController.Addressable("a", refuseWrites: true);

        Cli.Capture(() => Assert.ThrowsAny<IOException>(() => SetBrightness(80, "a", 1, Open(a))));

        Assert.Equal(before, TestData.ReadRaw(AuraState.FileName));
        Assert.Equal(30, Record("a", 1)!.Brightness);
        Assert.Equal(100, AuraState.Load().Brightness);
    }

    [Fact]
    public void ABrightnessForAControllerThatIsNotThereIsNotFound()
    {
        Stored.State(Board());
        string? before = TestData.ReadRaw(AuraState.FileName);
        using FakeController a = A();

        AuraNotFoundException error = Assert.Throws<AuraNotFoundException>(
            () => SetBrightness(40, "gone", -1, Open(a)));

        Assert.Equal(3, error.ExitCode);
        Assert.Equal(before, TestData.ReadRaw(AuraState.FileName));
    }

    // The firmware colours these itself: the burst carries the mode, there is no colour to dim.
    [Fact]
    public void BrightnessOnAFirmwareModeStillSendsThatMode()
    {
        Stored.State(Board(mode: Rainbow));
        using FakeController a = A();

        SetBrightness(40, null, -1, Open(a));

        Assert.All(Burst(a), look => Assert.Equal(Rainbow, look.Mode));
    }

    // ---- effects ----

    [Fact]
    public void AnEffectOnTheWholeBoardIsSentToEveryChannelAndBecomesTheBoardsLook()
    {
        Stored.State(Board(mode: Static));
        AuraPreset breathing = AuraPresets.All.Single(p => p.Mode == Breathing);
        using FakeController a = A();
        using FakeController b = B();

        AuraState result = ApplyPreset(breathing, null, null, -1, Open(a, b));

        Assert.Equal(Board(mode: Breathing), result);
        Assert.Equal(result, AuraState.Load());
        Assert.Equal(new[] { (0, Breathing), (1, Breathing) }, Modes(a));
        Assert.Equal(new[] { (0, Breathing) }, Modes(b));
        Assert.Equal(Breathing, Record("b", 0)!.Mode);
    }

    [Fact]
    public void AnEffectWithAColourTakesTheColourIntoTheBoardAndTheReports()
    {
        Stored.State(Board());
        AuraPreset staticPreset = AuraPresets.All.Single(p => p.Mode == Static);
        using FakeController a = A();

        AuraState result = ApplyPreset(staticPreset, Color.FromArgb(10, 20, 30), null, -1, Open(a));

        Assert.Equal((10, 20, 30), (result.Red, result.Green, result.Blue));
        Assert.Equal(result, AuraState.Load());
        Assert.All(Burst(a), look => Assert.Equal((10, 20, 30), (look.Red, look.Green, look.Blue)));
    }

    // One channel picking its own effect must not become what every untouched channel falls back to.
    [Fact]
    public void AnEffectForOneChannelLeavesTheBoardsLookAlone()
    {
        Stored.State(Board(mode: Static));
        AuraPreset breathing = AuraPresets.All.Single(p => p.Mode == Breathing);
        using FakeController a = A();

        AuraState result = ApplyPreset(breathing, Color.FromArgb(1, 2, 3), "a", 1, Open(a));

        Assert.Equal(Board(mode: Static), result);
        Assert.Equal(Board(mode: Static), AuraState.Load());
        Assert.Equal(new[] { new ChannelLook(1, Breathing, 1, 2, 3) }, Burst(a));
        Assert.Equal(Look(Breathing, 1, 2, 3), Own("a", 1));
    }

    [Fact]
    public void AnEffectSwitchesTheBoardOnAndDropsAnActiveCustomPreset()
    {
        Stored.State(Board(on: false, preset: "Mine"));
        AuraPreset staticPreset = AuraPresets.All.Single(p => p.Mode == Static);
        using FakeController a = A();

        AuraState result = ApplyPreset(staticPreset, null, null, -1, Open(a));

        Assert.True(result.On);
        Assert.Equal("", result.CustomPreset);
        Assert.Equal("", AuraState.Load().CustomPreset);
    }

    [Fact]
    public void AnEffectOnOneControllerKeepsTheOtherControllersDark()
    {
        Stored.State(Board());
        AuraPreset breathing = AuraPresets.All.Single(p => p.Mode == Breathing);
        using FakeController a = A();
        using FakeController b = B();

        ApplyPreset(breathing, null, "b", -1, Open(a, b));

        Assert.Empty(a.Reports());
        Assert.Equal(new[] { (0, Breathing) }, Modes(b));
    }

    // Two firmware modes on one controller cannot both run: the one picked wins on the other channel too, burst and record.
    [Fact]
    public void AFirmwareEffectPickedForOneChannelIsFollowedByAnotherFirmwareChannel()
    {
        Stored.State(Board());
        Stored.Look("a", 1, Look(11, 0, 0, 0));
        using FakeController a = A();
        AuraPreset rainbow = AuraPresets.All.Single(p => p.Mode == Rainbow);

        ApplyPreset(rainbow, null, "a", 0, Open(a));

        Assert.Equal(new[] { (0, Rainbow), (1, Rainbow) }, Modes(a));
        Assert.Equal(Rainbow, Own("a", 1).Mode);
    }

    [Fact]
    public void AnEffectTheControllerNeverReceivedLeavesTheSavedStateAlone()
    {
        Stored.State(Board(mode: Static));
        string? before = TestData.ReadRaw(AuraState.FileName);
        AuraPreset breathing = AuraPresets.All.Single(p => p.Mode == Breathing);
        using FakeController a = FakeController.Addressable("a", refuseWrites: true);

        Cli.Capture(() => Assert.ThrowsAny<IOException>(() => ApplyPreset(breathing, null, null, -1, Open(a))));

        Assert.Equal(before, TestData.ReadRaw(AuraState.FileName));
    }

    // ---- Send ----

    [Fact]
    public void SendReportsWhetherAnyChannelIsStillOn()
    {
        Stored.Look("a", 0, Look(Static, 1, 1, 1));
        Stored.Look("a", 1, Look(Static, 1, 1, 1));
        using FakeController a = A();
        List<AuraDevice> open = Open(a);

        Assert.True(Send(Static, Board(), "a", 0, open));
        Assert.True(Send(Off, Board(), "a", 0, open));
        Assert.False(Send(Off, Board(), "a", 1, open));
        Assert.True(Send(Static, Board(), "a", 1, open));
    }

    // ---- the helpers the switching is built from ----

    [Fact]
    public void ResolveDevicesTakesOneControllerByKeyOrAll()
    {
        using FakeController a = A();
        using FakeController b = B();
        List<AuraDevice> open = Open(a, b);

        Assert.Same(open, ResolveDevices(open, null));
        Assert.Same(open, ResolveDevices(open, ""));
        Assert.Equal(new[] { b.Device }, ResolveDevices(open, "b"));
    }

    [Fact]
    public void ResolveDevicesFailsForAKeyThatIsNotAttachedAndForNoDevicesAtAll()
    {
        using FakeController a = A();

        Assert.Equal(3, Assert.Throws<AuraNotFoundException>(() => ResolveDevices(Open(a), "gone")).ExitCode);
        Assert.Equal(3, Assert.Throws<AuraNotFoundException>(() => ResolveDevices(new List<AuraDevice>(), null)).ExitCode);
    }

    [Fact]
    public void ChannelsListsThePairsTheRecordsAreKeyedBy()
    {
        using FakeController a = A();
        using FakeController b = B();
        List<AuraDevice> open = Open(a, b);

        Assert.Equal(new[] { ("a", 0), ("a", 1), ("b", 0) }, Channels(open, null, -1));
        Assert.Equal(new[] { ("a", 0), ("a", 1) }, Channels(open, "a", -1));
        Assert.Equal(new[] { ("a", 1) }, Channels(open, "a", 1));
        Assert.Empty(Channels(open, "a", 5));
        Assert.Equal(new[] { ("b", 0) }, Channels(open, null, 0).Where(c => c.DeviceKey == "b"));
    }

    [Fact]
    public void AttachedListsTheControllerKeysInOrder()
    {
        using FakeController a = A();
        using FakeController b = B();

        Assert.Equal(new[] { "a", "b" }, Attached(Open(a, b)));
        Assert.Empty(Attached(new List<AuraDevice>()));
    }

    // A channel with no record has never been set on its own, so it follows the board and counts as on.
    [Fact]
    public void AnyChannelOnCountsAChannelWithoutARecordAsOn()
    {
        using FakeController a = A();

        Assert.True(AnyChannelOn(Open(a)));
    }

    [Fact]
    public void AnyChannelOnIsFalseOnlyWhenEveryChannelHasARecordAndAllAreOff()
    {
        using FakeController a = A();
        List<AuraDevice> open = Open(a);
        Stored.Look("a", 0, Look(Static, 1, 1, 1, on: false));

        Assert.True(AnyChannelOn(open));

        Stored.Look("a", 1, Look(Static, 1, 1, 1, on: false));
        Assert.False(AnyChannelOn(open));

        Stored.Look("a", 1, Look(Static, 1, 1, 1, on: true));
        Assert.True(AnyChannelOn(open));
        Assert.False(AnyChannelOn(new List<AuraDevice>()));
    }

    // ---- custom presets, as far as they can be reached without discovery ----

    private static CustomPreset StoredPreset(string name, string deviceKey = "a")
    {
        var preset = new CustomPreset(name, new List<CustomPresetEntry>
        {
            new(deviceKey, 0, "Onboard", Breathing, 1, 2, 3),
        });

        for (int attempt = 0; attempt < 10; attempt++)
        {
            AuraCustomPresets.Save(new List<CustomPreset> { preset });
            if (AuraCustomPresets.Load().Any(p => p.Name == name))
            {
                return preset;
            }
        }

        throw new IOException("The preset was not stored.");
    }

    // A preset with nothing in it names no controller, so there is nothing to apply it to.
    [Fact]
    public void ACustomPresetWithoutEntriesIsNotFound()
    {
        var empty = new CustomPreset("Empty", new List<CustomPresetEntry>());

        Assert.Equal(3, Assert.Throws<AuraNotFoundException>(() => ApplyCustomPreset(empty)).ExitCode);
    }

    [Fact]
    public void ACustomPresetWithNoControllerPresentIsNotFoundAndChangesNothing()
    {
        Stored.State(Board());
        string? before = TestData.ReadRaw(AuraState.FileName);
        CustomPreset preset = StoredPreset("Mine");

        Assert.Equal(3, Assert.Throws<AuraNotFoundException>(() => ApplyCustomPreset(preset)).ExitCode);
        Assert.Equal(before, TestData.ReadRaw(AuraState.FileName));
        Assert.Null(TestData.ReadRaw(AuraChannelStates.FileName));
    }

    // The active preset is re-applied by switching the whole board on, never by a switch off or a partial one.
    [Fact]
    public void SwitchingOffKeepsTheActiveCustomPresetInTheState()
    {
        StoredPreset("Mine");
        Stored.State(Board(preset: "Mine"));
        using FakeController a = A();

        AuraState result = Switch(false, null, -1, Open(a));

        Assert.False(result.On);
        Assert.Equal("Mine", result.CustomPreset);
        Assert.Equal("Mine", AuraState.Load().CustomPreset);
        Assert.Equal(new[] { (0, Off), (1, Off) }, Modes(a));
    }

    [Fact]
    public void SwitchingOneControllerOnRestoresRecordsInsteadOfApplyingTheCustomPreset()
    {
        StoredPreset("Mine");
        Stored.State(Board(on: false, preset: "Mine"));
        Stored.Look("a", 0, Look(Static, 7, 8, 9, on: false));
        using FakeController a = A();

        AuraState result = Switch(true, "a", -1, Open(a));

        Assert.True(result.On);
        Assert.Equal("Mine", result.CustomPreset);
        Assert.Equal(new ChannelLook(0, Static, 7, 8, 9), Burst(a)[0]);
    }

    // ---- the same on a mainboard controller ----

    [Fact]
    public void SwitchingOffAMainboardControllerSendsTheEffectAndTheColourOfEveryChannel()
    {
        Stored.State(Board());
        using FakeController board = FakeController.Mainboard("m", headers: 3);

        Switch(false, null, -1, Open(board));

        List<byte[]> reports = board.Reports();
        // Four channels, effect and colour each, two passes.
        Assert.Equal(16, reports.Count);
        Assert.Equal(new byte[] { 0x35, 0x36 }, reports.Take(2).Select(r => r[1]));
        Assert.All(reports.Where(r => r[1] == 0x35), r => Assert.Equal(Off, r[5]));
        Assert.Equal(new byte[] { 0, 1, 2, 3, 0, 1, 2, 3 },
            reports.Where(r => r[1] == 0x35).Select(r => r[2]));
    }

    [Fact]
    public void AMainboardControllerKeepsItsFirmwareModeOnEveryChannelWhenOneChannelPicksIt()
    {
        Stored.State(Board());
        using FakeController board = FakeController.Mainboard("m", headers: 3);
        AuraPreset rainbow = AuraPresets.All.Single(p => p.Mode == Rainbow);

        ApplyPreset(rainbow, null, "m", 2, Open(board));

        // The firmware effect runs on the whole controller, so the other channels are sent it too.
        Assert.All(board.Reports().Where(r => r[1] == 0x35), r => Assert.Equal(Rainbow, r[5]));
        Assert.Equal(Rainbow, Record("m", 0)!.Mode);
    }
}
