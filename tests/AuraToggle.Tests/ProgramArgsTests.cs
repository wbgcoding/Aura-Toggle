using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace AuraToggle.Tests;

/// <summary>Argument handling: spelling of commands, -device/-channel extraction, colours and target lookup.</summary>
public sealed class ProgramArgsTests : DataFolderTest
{
    // ---- Normalise -------------------------------------------------------------------------

    [Theory]
    [InlineData("-on", "on")]
    [InlineData("--on", "on")]
    [InlineData("/on", "on")]
    [InlineData("on", "on")]
    [InlineData("-ON", "on")]
    [InlineData("--Toggle", "toggle")]
    [InlineData("//On", "on")]
    [InlineData("-/-/x", "x")]
    [InlineData("/?", "?")]
    [InlineData("-", "")]
    [InlineData("", "")]
    public void NormaliseStripsDashesAndSlashesAndLowercases(string argument, string expected)
    {
        Assert.Equal(expected, Program.Normalise(argument));
    }

    [Fact]
    public void NormaliseKeepsInnerDashesAndDoesNotTrimWhitespace()
    {
        Assert.Equal("a-b", Program.Normalise("--A-B"));
        Assert.Equal(" on", Program.Normalise("- on"));
    }

    [Fact]
    public void NormaliseDoesNotDependOnTheCurrentCulture()
    {
        // The Turkish dotless i must not turn "-LIST" into something that is not "list".
        CultureInfo old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("list", Program.Normalise("-LIST"));
            Assert.Equal("light", Program.Normalise("-LIGHT"));
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }

    // ---- ExtractTargeting ------------------------------------------------------------------

    private static (string[] Positional, string? Device, string? Channel) Extract(params string[] args)
    {
        (List<string> rest, string? device, string? channel) = Program.ExtractTargeting(args);
        return (rest.ToArray(), device, channel);
    }

    [Fact]
    public void NoTargetingLeavesEveryTokenInOrder()
    {
        var (rest, device, channel) = Extract("-preset", "static", "red");

        Assert.Equal(new[] { "-preset", "static", "red" }, rest);
        Assert.Null(device);
        Assert.Null(channel);
    }

    [Fact]
    public void EmptyArgumentListGivesEmptyResult()
    {
        var (rest, device, channel) = Extract();

        Assert.Empty(rest);
        Assert.Null(device);
        Assert.Null(channel);
    }

    [Theory]
    [InlineData("-device")]
    [InlineData("--device")]
    [InlineData("/device")]
    [InlineData("-DEVICE")]
    [InlineData("/Device")]
    public void DeviceIsRecognisedInEverySpelling(string flag)
    {
        var (rest, device, channel) = Extract("-on", flag, "2");

        Assert.Equal(new[] { "-on" }, rest);
        Assert.Equal("2", device);
        Assert.Null(channel);
    }

    [Theory]
    [InlineData("-channel")]
    [InlineData("--channel")]
    [InlineData("/channel")]
    [InlineData("-CHANNEL")]
    public void ChannelIsRecognisedInEverySpelling(string flag)
    {
        var (rest, device, channel) = Extract("-on", flag, "1.2");

        Assert.Equal(new[] { "-on" }, rest);
        Assert.Null(device);
        Assert.Equal("1.2", channel);
    }

    [Fact]
    public void TargetingMayComeFirstMiddleOrLast()
    {
        string[] expected = { "-preset", "static", "red" };

        var first = Extract("-device", "1", "-channel", "2", "-preset", "static", "red");
        var middle = Extract("-preset", "-device", "1", "static", "-channel", "2", "red");
        var last = Extract("-preset", "static", "red", "-device", "1", "-channel", "2");

        foreach (var parsed in new[] { first, middle, last })
        {
            Assert.Equal(expected, parsed.Positional);
            Assert.Equal("1", parsed.Device);
            Assert.Equal("2", parsed.Channel);
        }
    }

    [Fact]
    public void ChannelBeforeDeviceIsTheSameAsDeviceBeforeChannel()
    {
        var parsed = Extract("-channel", "3", "-device", "2", "-off");

        Assert.Equal(new[] { "-off" }, parsed.Positional);
        Assert.Equal("2", parsed.Device);
        Assert.Equal("3", parsed.Channel);
    }

    [Fact]
    public void AFlagWithoutAValueStaysPositional()
    {
        // Last token: nothing to consume, so it is not swallowed and the command line is rejected later.
        var device = Extract("-on", "-device");
        var channel = Extract("-on", "-channel");

        Assert.Equal(new[] { "-on", "-device" }, device.Positional);
        Assert.Null(device.Device);
        Assert.Equal(new[] { "-on", "-channel" }, channel.Positional);
        Assert.Null(channel.Channel);
    }

    [Fact]
    public void ABareWordDeviceOrChannelIsNotTheOption()
    {
        // A channel or preset genuinely named "device" must not be mistaken for the option.
        var parsed = Extract("-preset", "static", "device", "channel");

        Assert.Equal(new[] { "-preset", "static", "device", "channel" }, parsed.Positional);
        Assert.Null(parsed.Device);
        Assert.Null(parsed.Channel);
    }

    [Fact]
    public void TheValueAfterAFlagIsTakenWhateverItLooksLike()
    {
        // The value may itself look like a flag or be empty.
        var dash = Extract("-on", "-device", "-channel");
        var empty = Extract("-on", "-channel", "");

        Assert.Equal(new[] { "-on" }, dash.Positional);
        Assert.Equal("-channel", dash.Device);
        Assert.Null(dash.Channel);
        Assert.Equal("", empty.Channel);
    }

    [Fact]
    public void ADuplicatedFlagKeepsTheLastValue()
    {
        var parsed = Extract("-on", "-device", "1", "-device", "2", "-channel", "3", "-channel", "4");

        Assert.Equal(new[] { "-on" }, parsed.Positional);
        Assert.Equal("2", parsed.Device);
        Assert.Equal("4", parsed.Channel);
    }

    [Fact]
    public void AnEmptyArgumentIsKeptAsPositional()
    {
        var parsed = Extract("", "-on");

        Assert.Equal(new[] { "", "-on" }, parsed.Positional);
    }

    // ---- TryParseColour --------------------------------------------------------------------

    [Theory]
    [InlineData("#FF0000", 255, 0, 0)]
    [InlineData("FF0000", 255, 0, 0)]
    [InlineData("ff0000", 255, 0, 0)]
    [InlineData("#20C0FF", 0x20, 0xC0, 0xFF)]
    [InlineData("#20c0ff", 0x20, 0xC0, 0xFF)]
    [InlineData("  #00FF7F  ", 0, 255, 127)]
    [InlineData("red", 255, 0, 0)]
    [InlineData("Red", 255, 0, 0)]
    [InlineData("RED", 255, 0, 0)]
    [InlineData("  blue ", 0, 0, 255)]
    [InlineData("Lime", 0, 255, 0)]
    public void AcceptedColours(string text, int red, int green, int blue)
    {
        Assert.True(Program.TryParseColour(text, out Color colour));
        Assert.Equal(Color.FromArgb(255, red, green, blue).ToArgb(), colour.ToArgb());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("#FFF")]
    [InlineData("FFF")]
    [InlineData("#GG0000")]
    [InlineData("#FF00000")]
    [InlineData("#FF0000FF")]
    [InlineData("12345")]
    [InlineData(" 12345")]
    [InlineData("0xFF0000")]
    [InlineData("not-a-colour")]
    [InlineData("transparent")]
    [InlineData("Transparent")]
    [InlineData("control")]
    [InlineData("Window")]
    [InlineData("ControlText")]
    [InlineData("ActiveBorder")]
    [InlineData("#-1")]
    public void RejectedColours(string text)
    {
        // Transparent and the system colours are known names but not colours the lighting can show.
        Assert.False(Program.TryParseColour(text, out _));
    }

    // ---- DefaultChannelName ----------------------------------------------------------------

    [Fact]
    public void DefaultNamesAreOnboardAndArgbNumber()
    {
        Assert.Equal("Onboard", Program.DefaultChannelName(Layouts.Onboard(), "en"));
        Assert.Equal("ARGB 1", Program.DefaultChannelName(Layouts.Header(1, 1), "en"));
        Assert.Equal("ARGB 3", Program.DefaultChannelName(Layouts.Header(3, 3), "en"));
    }

    [Fact]
    public void DefaultNameFollowsTheHeaderNumberNotTheChannelIndex()
    {
        // An addressable controller has no fixed zone: header 1 is channel index 0.
        Assert.Equal("ARGB 1", Program.DefaultChannelName(Layouts.Header(0, 1), "en"));
        Assert.Equal("ARGB 2", Program.DefaultChannelName(Layouts.Header(1, 2), "en"));
    }

    [Fact]
    public void DefaultNameCanBeAskedForInAnyLanguage()
    {
        string spanish = Program.DefaultChannelName(Layouts.Onboard(), "es");

        Assert.NotEqual("Onboard", spanish);
        Assert.Equal(Strings.InLanguage("ChannelOnboard", "es"), spanish);
        Assert.Equal("ARGB 2", Program.DefaultChannelName(Layouts.Header(2, 2), "es"));
    }

    // ---- FlattenChannels / DescribeChannel -------------------------------------------------

    [Fact]
    public void FlattenNumbersControllersChannelsAndTheFlatList()
    {
        List<Program.ChannelEntry> entries = Layouts.Entries(Layouts.Two());

        Assert.Equal(6, entries.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, entries.Select(e => e.FlatNumber));
        Assert.Equal(new[] { 1, 1, 1, 1, 2, 2 }, entries.Select(e => e.DeviceNumber));
        Assert.Equal(new[] { 1, 2, 3, 4, 1, 2 }, entries.Select(e => e.ChannelNumber));
        Assert.Equal(new[] { 0, 1, 2, 3, 0, 1 }, entries.Select(e => e.ChannelIndex));
        Assert.Equal(new[] { "dev-a", "dev-a", "dev-a", "dev-a", "dev-b", "dev-b" }, entries.Select(e => e.DeviceKey));
    }

    [Fact]
    public void FlattenOfNoControllersOrAControllerWithoutChannelsIsEmpty()
    {
        Assert.Empty(Program.FlattenChannels(new List<AuraDeviceSummary>()));
        Assert.Empty(Program.FlattenChannels(new List<AuraDeviceSummary>
        {
            new("dev-x", "Empty", new List<AuraChannel>()),
        }));
    }

    [Fact]
    public void DescribeIsFlatNumberDeviceDotChannelAndEnglishNameSeparatedByTabs()
    {
        List<Program.ChannelEntry> entries = Layouts.Entries(Layouts.Two());

        Assert.Equal("1\t1.1\tOnboard", Program.DescribeChannel(entries[0]));
        Assert.Equal("4\t1.4\tARGB 3", Program.DescribeChannel(entries[3]));
        Assert.Equal("5\t2.1\tARGB 1", Program.DescribeChannel(entries[4]));
    }

    [Fact]
    public void DescribeStaysEnglishWhateverTheInterfaceLanguageIs()
    {
        // A script reading -list must not break when the user switches the display language.
        string old = Strings.Override;
        try
        {
            Strings.Override = "es";
            Program.ChannelEntry entry = Layouts.Entries(Layouts.Two())[0];

            Assert.Equal("1\t1.1\tOnboard", Program.DescribeChannel(entry));
        }
        finally
        {
            Strings.Override = old;
        }
    }

    // ---- ResolveChannelEntry ---------------------------------------------------------------

    private static Program.ChannelEntry? Resolve(string text, List<Program.ChannelEntry> pool,
        out List<string> candidates) => Program.ResolveChannelEntry(text, pool, out candidates);

    [Theory]
    [InlineData("1", 1)]
    [InlineData("4", 4)]
    [InlineData("5", 5)]
    [InlineData("6", 6)]
    [InlineData(" 2 ", 2)]
    public void FlatNumberPicksThatChannel(string text, int flat)
    {
        List<Program.ChannelEntry> pool = Layouts.Entries(Layouts.Two());

        Program.ChannelEntry? found = Resolve(text, pool, out List<string> candidates);

        Assert.NotNull(found);
        Assert.Equal(flat, found.FlatNumber);
        Assert.Empty(candidates);
    }

    [Theory]
    [InlineData("1.1", 1, "dev-a", 0)]
    [InlineData("1.2", 2, "dev-a", 1)]
    [InlineData("1.4", 4, "dev-a", 3)]
    [InlineData("2.1", 5, "dev-b", 0)]
    [InlineData("2.2", 6, "dev-b", 1)]
    public void ControllerDotChannelPicksByPosition(string text, int flat, string key, int index)
    {
        List<Program.ChannelEntry> pool = Layouts.Entries(Layouts.Two());

        Program.ChannelEntry? found = Resolve(text, pool, out _);

        Assert.NotNull(found);
        Assert.Equal(flat, found.FlatNumber);
        Assert.Equal(key, found.DeviceKey);
        Assert.Equal(index, found.ChannelIndex);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("0.1")]
    [InlineData("1.0")]
    [InlineData("1.5")]
    [InlineData("3.1")]
    [InlineData("1.2.3")]
    [InlineData("1.")]
    [InlineData(".1")]
    [InlineData("a.b")]
    [InlineData("")]
    [InlineData("does-not-exist")]
    public void UnknownTargetsResolveToNothingAndListEveryCandidate(string text)
    {
        List<Program.ChannelEntry> pool = Layouts.Entries(Layouts.Two());

        Program.ChannelEntry? found = Resolve(text, pool, out List<string> candidates);

        Assert.Null(found);
        Assert.Equal(pool.Select(Program.DescribeChannel), candidates);
    }

    [Theory]
    [InlineData("Onboard", 1)]
    [InlineData("onboard", 1)]
    [InlineData("ONBOARD", 1)]
    [InlineData("  onboard ", 1)]
    [InlineData("ARGB 2", 3)]
    [InlineData("argb 2", 3)]
    [InlineData("argb2", 3)]
    [InlineData("ARGB-2", 3)]
    [InlineData("argb_2", 3)]
    [InlineData("ARGB 3", 4)]
    public void DefaultNamesMatchForgivingly(string text, int flat)
    {
        // Only the mainboard controller is in the pool, so every name here is unique.
        List<Program.ChannelEntry> pool = Layouts.Entries(new List<AuraDeviceSummary> { Layouts.Mainboard("dev-a") });

        Program.ChannelEntry? found = Resolve(text, pool, out _);

        Assert.NotNull(found);
        Assert.Equal(flat, found.FlatNumber);
    }

    [Fact]
    public void DefaultNameInAnotherInterfaceLanguageMatches()
    {
        List<Program.ChannelEntry> pool = Layouts.Entries(new List<AuraDeviceSummary> { Layouts.Mainboard("dev-a") });
        string[] spoken =
        {
            Strings.InLanguage("ChannelOnboard", "es"),
            Strings.InLanguage("ChannelOnboard", "ja"),
            Strings.InLanguage("ChannelOnboard", "tr"),
        };

        foreach (string name in spoken)
        {
            Assert.NotEqual("Onboard", name);
            Program.ChannelEntry? found = Resolve(name, pool, out _);

            Assert.NotNull(found);
            Assert.Equal(1, found.FlatNumber);
        }
    }

    [Fact]
    public void ADefaultNameThatTwoControllersShareIsAmbiguous()
    {
        List<Program.ChannelEntry> pool = Layouts.Entries(Layouts.Two());

        // ARGB 1 exists on both controllers (flat 2 and flat 5): no silent pick of the first.
        Program.ChannelEntry? found = Resolve("ARGB 1", pool, out List<string> candidates);

        Assert.Null(found);
        Assert.Equal(6, candidates.Count);
    }

    [Fact]
    public void NarrowingThePoolToOneControllerMakesTheNameUnique()
    {
        List<Program.ChannelEntry> all = Layouts.Entries(Layouts.Two());
        List<Program.ChannelEntry> second = all.FindAll(e => e.DeviceNumber == 2);

        Program.ChannelEntry? found = Resolve("ARGB 1", second, out _);

        Assert.NotNull(found);
        Assert.Equal("dev-b", found.DeviceKey);
        Assert.Equal(0, found.ChannelIndex);
    }

    [Fact]
    public void ACustomNameMatchesOnlyThatChannel()
    {
        List<Program.ChannelEntry> pool = Layouts.Entries(Layouts.Two());
        AuraChannelNames.Set("dev-b", 1, "My Header");

        foreach (string text in new[] { "My Header", "my header", "myheader", "MY-HEADER" })
        {
            Program.ChannelEntry? found = Resolve(text, pool, out _);

            Assert.NotNull(found);
            Assert.Equal("dev-b", found.DeviceKey);
            Assert.Equal(1, found.ChannelIndex);
        }
    }

    [Fact]
    public void ACustomNameDoesNotReplaceTheDefaultOne()
    {
        List<Program.ChannelEntry> pool = Layouts.Entries(new List<AuraDeviceSummary> { Layouts.Mainboard("dev-a") });
        AuraChannelNames.Set("dev-a", 2, "Fans");

        Program.ChannelEntry? byDefault = Resolve("ARGB 2", pool, out _);
        Program.ChannelEntry? byCustom = Resolve("fans", pool, out _);

        Assert.NotNull(byDefault);
        Assert.NotNull(byCustom);
        Assert.Equal(byDefault, byCustom);
    }

    [Fact]
    public void ACustomNameTwoChannelsShareIsAmbiguous()
    {
        List<Program.ChannelEntry> pool = Layouts.Entries(Layouts.Two());
        AuraChannelNames.Set("dev-a", 1, "Case");
        AuraChannelNames.Set("dev-b", 0, "case");

        Program.ChannelEntry? found = Resolve("CASE", pool, out List<string> candidates);

        Assert.Null(found);
        Assert.Equal(pool.Count, candidates.Count);
    }

    [Fact]
    public void ACustomNameThatEqualsAnotherChannelsDefaultIsAmbiguous()
    {
        List<Program.ChannelEntry> pool = Layouts.Entries(new List<AuraDeviceSummary> { Layouts.Mainboard("dev-a") });
        AuraChannelNames.Set("dev-a", 1, "ARGB 3");

        Program.ChannelEntry? found = Resolve("ARGB 3", pool, out _);

        Assert.Null(found);
    }

    [Fact]
    public void ANumberIsTakenAsTheFlatNumberBeforeAnyName()
    {
        List<Program.ChannelEntry> pool = Layouts.Entries(Layouts.Two());
        AuraChannelNames.Set("dev-a", 3, "2");

        Program.ChannelEntry? found = Resolve("2", pool, out _);

        Assert.NotNull(found);
        Assert.Equal(2, found.FlatNumber);
    }

    [Fact]
    public void ANumberOutsideTheListFallsBackToACustomNameThatIsANumber()
    {
        List<Program.ChannelEntry> pool = Layouts.Entries(new List<AuraDeviceSummary> { Layouts.Mainboard("dev-a") });
        AuraChannelNames.Set("dev-a", 3, "7");

        Program.ChannelEntry? found = Resolve("7", pool, out _);

        Assert.NotNull(found);
        Assert.Equal(4, found.FlatNumber);
    }

    [Fact]
    public void AnEmptyPoolResolvesToNothingWithoutCandidates()
    {
        Program.ChannelEntry? found = Resolve("1", new List<Program.ChannelEntry>(), out List<string> candidates);

        Assert.Null(found);
        Assert.Empty(candidates);
    }

    [Fact]
    public void ASingleChannelIsReachableByEveryForm()
    {
        List<Program.ChannelEntry> pool = Layouts.Entries(new List<AuraDeviceSummary>
        {
            new("only", "Only", new List<AuraChannel> { Layouts.Onboard() }),
        });

        foreach (string text in new[] { "1", "1.1", "Onboard", "onboard" })
        {
            Assert.NotNull(Resolve(text, pool, out _));
        }
    }

    [Fact]
    public void ChannelNameMatchesComparesTheNormalisedWantedName()
    {
        Program.ChannelEntry header = Layouts.Entries(new List<AuraDeviceSummary> { Layouts.Mainboard("dev-a") })[2];
        var names = new Dictionary<string, string>();

        Assert.True(Program.ChannelNameMatches(header, "argb2", names));
        Assert.False(Program.ChannelNameMatches(header, "argb3", names));
        Assert.False(Program.ChannelNameMatches(header, "ARGB 2", names));
        Assert.False(Program.ChannelNameMatches(header, "", names));
    }

    [Fact]
    public void ChannelNameMatchesUsesTheNamesItIsGiven()
    {
        Program.ChannelEntry header = Layouts.Entries(new List<AuraDeviceSummary> { Layouts.Mainboard("dev-a") })[2];
        AuraChannelNames.Set("dev-a", header.ChannelIndex, "Radiator");
        Dictionary<string, string> names = AuraChannelNames.All();

        Assert.True(Program.ChannelNameMatches(header, "radiator", names));
        Assert.False(Program.ChannelNameMatches(header, "radiator", new Dictionary<string, string>()));
    }

    // ---- TryResolveTarget ------------------------------------------------------------------

    private sealed record Resolved(bool Ok, string? DeviceKey, int Channel, int ExitCode, string Error);

    private static Resolved ResolveTarget(string? device, string? channel, List<AuraDeviceSummary> devices)
    {
        bool ok = false;
        string? key = null;
        int index = 0;
        int exit = 0;
        (_, string error) = Cli.Capture(() =>
            ok = Program.TryResolveTarget(device, channel, devices, out key, out index, out exit));
        return new Resolved(ok, key, index, exit, error);
    }

    [Fact]
    public void NoTargetMeansTheWholeBoard()
    {
        Resolved result = ResolveTarget(null, null, Layouts.Two());

        Assert.True(result.Ok);
        Assert.Null(result.DeviceKey);
        Assert.Equal(-1, result.Channel);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
    }

    [Theory]
    [InlineData("1", "dev-a")]
    [InlineData("2", "dev-b")]
    [InlineData(" 2", "dev-b")]
    public void ADeviceNumberPicksTheControllerAndAllItsChannels(string device, string key)
    {
        Resolved result = ResolveTarget(device, null, Layouts.Two());

        Assert.True(result.Ok);
        Assert.Equal(key, result.DeviceKey);
        Assert.Equal(-1, result.Channel);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.1")]
    [InlineData("dev-a")]
    public void AnUnknownDeviceExitsWithUsageCodeAndNoChannelLookup(string device)
    {
        Resolved result = ResolveTarget(device, "1", Layouts.Two());

        Assert.False(result.Ok);
        Assert.Equal(2, result.ExitCode);
        Assert.Equal(Strings.InLanguage("ErrorDeviceNotFound", "en"), result.Error.Trim());
    }

    [Fact]
    public void AChannelAloneIsSearchedOnEveryController()
    {
        Resolved byFlat = ResolveTarget(null, "5", Layouts.Two());
        Resolved dotted = ResolveTarget(null, "2.2", Layouts.Two());

        Assert.True(byFlat.Ok);
        Assert.Equal("dev-b", byFlat.DeviceKey);
        Assert.Equal(0, byFlat.Channel);
        Assert.True(dotted.Ok);
        Assert.Equal("dev-b", dotted.DeviceKey);
        Assert.Equal(1, dotted.Channel);
    }

    [Fact]
    public void TheChannelIndexIsReturnedNotThePosition()
    {
        // ARGB 2 on the addressable controller is its channel index 1, position 2.
        Resolved result = ResolveTarget("2", "ARGB 2", Layouts.Two());

        Assert.True(result.Ok);
        Assert.Equal("dev-b", result.DeviceKey);
        Assert.Equal(1, result.Channel);
    }

    [Fact]
    public void DeviceAndChannelNarrowTheSearchToThatController()
    {
        // "ARGB 1" is ambiguous board-wide but unique once the controller is named.
        Resolved board = ResolveTarget(null, "ARGB 1", Layouts.Two());
        Resolved first = ResolveTarget("1", "ARGB 1", Layouts.Two());
        Resolved second = ResolveTarget("2", "ARGB 1", Layouts.Two());

        Assert.False(board.Ok);
        Assert.True(first.Ok);
        Assert.Equal(("dev-a", 1), (first.DeviceKey, first.Channel));
        Assert.True(second.Ok);
        Assert.Equal(("dev-b", 0), (second.DeviceKey, second.Channel));
    }

    [Fact]
    public void AChannelOfAnotherControllerIsNotFoundWhenADeviceIsNamed()
    {
        Resolved result = ResolveTarget("1", "6", Layouts.Two());

        Assert.False(result.Ok);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void AnUnknownChannelPrintsTheHeadingAndEveryCandidateOnStderr()
    {
        List<AuraDeviceSummary> devices = Layouts.Two();
        Resolved result = ResolveTarget(null, "nope", devices);

        Assert.False(result.Ok);
        Assert.Equal(2, result.ExitCode);

        List<string> lines = Cli.Lines(result.Error);
        Assert.Equal(Strings.InLanguage("ErrorChannelNotFound", "en"), lines[0]);
        Assert.Equal(Layouts.Entries(devices).Select(Program.DescribeChannel), lines.Skip(1));
    }

    [Fact]
    public void WithADeviceNamedOnlyThatControllersChannelsAreListed()
    {
        Resolved result = ResolveTarget("2", "nope", Layouts.Two());

        List<string> lines = Cli.Lines(result.Error);
        Assert.False(result.Ok);
        Assert.Equal(3, lines.Count);
        Assert.StartsWith("5\t2.1", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("6\t2.2", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void AnAmbiguousChannelIsRefusedWithExitTwo()
    {
        Resolved result = ResolveTarget(null, "ARGB 2", Layouts.Two());

        Assert.False(result.Ok);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void ADeviceOnAnEmptyBoardIsUnknown()
    {
        Resolved result = ResolveTarget("1", null, new List<AuraDeviceSummary>());

        Assert.False(result.Ok);
        Assert.Equal(2, result.ExitCode);
    }

    // ---- ListErrorText ---------------------------------------------------------------------

    [Fact]
    public void ListErrorTextFollowsTheExitCode()
    {
        string old = Strings.Override;
        try
        {
            Strings.Override = "en";

            Assert.Equal(Strings.ErrorControllerBusy, Program.ListErrorText(4));
            Assert.Equal(Strings.ErrorWriteGeneric, Program.ListErrorText(5));
            Assert.Equal(Strings.ErrorControllerNotFound, Program.ListErrorText(3));

            // Anything else reads as "not found" rather than an empty or wrong message.
            Assert.Equal(Strings.ErrorControllerNotFound, Program.ListErrorText(0));
            Assert.Equal(Strings.ErrorControllerNotFound, Program.ListErrorText(2));
            Assert.Equal(Strings.ErrorControllerNotFound, Program.ListErrorText(-1));
        }
        finally
        {
            Strings.Override = old;
        }
    }

    [Fact]
    public void ListErrorTextGivesThreeDistinctMessages()
    {
        string old = Strings.Override;
        try
        {
            Strings.Override = "en";

            Assert.Equal(3, new[] { 3, 4, 5 }.Select(Program.ListErrorText).Distinct().Count());
        }
        finally
        {
            Strings.Override = old;
        }
    }

    // ---- FormatStatusLine ------------------------------------------------------------------

    [Fact]
    public void StatusLineHasLabelStateEffectColourAndBrightnessSeparatedByTabs()
    {
        string line = Program.FormatStatusLine("1\t1.1\tOnboard", true, 1, 0x20, 0xC0, 0xFF, "80");

        Assert.Equal("1\t1.1\tOnboard\ton\tstatic\t#20C0FF\tbrightness 80", line);
    }

    [Fact]
    public void StatusLineSaysOffWhenOff()
    {
        string line = Program.FormatStatusLine("Board", false, 2, 1, 2, 3, "100");

        Assert.Equal("Board\toff\tbreathing\t#010203\tbrightness 100", line);
    }

    [Fact]
    public void StatusLineHasNoColourForEffectsTheFirmwareColoursItself()
    {
        foreach (AuraPreset preset in AuraPresets.All.Where(p => !p.UsesColour))
        {
            string line = Program.FormatStatusLine("Board", true, preset.Mode, 255, 0, 0, "100");

            Assert.Equal($"Board\ton\t{preset.Key}\t-\tbrightness 100", line);
        }
    }

    [Fact]
    public void StatusLineShowsTheColourForEveryColourEffect()
    {
        foreach (AuraPreset preset in AuraPresets.All.Where(p => p.UsesColour))
        {
            string line = Program.FormatStatusLine("Board", true, preset.Mode, 0xAB, 0xCD, 0xEF, "50");

            Assert.Equal($"Board\ton\t{preset.Key}\t#ABCDEF\tbrightness 50", line);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(200)]
    public void StatusLineNamesAnUnknownModeAndShowsNoColour(int mode)
    {
        string line = Program.FormatStatusLine("Board", false, (byte)mode, 255, 255, 255, "-");

        Assert.Equal("Board\toff\tunknown\t-\tbrightness -", line);
    }

    // ---- StatusJson ------------------------------------------------------------------------

    private static JsonElement ParseStatus(AuraState state, Dictionary<string, ChannelLighting> remembered,
        List<AuraDeviceSummary> devices, out string raw)
    {
        raw = Program.StatusJson(state, remembered, Layouts.Entries(devices));
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    [Fact]
    public void StatusJsonIsOneLineOfValidJson()
    {
        var state = new AuraState(true, 1, 0x20, 0xC0, 0xFF, "", 80);

        _ = ParseStatus(state, new Dictionary<string, ChannelLighting>(), Layouts.Two(), out string raw);

        Assert.DoesNotContain('\n', raw);
        Assert.DoesNotContain('\r', raw);
        Assert.StartsWith("{", raw, StringComparison.Ordinal);
        Assert.EndsWith("}", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusJsonCarriesTheBoardFields()
    {
        var state = new AuraState(true, 1, 0x20, 0xC0, 0xFF, "Evening", 80);

        JsonElement root = ParseStatus(state, new Dictionary<string, ChannelLighting>(), Layouts.Two(), out _);

        Assert.True(root.GetProperty("on").GetBoolean());
        Assert.Equal("static", root.GetProperty("effect").GetString());
        Assert.Equal("#20C0FF", root.GetProperty("colour").GetString());
        Assert.Equal(80, root.GetProperty("brightness").GetInt32());
        Assert.Equal("Evening", root.GetProperty("customPreset").GetString());
        Assert.Equal(6, root.GetProperty("channels").GetArrayLength());
    }

    [Fact]
    public void StatusJsonNumbersChannelsLikeTheListAndNamesThemInEnglish()
    {
        var state = new AuraState(true, 1, 0, 0, 0, "", 100);
        string old = Strings.Override;
        try
        {
            Strings.Override = "es";

            JsonElement root = ParseStatus(state, new Dictionary<string, ChannelLighting>(), Layouts.Two(), out _);
            JsonElement[] channels = root.GetProperty("channels").EnumerateArray().ToArray();

            Assert.Equal(new[] { 1, 1, 1, 1, 2, 2 }, channels.Select(c => c.GetProperty("device").GetInt32()));
            Assert.Equal(new[] { 1, 2, 3, 4, 1, 2 }, channels.Select(c => c.GetProperty("channel").GetInt32()));
            Assert.Equal(new[] { "Onboard", "ARGB 1", "ARGB 2", "ARGB 3", "ARGB 1", "ARGB 2" },
                channels.Select(c => c.GetProperty("name").GetString()));
        }
        finally
        {
            Strings.Override = old;
        }
    }

    [Fact]
    public void AChannelWithoutARecordReportsTheBoardLookWithBrightnessZero()
    {
        var state = new AuraState(true, 2, 0x10, 0x20, 0x30, "", 70);

        JsonElement root = ParseStatus(state, new Dictionary<string, ChannelLighting>(), Layouts.Two(), out _);
        JsonElement first = root.GetProperty("channels")[0];

        Assert.Equal("breathing", first.GetProperty("effect").GetString());
        Assert.Equal("#102030", first.GetProperty("colour").GetString());
        Assert.Equal(0, first.GetProperty("brightness").GetInt32());
        Assert.True(first.GetProperty("on").GetBoolean());
    }

    [Fact]
    public void AChannelWithoutARecordIsOffWhenTheBoardIsOff()
    {
        var state = new AuraState(false, 1, 0, 0, 0, "", 100);

        JsonElement root = ParseStatus(state, new Dictionary<string, ChannelLighting>(), Layouts.Two(), out _);

        Assert.All(root.GetProperty("channels").EnumerateArray(), c => Assert.False(c.GetProperty("on").GetBoolean()));
    }

    [Fact]
    public void AChannelWithARecordReportsItsOwnLook()
    {
        var state = new AuraState(true, 1, 255, 255, 255, "", 100);
        var remembered = new Dictionary<string, ChannelLighting>();
        Dictionary<string, ChannelLighting> Remember(string key, int channel, ChannelLighting look)
        {
            AuraChannelStates.Remember(new[] { (key, channel) }, look, new[] { key });
            return AuraChannelStates.All();
        }

        remembered = Remember("dev-b", 1, new ChannelLighting(3, 0xAA, 0xBB, 0xCC, On: false, Brightness: 40));

        JsonElement root = ParseStatus(state, remembered, Layouts.Two(), out _);
        JsonElement own = root.GetProperty("channels")[5];
        JsonElement other = root.GetProperty("channels")[4];

        Assert.Equal("flashing", own.GetProperty("effect").GetString());
        Assert.Equal("#AABBCC", own.GetProperty("colour").GetString());
        Assert.Equal(40, own.GetProperty("brightness").GetInt32());
        Assert.False(own.GetProperty("on").GetBoolean());
        Assert.Equal("static", other.GetProperty("effect").GetString());
        Assert.True(other.GetProperty("on").GetBoolean());
    }

    [Fact]
    public void AnUnknownModeIsReportedAsUnknown()
    {
        var state = new AuraState(true, 42, 1, 2, 3, "", 100);

        JsonElement root = ParseStatus(state, new Dictionary<string, ChannelLighting>(), Layouts.Two(), out _);

        Assert.Equal("unknown", root.GetProperty("effect").GetString());
        Assert.Equal("unknown", root.GetProperty("channels")[0].GetProperty("effect").GetString());
    }

    [Fact]
    public void StatusJsonOfNoChannelsHasAnEmptyArray()
    {
        var state = new AuraState(true, 1, 0, 0, 0, "", 100);

        JsonElement root = ParseStatus(state, new Dictionary<string, ChannelLighting>(), new List<AuraDeviceSummary>(),
            out _);

        Assert.Equal(0, root.GetProperty("channels").GetArrayLength());
    }

    [Fact]
    public void StatusJsonNamesEveryEffectByItsKey()
    {
        foreach (AuraPreset preset in AuraPresets.All)
        {
            var state = new AuraState(true, preset.Mode, 0, 0, 0, "", 100);

            JsonElement root = ParseStatus(state, new Dictionary<string, ChannelLighting>(), Layouts.Two(), out _);

            Assert.Equal(preset.Key, root.GetProperty("effect").GetString());
        }
    }

    [Fact]
    public void StatusJsonEscapesAPresetNameThatNeedsIt()
    {
        var state = new AuraState(true, 1, 0, 0, 0, "Quote \" and \\ slash", 100);

        JsonElement root = ParseStatus(state, new Dictionary<string, ChannelLighting>(), Layouts.Two(), out _);

        Assert.Equal("Quote \" and \\ slash", root.GetProperty("customPreset").GetString());
    }
}
