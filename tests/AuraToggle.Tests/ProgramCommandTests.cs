using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace AuraToggle.Tests;

/// <summary>
/// Whole command lines through <c>Program.Run</c> that end before any hardware is touched, and the
/// ones that need a controller (there is none in the test folder, so they end in exit 3).
/// </summary>
public sealed class ProgramCommandTests : DataFolderTest
{
    private const int Usage = 2;
    private const int NoController = 3;

    /// <summary>Every stored file as name and content, to prove a failed command left the folder alone.</summary>
    private static Dictionary<string, string> Snapshot() =>
        Directory.GetFiles(TestData.Folder, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(TestData.Folder, path), File.ReadAllText);

    private static void SeedStoredFiles()
    {
        AuraState state = new(On: true, Mode: 2, Red: 10, Green: 20, Blue: 30, CustomPreset: "", Brightness: 55);
        state.Save();
        AuraChannelStates.Remember(new[] { ("dev-a", 1) }, new ChannelLighting(3, 1, 2, 3, On: true, Brightness: 60),
            new[] { "dev-a" });
        AuraCustomPresets.Save(new List<CustomPreset>
        {
            new("Evening", new List<CustomPresetEntry> { new("dev-a", 0, "Onboard", 1, 255, 0, 0) }),
        });
    }

    // ---- No arguments and unknown commands -------------------------------------------------

    [Fact]
    public void NoArgumentsPrintsUsageAndExitsTwo()
    {
        CliResult result = Cli.Run();

        Assert.Equal(Usage, result.ExitCode);
        Assert.Equal("", result.Out);
        Assert.StartsWith("Usage: AuraToggle", result.Error, StringComparison.Ordinal);
        Assert.Contains("Effects: ", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UsageNamesEveryEffectTheCommandLineAccepts()
    {
        CliResult result = Cli.Run();

        foreach (AuraPreset preset in AuraPresets.All)
        {
            Assert.Contains(preset.Key, result.Error, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("-bogus")]
    [InlineData("bogus")]
    [InlineData("--")]
    [InlineData("/")]
    [InlineData("")]
    [InlineData("-onn")]
    [InlineData("-preset-static")]
    public void AnUnknownCommandIsUsage(string argument)
    {
        CliResult result = Cli.Run(argument);

        Assert.Equal(Usage, result.ExitCode);
        Assert.Equal("", result.Out);
        Assert.NotEqual("", result.Error);
    }

    [Theory]
    [InlineData("-on", "extra")]
    [InlineData("-off", "now")]
    [InlineData("-toggle", "1")]
    [InlineData("-on", "-off")]
    public void SurplusWordsAfterOnOffOrToggleAreUsage(string first, string second)
    {
        Assert.Equal(Usage, Cli.Run(first, second).ExitCode);
    }

    [Fact]
    public void TargetingAloneLeavesNoCommandWord()
    {
        // Targeting is pulled out first, so what is left decides: nothing left is usage.
        Assert.Equal(Usage, Cli.Run("-device", "1").ExitCode);
        Assert.Equal(Usage, Cli.Run("-channel", "x", "-device", "1").ExitCode);
    }

    [Fact]
    public void OnlyTargetingAndNoCommandIsUsage()
    {
        Assert.Equal(Usage, Cli.Run("-channel", "1").ExitCode);
        Assert.Equal(Usage, Cli.Run("-device", "1", "-channel", "1").ExitCode);
    }

    // ---- Help and version ------------------------------------------------------------------

    [Theory]
    [InlineData("-help")]
    [InlineData("--help")]
    [InlineData("/help")]
    [InlineData("help")]
    [InlineData("-h")]
    [InlineData("/h")]
    [InlineData("-?")]
    [InlineData("/?")]
    [InlineData("-HELP")]
    public void HelpInEverySpellingExitsZeroAndPrintsToStdout(string argument)
    {
        CliResult result = Cli.Run(argument);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Contains("Commands:", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpTextIsEnglishAndTheSameWhateverTheInterfaceLanguageIs()
    {
        string old = Strings.Override;
        TextWriter oldOut = Console.Out;
        try
        {
            var results = new List<string>();
            foreach (string language in new[] { "en", "de", "ja" })
            {
                Strings.Override = language;
                var writer = new StringWriter();
                Console.SetOut(writer);
                Assert.Equal(0, Program.PrintHelp());
                results.Add(writer.ToString());
            }

            Assert.Equal(results[0], results[1]);
            Assert.Equal(results[0], results[2]);
        }
        finally
        {
            Console.SetOut(oldOut);
            Strings.Override = old;
        }
    }

    [Fact]
    public void HelpMentionsEveryCommandOptionExitCodeAndEffect()
    {
        string help = Cli.Run("-help").Out;

        foreach (string word in new[]
                 {
                     "-on", "-off", "-toggle", "-preset", "-brightness", "-custom", "-list", "-status", "--version",
                     "-help", "-device", "-channel", "#RRGGBB",
                 })
        {
            Assert.Contains(word, help, StringComparison.Ordinal);
        }

        Assert.Contains("0 success, 2 bad arguments, 3 no controller found", help, StringComparison.Ordinal);
        Assert.Contains($"-brightness <{AuraState.MinBrightness}-{AuraState.MaxBrightness}>", help,
            StringComparison.Ordinal);
        Assert.Contains($"Effects: {AuraPresets.Names}", help, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpFirstLineCarriesTheVersion()
    {
        string firstLine = Cli.Lines(Cli.Run("-help").Out)[0];

        Assert.Equal($"Aura Toggle {Program.VersionText}", firstLine);
    }

    [Fact]
    public void HelpWithAnyFurtherWordIsUsage()
    {
        Assert.Equal(Usage, Cli.Run("-help", "me").ExitCode);
        Assert.Equal(Usage, Cli.Run("-?", "-on").ExitCode);
    }

    [Theory]
    [InlineData("-version")]
    [InlineData("--version")]
    [InlineData("/version")]
    [InlineData("version")]
    [InlineData("--VERSION")]
    public void VersionPrintsOnlyTheVersionNumber(string argument)
    {
        CliResult result = Cli.Run(argument);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Equal(Program.VersionText, result.Out.TrimEnd('\r', '\n'));
        Assert.Matches(@"^\d+\.\d+\.\d+", Program.VersionText);
        Assert.DoesNotContain('+', Program.VersionText);
    }

    [Fact]
    public void VersionWithAnyFurtherWordIsUsage()
    {
        Assert.Equal(Usage, Cli.Run("--version", "x").ExitCode);
    }

    [Theory]
    [InlineData("-version")]
    [InlineData("-list")]
    [InlineData("-status")]
    [InlineData("-custom")]
    [InlineData("-help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void CommandsWithNothingToTargetRefuseDeviceAndChannel(string command)
    {
        // Silently dropping the option would read as "it worked" when it did nothing.
        Assert.Equal(Usage, Cli.Run(command, "-device", "1").ExitCode);
        Assert.Equal(Usage, Cli.Run(command, "-channel", "1").ExitCode);
        Assert.Equal(Usage, Cli.Run("-device", "1", command).ExitCode);
    }

    [Fact]
    public void ARefusedTargetingPrintsNoOutputButTheUsageOnStderr()
    {
        CliResult result = Cli.Run("-list", "-device", "1");

        Assert.Equal("", result.Out);
        Assert.StartsWith("Usage: AuraToggle", result.Error, StringComparison.Ordinal);
    }

    // ---- Presets and colours (rejected before any controller is looked at) -----------------

    [Theory]
    [InlineData("-preset")]
    [InlineData("-preset", "nosuchEffect")]
    [InlineData("-preset", "static", "notacolour")]
    [InlineData("-preset", "static", "#12345")]
    [InlineData("-preset", "static", "#GG0000")]
    [InlineData("-preset", "static", "transparent")]
    [InlineData("-preset", "static", "Window")]
    [InlineData("-preset", "static", "red", "extra")]
    [InlineData("-preset", "nosuchEffect", "red")]
    public void BadPresetCommandLinesAreUsageWithoutLookingForAController(params string[] args)
    {
        CliResult result = Cli.Run(args);

        Assert.Equal(Usage, result.ExitCode);
        Assert.Equal("", result.Out);
    }

    [Fact]
    public void AValidPresetNeedsAControllerAndSaysSo()
    {
        CliResult result = Cli.Run("-preset", "static", "#FF0000");

        Assert.Equal(NoController, result.ExitCode);
        Assert.Contains(Strings.InLanguage("ErrorControllerNotFound", "en"), result.Error, StringComparison.Ordinal);
    }

    // ---- Brightness ------------------------------------------------------------------------

    [Theory]
    [InlineData("101")]
    [InlineData("255")]
    [InlineData("256")]
    [InlineData("1000")]
    [InlineData("-1")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("50%")]
    [InlineData("5.5")]
    [InlineData("1e2")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("99999999999999999999")]
    public void ABrightnessThatIsNotAWholeNumberUpToTheCeilingIsUsage(string value)
    {
        CliResult result = Cli.Run("-brightness", value);

        Assert.Equal(Usage, result.ExitCode);
        Assert.Equal("", result.Out);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("9")]
    [InlineData("10")]
    [InlineData("50")]
    [InlineData("99")]
    [InlineData("100")]
    [InlineData("007")]
    [InlineData(" 40 ")]
    [InlineData("+40")]
    public void AValidBrightnessGetsAsFarAsTheController(string value)
    {
        // Below the floor is clamped, not rejected: all of these pass the argument check.
        Assert.Equal(NoController, Cli.Run("-brightness", value).ExitCode);
    }

    [Fact]
    public void BrightnessNeedsExactlyOneValue()
    {
        Assert.Equal(Usage, Cli.Run("-brightness").ExitCode);
        Assert.Equal(Usage, Cli.Run("-brightness", "50", "60").ExitCode);
    }

    // ---- Custom preset lookup --------------------------------------------------------------

    [Fact]
    public void CustomWithoutANameIsUsage()
    {
        Assert.Equal(Usage, Cli.Run("-custom").ExitCode);
        Assert.Equal(Usage, Cli.Run("-custom", "a", "b").ExitCode);
    }

    [Fact]
    public void CustomWithNoSavedPresetsIsUsage()
    {
        Assert.Equal(Usage, Cli.Run("-custom", "Evening").ExitCode);
        Assert.Equal(Usage, Cli.Run("-custom", "1").ExitCode);
    }

    [Theory]
    [InlineData("Evening")]
    [InlineData("evening")]
    [InlineData("EVENING")]
    [InlineData("  evening ")]
    [InlineData("ev-ening")]
    [InlineData("1")]
    public void CustomFindsAPresetByNameForgivingSpellingOrNumber(string name)
    {
        // Found means it went on to the controller (exit 3 here); not found would be exit 2.
        SeedStoredFiles();

        Assert.Equal(NoController, Cli.Run("-custom", name).ExitCode);
    }

    [Theory]
    [InlineData("Morning")]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("")]
    public void CustomWithAnUnknownNameOrNumberIsUsage(string name)
    {
        SeedStoredFiles();

        Assert.Equal(Usage, Cli.Run("-custom", name).ExitCode);
    }

    [Fact]
    public void ANameBeatsANumberWhenAPresetIsNamedLikeOne()
    {
        // Preset 1 is called "2" and preset 2 is called "Other": "-custom 2" must pick by name,
        // so it finds a preset either way, but "-custom 3" has neither a name nor a number.
        AuraCustomPresets.Save(new List<CustomPreset>
        {
            new("2", new List<CustomPresetEntry> { new("dev-a", 0, "Onboard", 1, 1, 2, 3) }),
            new("Other", new List<CustomPresetEntry> { new("dev-a", 0, "Onboard", 1, 1, 2, 3) }),
        });

        Assert.Equal(NoController, Cli.Run("-custom", "2").ExitCode);
        Assert.Equal(Usage, Cli.Run("-custom", "3").ExitCode);
    }

    // ---- Commands that need a controller ---------------------------------------------------

    [Theory]
    [InlineData("-on")]
    [InlineData("-off")]
    [InlineData("-toggle")]
    [InlineData("--ON")]
    [InlineData("/off")]
    public void SwitchCommandsWithoutAControllerExitThree(string command)
    {
        CliResult result = Cli.Run(command);

        Assert.Equal(NoController, result.ExitCode);
        Assert.Equal("", result.Out);
        Assert.Contains(Strings.InLanguage("ErrorControllerNotFound", "en"), result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-list")]
    [InlineData("-status")]
    [InlineData("-status", "--json")]
    [InlineData("-status", "json")]
    [InlineData("-status", "-JSON")]
    public void ListAndStatusWithoutAControllerExitThreeAndPrintNothingOnStdout(params string[] args)
    {
        CliResult result = Cli.Run(args);

        Assert.Equal(NoController, result.ExitCode);
        Assert.Equal("", result.Out);
        Assert.Contains(Strings.InLanguage("ErrorControllerNotFound", "en"), result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-status", "xml")]
    [InlineData("-status", "--json", "x")]
    [InlineData("-list", "now")]
    public void ListAndStatusWithTheWrongExtraWordAreUsage(params string[] args)
    {
        Assert.Equal(Usage, Cli.Run(args).ExitCode);
    }

    [Theory]
    [InlineData("-on")]
    [InlineData("-off")]
    [InlineData("-toggle")]
    [InlineData("-preset", "static", "red")]
    [InlineData("-brightness", "30")]
    public void ATargetedCommandWithoutAControllerExitsThreeBeforeItChecksTheTarget(params string[] command)
    {
        // The target is looked up on the discovered controllers, and there are none.
        string[] withDevice = command.Concat(new[] { "-device", "1" }).ToArray();
        string[] withChannel = command.Concat(new[] { "-channel", "1" }).ToArray();
        string[] withBoth = command.Concat(new[] { "-device", "1", "-channel", "1" }).ToArray();

        Assert.Equal(NoController, Cli.Run(withDevice).ExitCode);
        Assert.Equal(NoController, Cli.Run(withChannel).ExitCode);
        Assert.Equal(NoController, Cli.Run(withBoth).ExitCode);
    }

    [Fact]
    public void TargetingMayPrecedeTheCommand()
    {
        Assert.Equal(NoController, Cli.Run("-device", "1", "-channel", "2", "-off").ExitCode);
        Assert.Equal(NoController, Cli.Run("-channel", "2", "-brightness", "40").ExitCode);
    }

    [Fact]
    public void ATargetedBadCommandWordIsStillUsageOnceTheControllersAreFound()
    {
        // No controller means exit 3 first; the point is that a bad word never turns into a switch.
        Assert.Equal(NoController, Cli.Run("-bogus", "-device", "1").ExitCode);
    }

    // ---- Stored files stay untouched when no controller answers ----------------------------

    public static IEnumerable<object[]> NeedsAController() => new[]
    {
        new object[] { new[] { "-on" } },
        new object[] { new[] { "-off" } },
        new object[] { new[] { "-toggle" } },
        new object[] { new[] { "-preset", "static", "#FF0000" } },
        new object[] { new[] { "-preset", "rainbow" } },
        new object[] { new[] { "-brightness", "20" } },
        new object[] { new[] { "-brightness", "0" } },
        new object[] { new[] { "-custom", "Evening" } },
        new object[] { new[] { "-on", "-device", "1" } },
        new object[] { new[] { "-off", "-channel", "1" } },
        new object[] { new[] { "-preset", "static", "red", "-device", "1", "-channel", "1" } },
        new object[] { new[] { "-brightness", "70", "-device", "1" } },
        new object[] { new[] { "-list" } },
        new object[] { new[] { "-status" } },
        new object[] { new[] { "-status", "--json" } },
    };

    [Theory]
    [MemberData(nameof(NeedsAController))]
    public void ACommandThatEndsInExitThreeLeavesEveryStoredFileUnchanged(string[] args)
    {
        SeedStoredFiles();
        Dictionary<string, string> before = Snapshot();
        Assert.Contains(AuraState.FileName, before.Keys);

        CliResult result = Cli.Run(args);

        Assert.Equal(NoController, result.ExitCode);
        Assert.Equal(before, Snapshot());
    }

    // With no saved preset a -custom name is a usage error (covered above), so it is not in this set.
    public static IEnumerable<object[]> NeedsAControllerWithoutPresets() =>
        NeedsAController().Where(row => ((string[])row[0])[0] != "-custom");

    [Theory]
    [MemberData(nameof(NeedsAControllerWithoutPresets))]
    public void ACommandThatEndsInExitThreeCreatesNoStoredDataInAnEmptyFolder(string[] args)
    {
        CliResult result = Cli.Run(args);

        Assert.Equal(NoController, result.ExitCode);
        Assert.DoesNotContain(Snapshot().Keys, name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("-bogus")]
    [InlineData("-brightness", "500")]
    [InlineData("-preset", "static", "nope")]
    [InlineData("-custom", "Nothing")]
    public void AUsageErrorLeavesEveryStoredFileUnchanged(params string[] args)
    {
        SeedStoredFiles();
        Dictionary<string, string> before = Snapshot();

        CliResult result = Cli.Run(args);

        Assert.Equal(Usage, result.ExitCode);
        Assert.Equal(before, Snapshot());
    }

    // ---- Exit code mapping that Main applies -----------------------------------------------

    [Fact]
    public void ANotFoundExceptionCarriesItsOwnExitCodeAndMessage()
    {
        var exception = new AuraNotFoundException("gone", 4);

        Assert.Equal(4, exception.ExitCode);
        Assert.Equal("gone", exception.Message);
        Assert.True(exception.WorthRetrying);
        Assert.False(new AuraNotFoundException("x", 3, worthRetrying: false).WorthRetrying);
    }
}
