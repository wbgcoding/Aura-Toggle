using System;
using System.Collections.Generic;
using System.IO;

namespace AuraToggle.Tests;

/// <summary>What a command line run printed and returned.</summary>
internal sealed record CliResult(int ExitCode, string Out, string Error);

/// <summary>
/// Runs the command line the way <c>Main</c> does for a real argument list, without the
/// process-wide handlers and without the window path, and captures what it writes to the console.
/// </summary>
internal static class Cli
{
    /// <summary>Runs <see cref="Program.Run"/> with the interface forced to English.</summary>
    public static CliResult Run(params string[] args)
    {
        int exitCode = 0;
        (string output, string error) = Capture(() =>
        {
            try
            {
                exitCode = Program.Run(args);
            }
            catch (AuraNotFoundException ex)
            {
                // Same mapping Main applies: the message to stderr, the exception's own exit code.
                Console.Error.WriteLine(ex.Message);
                exitCode = ex.ExitCode;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine(ex.Message);
                exitCode = 5;
            }
        });

        return new CliResult(exitCode, output, error);
    }

    /// <summary>Runs an action with stdout and stderr redirected and the interface in English.</summary>
    public static (string Out, string Error) Capture(Action action)
    {
        TextWriter oldOut = Console.Out;
        TextWriter oldError = Console.Error;
        string oldLanguage = Strings.Override;
        var output = new StringWriter();
        var error = new StringWriter();

        Console.SetOut(output);
        Console.SetError(error);
        Strings.Override = "en";

        try
        {
            action();
        }
        finally
        {
            Strings.Override = oldLanguage;
            Console.SetOut(oldOut);
            Console.SetError(oldError);
        }

        return (output.ToString(), error.ToString());
    }

    /// <summary>The non-empty lines of a captured stream.</summary>
    public static List<string> Lines(string text) =>
        new(text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Hand-built controller lists, the shapes <c>-list</c> numbers.</summary>
internal static class Layouts
{
    public static AuraChannel Onboard(int index = 0) => new(index, true, 0);

    public static AuraChannel Header(int index, int header) => new(index, false, header);

    /// <summary>A mainboard controller: the onboard zone as channel 0 and ARGB headers 1..n.</summary>
    public static AuraDeviceSummary Mainboard(string key, int headers = 3, string name = "Mainboard")
    {
        var channels = new List<AuraChannel> { Onboard() };
        for (int h = 1; h <= headers; h++)
        {
            channels.Add(Header(h, h));
        }

        return new AuraDeviceSummary(key, name, channels);
    }

    /// <summary>An addressable controller: no fixed zone, so header 1 is channel 0.</summary>
    public static AuraDeviceSummary Addressable(string key, int headers = 2, string name = "Addressable")
    {
        var channels = new List<AuraChannel>();
        for (int h = 1; h <= headers; h++)
        {
            channels.Add(Header(h - 1, h));
        }

        return new AuraDeviceSummary(key, name, channels);
    }

    /// <summary>Mainboard (4 channels: flat 1-4) and addressable (2 channels: flat 5-6).</summary>
    public static List<AuraDeviceSummary> Two() => new() { Mainboard("dev-a"), Addressable("dev-b") };

    public static List<Program.ChannelEntry> Entries(List<AuraDeviceSummary> devices) =>
        Program.FlattenChannels(devices);
}
