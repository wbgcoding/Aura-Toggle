using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace AuraToggle.Tests;

/// <summary>
/// The protocol the controller is spoken to in: which commands may leave the app at all, which
/// bytes a look turns into, and how the channel layout comes out of the config table. The
/// controller is a file the test reads back (<see cref="FakeController"/>), never hardware.
/// </summary>
public sealed class AuraDeviceTests : DataFolderTest
{
    private const byte Off = 0;
    private const byte Static = 1;
    private const byte Rainbow = 5;

    // ---- the command allow list ----

    public static IEnumerable<object[]> AllowedCommands() =>
        new byte[] { 0x82, 0xB0, 0x35, 0x36, 0x3B }.Select(command => new object[] { command });

    public static IEnumerable<object[]> RefusedCommands() =>
        Enumerable.Range(0, 256)
            .Where(command => Array.IndexOf(new[] { 0x82, 0xB0, 0x35, 0x36, 0x3B }, command) < 0)
            .Select(command => new object[] { (byte)command });

    [Theory]
    [MemberData(nameof(AllowedCommands))]
    public void TheFiveVerifiedCommandsPassTheFilter(byte command)
    {
        var report = new byte[65];
        report[0] = 0xEC;
        report[1] = command;

        AuraDevice.EnsureAllowed(report);
    }

    // Every other first byte is refused, the flash commit 0x3F above all: it can brick the board.
    [Theory]
    [MemberData(nameof(RefusedCommands))]
    public void EveryOtherCommandIsRefused(byte command)
    {
        var report = new byte[65];
        report[0] = 0xEC;
        report[1] = command;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => AuraDevice.EnsureAllowed(report));
        Assert.Contains($"0x{command:X2}", error.Message);
    }

    // A report with no command byte cannot be vetted, so it is refused instead of read past its end.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AReportTooShortToCarryACommandIsRefused(int length)
    {
        var report = new byte[length];
        if (length > 0)
        {
            report[0] = 0xEC;
        }

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => AuraDevice.EnsureAllowed(report));
        Assert.Contains("0x00", error.Message);
    }

    [Fact]
    public void TheCommitCommandNeverReachesTheController()
    {
        using FakeController controller = FakeController.Mainboard("k");
        var report = new byte[65];
        report[0] = 0xEC;
        report[1] = 0x3F;

        Assert.Throws<InvalidOperationException>(() => controller.Device.Write(report));
        Assert.Empty(controller.Reports());
    }

    // Byte 4 of the colour command picks the shutdown effect, which lives in the controller flash.
    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    [InlineData(0x80)]
    [InlineData(0xFF)]
    public void TheShutdownEffectOfTheColourCommandIsRefused(byte flag)
    {
        using FakeController controller = FakeController.Mainboard("k");
        var report = new byte[65];
        report[0] = 0xEC;
        report[1] = 0x36;
        report[4] = flag;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => controller.Device.Write(report));
        Assert.Contains("flash", error.Message);
        Assert.Empty(controller.Reports());
    }

    [Fact]
    public void TheColourCommandForTheRunningEffectIsWritten()
    {
        using FakeController controller = FakeController.Mainboard("k");
        var report = new byte[65];
        report[0] = 0xEC;
        report[1] = 0x36;
        report[4] = 0x00;
        report[5] = 7;

        controller.Device.Write(report);

        Assert.Equal(report, Assert.Single(controller.Reports()));
    }

    // Byte 4 only matters for the colour command; the other verified commands keep their own layout.
    [Theory]
    [InlineData(0x35)]
    [InlineData(0x3B)]
    [InlineData(0x82)]
    [InlineData(0xB0)]
    public void ByteFourOfTheOtherCommandsIsNotTheShutdownFlag(byte command)
    {
        using FakeController controller = FakeController.Mainboard("k");
        var report = new byte[65];
        report[0] = 0xEC;
        report[1] = command;
        report[4] = 0x01;

        controller.Device.Write(report);

        Assert.Single(controller.Reports());
    }

    // ---- Verified and CarriesColour ----

    [Fact]
    public void OffAndEveryKnownModeAreSentAsTheyAre()
    {
        Assert.Equal(Off, AuraDevice.Verified(Off));
        foreach (AuraPreset preset in AuraPresets.All)
        {
            Assert.Equal(preset.Mode, AuraDevice.Verified(preset.Mode));
        }
    }

    // A mode from a hand-edited file that this build never verified falls back to the ASUS default.
    [Fact]
    public void AnyOtherModeFallsBackToRainbow()
    {
        var known = AuraPresets.All.Select(preset => (int)preset.Mode).Append(0).ToHashSet();

        foreach (int mode in Enumerable.Range(0, 256).Where(mode => !known.Contains(mode)))
        {
            Assert.Equal(AuraState.ModeRainbow, AuraDevice.Verified((byte)mode));
        }

        Assert.Equal(Rainbow, AuraState.ModeRainbow);
    }

    [Fact]
    public void OnlyTheFourFirmwareColouredModesSkipTheColourCommand()
    {
        var withoutColour = AuraPresets.All.Where(preset => !AuraDevice.CarriesColour(preset.Mode))
            .Select(preset => (int)preset.Mode).OrderBy(mode => mode).ToList();

        Assert.Equal(new[] { 4, 5, 6, 11 }, withoutColour);
    }

    [Fact]
    public void OffAndUnknownModesKeepTheirColourCommand()
    {
        Assert.True(AuraDevice.CarriesColour(Off));

        var known = AuraPresets.All.Select(preset => (int)preset.Mode).ToHashSet();
        foreach (int mode in Enumerable.Range(0, 256).Where(mode => !known.Contains(mode)))
        {
            Assert.True(AuraDevice.CarriesColour((byte)mode));
        }
    }

    // ---- the reports a mainboard controller gets ----

    private static byte[] Command(params byte[] head)
    {
        var report = new byte[65];
        report[0] = 0xEC;
        Array.Copy(head, 0, report, 1, head.Length);
        return report;
    }

    /// <summary>The 0x36 report for <paramref name="count"/> LEDs from <paramref name="start"/>.</summary>
    private static byte[] Colour(int start, int count, byte red, byte green, byte blue)
    {
        int mask = ((1 << count) - 1) << start;
        byte[] report = Command(0x36, (byte)(mask >> 8), (byte)(mask & 0xFF), 0x00);
        for (int led = 0; led < count; led++)
        {
            int at = 5 + ((start + led) * 3);
            report[at] = red;
            report[at + 1] = green;
            report[at + 2] = blue;
        }

        return report;
    }

    private static void AssertTwice(List<byte[]> actual, params byte[][] pass)
    {
        Assert.Equal(pass.Length * 2, actual.Count);
        for (int i = 0; i < actual.Count; i++)
        {
            Assert.Equal(pass[i % pass.Length], actual[i]);
        }
    }

    // Layout of the reference board: 4 onboard LEDs (channel 0), then one LED per ARGB header.
    [Theory]
    [InlineData(0, 0, 4)]
    [InlineData(1, 4, 1)]
    [InlineData(2, 5, 1)]
    [InlineData(3, 6, 1)]
    public void AMainboardChannelGetsTheEffectThenTheColourOfItsOwnLeds(int channel, int start, int count)
    {
        using FakeController controller = FakeController.Mainboard("k");

        controller.Device.Apply(new[] { new ChannelLook(channel, Static, 10, 20, 30) });

        AssertTwice(controller.Reports(),
            Command(0x35, (byte)channel, 0x00, 0x00, Static),
            Colour(start, count, 10, 20, 30));
    }

    [Fact]
    public void TheMainboardLayoutNumbersTheOnboardZoneFirstThenTheHeaders()
    {
        using FakeController controller = FakeController.Mainboard("k");

        Assert.Equal(4, controller.Device.ChannelCount);
        Assert.Equal(
            new[] { new AuraChannel(0, true, 0), new AuraChannel(1, false, 1), new AuraChannel(2, false, 2), new AuraChannel(3, false, 3) },
            controller.Device.Channels);
    }

    [Fact]
    public void EveryReportHasTheLengthTheDeviceReportedAndTheReportId()
    {
        using FakeController controller = FakeController.Mainboard("k");

        controller.Device.Apply(new[] { new ChannelLook(0, Static, 1, 2, 3), new ChannelLook(2, Off, 0, 0, 0) });

        List<byte[]> reports = controller.Reports();
        Assert.Equal(8, reports.Count);
        Assert.All(reports, report =>
        {
            Assert.Equal(65, report.Length);
            Assert.Equal(0xEC, report[0]);
        });
    }

    // The firmware colours these four itself, so the colour command is left out of the burst.
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(11)]
    public void AFirmwareColouredModeSendsNoColourCommand(byte mode)
    {
        using FakeController controller = FakeController.Mainboard("k");

        controller.Device.Apply(new[] { new ChannelLook(1, mode, 9, 9, 9) });

        AssertTwice(controller.Reports(), Command(0x35, 1, 0x00, 0x00, mode));
    }

    [Fact]
    public void EveryPresetSendsTheColourCommandExactlyWhenItUsesColour()
    {
        foreach (AuraPreset preset in AuraPresets.All)
        {
            using FakeController controller = FakeController.Mainboard("k");

            controller.Device.Apply(new[] { new ChannelLook(0, preset.Mode, 1, 2, 3) });

            Assert.Equal(
                preset.UsesColour ? new byte[] { 0x35, 0x36, 0x35, 0x36 } : new byte[] { 0x35, 0x35 },
                controller.Reports().Select(report => report[1]));
        }
    }

    // Off keeps its colour command: switching off is the path that must send the full set of reports.
    [Fact]
    public void OffStillSendsTheColourCommand()
    {
        using FakeController controller = FakeController.Mainboard("k");

        controller.Device.Apply(new[] { new ChannelLook(0, Off, 0, 0, 0) });

        AssertTwice(controller.Reports(), Command(0x35, 0, 0x00, 0x00, Off), Colour(0, 4, 0, 0, 0));
    }

    // An unverified mode number is never sent: it becomes rainbow, which carries no colour command.
    [Fact]
    public void AnUnknownModeIsSentAsRainbow()
    {
        using FakeController controller = FakeController.Mainboard("k");

        controller.Device.Apply(new[] { new ChannelLook(0, 200, 5, 5, 5) });

        AssertTwice(controller.Reports(), Command(0x35, 0, 0x00, 0x00, Rainbow));
    }

    // A record can outlive the hardware it describes, so a look for a missing channel is ignored.
    [Fact]
    public void ALookForAChannelTheControllerLacksSendsNothing()
    {
        using FakeController controller = FakeController.Mainboard("k");

        controller.Device.Apply(new[] { new ChannelLook(9, Static, 1, 1, 1) });
        controller.Device.Apply(Array.Empty<ChannelLook>());

        Assert.Empty(controller.Reports());
    }

    // Both passes sweep the whole list, so the channels come up one report apart rather than in stages.
    [Fact]
    public void TheBurstSweepsTheWholeListTwiceInsteadOfOneChannelAtATime()
    {
        using FakeController controller = FakeController.Mainboard("k");

        controller.Device.Apply(new[] { new ChannelLook(2, Static, 1, 2, 3), new ChannelLook(0, Static, 4, 5, 6) });

        AssertTwice(controller.Reports(),
            Command(0x35, 2, 0x00, 0x00, Static),
            Colour(5, 1, 1, 2, 3),
            Command(0x35, 0, 0x00, 0x00, Static),
            Colour(0, 4, 4, 5, 6));
    }

    [Fact]
    public void AMissingChannelInTheMiddleOfAListDoesNotStopTheRest()
    {
        using FakeController controller = FakeController.Mainboard("k");

        controller.Device.Apply(new[] { new ChannelLook(1, Static, 1, 1, 1), new ChannelLook(77, Static, 2, 2, 2), new ChannelLook(3, Static, 3, 3, 3) });

        AssertTwice(controller.Reports(),
            Command(0x35, 1, 0x00, 0x00, Static),
            Colour(4, 1, 1, 1, 1),
            Command(0x35, 3, 0x00, 0x00, Static),
            Colour(6, 1, 3, 3, 3));
    }

    // A mainboard controller whose board has no onboard lighting still speaks 0x35/0x36, with the
    // first header as channel 0; it does not take the addressable command.
    [Fact]
    public void AMainboardControllerWithoutOnboardLedsStartsItsChannelsAtTheFirstHeader()
    {
        using FakeController controller = FakeController.Mainboard("k", headers: 2, onboardLeds: 0);

        Assert.Equal(new[] { new AuraChannel(0, false, 1), new AuraChannel(1, false, 2) }, controller.Device.Channels);

        controller.Device.Apply(new[] { new ChannelLook(1, Static, 1, 2, 3) });

        AssertTwice(controller.Reports(), Command(0x35, 1, 0x00, 0x00, Static), Colour(1, 1, 1, 2, 3));
    }

    // ---- the 16 LED limit of the colour mask ----

    [Fact]
    public void AColourMaskCoversSixteenLedsAndNoMore()
    {
        using FakeController controller = FakeController.Mainboard("k", headers: 2, onboardLeds: 15);

        controller.Device.Apply(new[]
        {
            new ChannelLook(0, Static, 1, 2, 3), // LEDs 0..14
            new ChannelLook(1, Static, 4, 5, 6), // LED 15, the last one the mask reaches
            new ChannelLook(2, Static, 7, 8, 9), // LED 16, past the mask
        });

        AssertTwice(controller.Reports(),
            Command(0x35, 0, 0x00, 0x00, Static),
            Colour(0, 15, 1, 2, 3),
            Command(0x35, 1, 0x00, 0x00, Static),
            Colour(15, 1, 4, 5, 6),
            Command(0x35, 2, 0x00, 0x00, Static));
    }

    [Fact]
    public void AnOnboardZoneLongerThanTheMaskIsColouredUpToLedSixteenOnly()
    {
        using FakeController controller = FakeController.Mainboard("k", headers: 1, onboardLeds: 20);

        controller.Device.Apply(new[] { new ChannelLook(0, Static, 1, 2, 3), new ChannelLook(1, Static, 4, 5, 6) });

        AssertTwice(controller.Reports(),
            Command(0x35, 0, 0x00, 0x00, Static),
            Colour(0, 16, 1, 2, 3),
            Command(0x35, 1, 0x00, 0x00, Static));
    }

    [Fact]
    public void ThereAreNoMoreChannelsThanAByteCanAddress()
    {
        using FakeController controller = FakeController.Mainboard("k", headers: 255);

        Assert.Equal(255, controller.Device.ChannelCount);
        Assert.Equal(254, controller.Device.Channels[^1].Header);
    }

    [Fact]
    public void ABoardWithNeitherLedsNorHeadersHasNoChannels()
    {
        using FakeController none = FakeController.Mainboard("a", headers: 0, onboardLeds: 0);
        using FakeController onlyOnboard = FakeController.Mainboard("b", headers: 0, onboardLeds: 4);

        Assert.Equal(0, none.Device.ChannelCount);
        Assert.Empty(none.Device.Channels);
        Assert.Equal(new[] { new AuraChannel(0, true, 0) }, onlyOnboard.Device.Channels);
    }

    // ---- the reports an addressable controller gets ----

    [Fact]
    public void AnAddressableControllerHasNoFixedZoneSoHeaderOneIsChannelZero()
    {
        using FakeController controller = FakeController.Addressable("k", headers: 3);

        Assert.Equal(
            new[] { new AuraChannel(0, false, 1), new AuraChannel(1, false, 2), new AuraChannel(2, false, 3) },
            controller.Device.Channels);
    }

    // The product id decides the command set: an onboard LED count in the table adds no zone there.
    [Fact]
    public void TheOnboardLedCountOfAnAddressableControllerAddsNoChannel()
    {
        using FakeController controller = FakeController.Addressable("k", headers: 2, onboardLeds: 4);

        Assert.Equal(2, controller.Device.ChannelCount);
        Assert.DoesNotContain(controller.Device.Channels, channel => channel.Onboard);
    }

    [Theory]
    [InlineData((ushort)0x1867)]
    [InlineData((ushort)0x1872)]
    [InlineData((ushort)0x18A3)]
    [InlineData((ushort)0x18A5)]
    [InlineData((ushort)0x1889)]
    public void EveryAddressableProductIdTakesTheInlineColourCommand(ushort pid)
    {
        using FakeController controller = FakeController.Addressable("k", headers: 2, pid: pid);

        controller.Device.Apply(new[] { new ChannelLook(1, Static, 10, 20, 30) });

        AssertTwice(controller.Reports(), Command(0x3B, 1, 0x00, Static, 10, 20, 30));
    }

    [Theory]
    [InlineData((ushort)0x18F3)]
    [InlineData((ushort)0x1939)]
    [InlineData((ushort)0x19AF)]
    [InlineData((ushort)0x1AA6)]
    [InlineData((ushort)0x1BED)]
    [InlineData((ushort)0x1234)]
    public void EveryOtherProductIdTakesTheMainboardCommands(ushort pid)
    {
        using FakeController controller = FakeController.Mainboard("k", pid: pid);

        controller.Device.Apply(new[] { new ChannelLook(0, Static, 10, 20, 30) });

        AssertTwice(controller.Reports(), Command(0x35, 0, 0x00, 0x00, Static), Colour(0, 4, 10, 20, 30));
    }

    // The inline command carries the colour whatever the mode, so even a firmware mode keeps it.
    [Theory]
    [InlineData(Off)]
    [InlineData(Rainbow)]
    [InlineData(11)]
    public void AnAddressableLookAlwaysCarriesItsColour(byte mode)
    {
        using FakeController controller = FakeController.Addressable("k");

        controller.Device.Apply(new[] { new ChannelLook(0, mode, 1, 2, 3) });

        AssertTwice(controller.Reports(), Command(0x3B, 0, 0x00, mode, 1, 2, 3));
    }

    [Fact]
    public void AnAddressableUnknownModeIsSentAsRainbowToo()
    {
        using FakeController controller = FakeController.Addressable("k");

        controller.Device.Apply(new[] { new ChannelLook(0, 99, 1, 2, 3) });

        AssertTwice(controller.Reports(), Command(0x3B, 0, 0x00, Rainbow, 1, 2, 3));
    }

    [Fact]
    public void AnAddressableLookForAMissingChannelSendsNothing()
    {
        using FakeController controller = FakeController.Addressable("k", headers: 2);

        controller.Device.Apply(new[] { new ChannelLook(2, Static, 1, 2, 3) });

        Assert.Empty(controller.Reports());
    }

    // ---- a controller that refuses writes ----

    [Fact]
    public void AWriteTheControllerRefusesSurfacesAsAnIoErrorNamingTheCommand()
    {
        using FakeController controller = FakeController.Mainboard("k", refuseWrites: true);
        IOException? error = null;

        Cli.Capture(() =>
        {
            error = Assert.ThrowsAny<IOException>(() =>
                controller.Device.Apply(new[] { new ChannelLook(0, Static, 1, 2, 3) }));
        });

        Assert.NotNull(error);
        Assert.Contains("0x35", error.Message);
        Assert.NotNull(error.InnerException);
        Assert.Empty(controller.Reports());
    }

    // ---- name, key and summaries ----

    [Fact]
    public void TheKeyIsTheDevicePathAndTheNameTheProductString()
    {
        using FakeController controller = FakeController.Mainboard(@"\\?\hid#vid_0b05&pid_18f3#a", product: "My Board");

        Assert.Equal(@"\\?\hid#vid_0b05&pid_18f3#a", controller.Device.Key);
        Assert.Equal("My Board", controller.Device.Name);
    }

    [Fact]
    public void SummarizeKeepsTheNamesAndNumbersTheUnnamedOnesInOrder()
    {
        using FakeController named = FakeController.Mainboard("a", product: "Aura Terminal");
        using FakeController first = FakeController.Addressable("b", product: "");
        using FakeController second = FakeController.Mainboard("c", headers: 1, product: "");
        List<AuraDeviceSummary>? summaries = null;

        Cli.Capture(() =>
            summaries = AuraDevice.Summarize(new List<AuraDevice> { first.Device, named.Device, second.Device }));

        Assert.NotNull(summaries);
        Assert.Equal(new[] { "b", "a", "c" }, summaries.Select(s => s.Key));
        Assert.Equal(new[] { "Aura Controller #1", "Aura Terminal", "Aura Controller #2" }, summaries.Select(s => s.Name));
        Assert.Equal(first.Device.Channels, summaries[0].Channels);
        Assert.Equal(second.Device.Channels, summaries[2].Channels);
    }

    [Fact]
    public void SummarizeOfNoDevicesIsEmpty()
    {
        Assert.Empty(AuraDevice.Summarize(new List<AuraDevice>()));
    }

    // ---- discovery without hardware ----

    [Fact]
    public void WithNoControllerDiscoveryThrowsNotFoundWithExitCodeThree()
    {
        AuraNotFoundException error = Assert.Throws<AuraNotFoundException>(AuraDevice.DiscoverAll);

        Assert.Equal(3, error.ExitCode);
        Assert.True(error.WorthRetrying);
    }

    [Fact]
    public void TryDiscoverAllReportsTheExitCodeInsteadOfThrowing()
    {
        bool found = AuraDevice.TryDiscoverAll(out List<AuraDevice> devices, out int exitCode);

        Assert.False(found);
        Assert.Empty(devices);
        Assert.Equal(3, exitCode);
    }

    [Fact]
    public void ListDevicesIsEmptyWithTheReasonWhenNoControllerAnswers()
    {
        List<AuraDeviceSummary> listed = AuraDevice.ListDevices(out int exitCode);

        Assert.Empty(listed);
        Assert.Equal(3, exitCode);
        Assert.Empty(AuraDevice.ListDevices());
    }

    [Fact]
    public void NotFoundCarriesItsMessageExitCodeAndRetryAdvice()
    {
        var retry = new AuraNotFoundException("gone", 4);
        var final = new AuraNotFoundException("gone for good", 3, worthRetrying: false);

        Assert.Equal("gone", retry.Message);
        Assert.Equal(4, retry.ExitCode);
        Assert.True(retry.WorthRetrying);
        Assert.Equal("gone for good", final.Message);
        Assert.Equal(3, final.ExitCode);
        Assert.False(final.WorthRetrying);
        Assert.IsAssignableFrom<Exception>(retry);
    }

    // ---- channel labels ----

    private static Dictionary<string, string> Chosen(params (string Device, int Channel, string Name)[] names) =>
        names.ToDictionary(n => AuraFiles.ChannelKey(n.Device, n.Channel), n => n.Name);

    private static string Label(AuraDeviceSummary device, AuraChannel channel, bool withDevice,
        Dictionary<string, string> chosen)
    {
        string label = "";
        Cli.Capture(() => label = ChannelLabels.For(device, channel, withDevice, chosen));
        return label;
    }

    [Fact]
    public void ADefaultLabelIsOnboardOrTheHeaderNumber()
    {
        AuraDeviceSummary device = Layouts.Mainboard("k", headers: 3, name: "Board");
        var none = new Dictionary<string, string>();

        Assert.Equal("Onboard", Label(device, device.Channels[0], false, none));
        Assert.Equal("ARGB 1", Label(device, device.Channels[1], false, none));
        Assert.Equal("ARGB 3", Label(device, device.Channels[3], false, none));
    }

    // The label counts headers, not indexes: on an addressable controller header 1 is channel 0.
    [Fact]
    public void AnAddressableLabelFollowsTheHeaderNotTheIndex()
    {
        AuraDeviceSummary device = Layouts.Addressable("k", headers: 2, name: "Strip");
        var none = new Dictionary<string, string>();

        Assert.Equal("ARGB 1", Label(device, device.Channels[0], false, none));
        Assert.Equal("ARGB 2", Label(device, device.Channels[1], false, none));
    }

    [Fact]
    public void AChosenNameWinsForItsOwnChannelOnly()
    {
        AuraDeviceSummary device = Layouts.Mainboard("k");
        Dictionary<string, string> chosen = Chosen(("k", 2, "Fans"), ("other", 1, "Pump"));

        Assert.Equal("Fans", Label(device, device.Channels[2], false, chosen));
        Assert.Equal("ARGB 1", Label(device, device.Channels[1], false, chosen));
    }

    [Fact]
    public void AnEmptyChosenNameFallsBackToTheDefault()
    {
        AuraDeviceSummary device = Layouts.Mainboard("k");

        Assert.Equal("Onboard", Label(device, device.Channels[0], false, Chosen(("k", 0, ""))));
    }

    [Fact]
    public void ALabelForAMachineWithSeveralControllersNamesTheController()
    {
        AuraDeviceSummary device = Layouts.Mainboard("k", name: "Board");
        Dictionary<string, string> chosen = Chosen(("k", 1, "Fans"));

        Assert.Equal("Board - Onboard", Label(device, device.Channels[0], true, chosen));
        Assert.Equal("Board - Fans", Label(device, device.Channels[1], true, chosen));
        Assert.Equal("Board - ARGB 2", Label(device, device.Channels[2], true, chosen));
    }

    // The label is formatted from the current interface language, so a language change relabels it.
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("nl")]
    [InlineData("pl")]
    [InlineData("pt")]
    [InlineData("tr")]
    [InlineData("zh")]
    public void ALabelUsesTheStringsOfTheInterfaceLanguage(string language)
    {
        AuraDeviceSummary device = Layouts.Mainboard("k", name: "Board");
        string old = Strings.Override;
        try
        {
            Strings.Override = language;
            string onboard = Strings.InLanguage("ChannelOnboard", language);
            string header = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                Strings.InLanguage("ChannelHeader", language), 2);
            string qualified = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                Strings.InLanguage("ChannelQualified", language), "Board", header);
            var none = new Dictionary<string, string>();

            Assert.Equal(onboard, ChannelLabels.For(device, device.Channels[0], false, none));
            Assert.Equal(header, ChannelLabels.For(device, device.Channels[2], false, none));
            Assert.Equal(qualified, ChannelLabels.For(device, device.Channels[2], true, none));
        }
        finally
        {
            Strings.Override = old;
        }
    }
}
