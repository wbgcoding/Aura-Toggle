using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AuraToggle;

internal static class Program
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr Broadcast = new(0xFFFF);

    /// <summary>Asks an already running window to come back from the notification area.</summary>
    public static readonly uint ShowWindowMessage = RegisterWindowMessage("AuraToggle.Show");

    [STAThread]
    private static int Main(string[] args)
    {
        // The language choice also governs usage and error output on the command line.
        Strings.Override = AuraSettings.Load().Language;

        if (args.Length == 0)
        {
            // A second start hands over to the instance already running, which may be sitting
            // in the notification area with no window to click.
            using var single = new Mutex(initiallyOwned: true, "AuraToggle.SingleInstance", out bool first);
            if (!first)
            {
                PostMessage(Broadcast, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
                return 0;
            }

            ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // colour mode support is still marked experimental
            Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001
            Application.Run(new ToggleForm());
            GC.KeepAlive(single);
            return 0;
        }

        try
        {
            return Run(args);
        }
        catch (AuraNotFoundException ex)
        {
            WriteError(ex.Message);
            return ex.ExitCode;
        }
        catch (IOException ex)
        {
            WriteError(ex.Message);
            return 5;
        }
    }

    private static int Run(string[] args)
    {
        string command = Normalise(args[0]);

        if (args.Length == 1 && command is "on" or "off")
        {
            Switch(command == "on");
            return 0;
        }

        if (command == "preset" && args.Length is 2 or 3)
        {
            AuraPreset? preset = AuraPresets.Find(args[1]);
            if (preset == null)
            {
                return Usage();
            }

            Color? colour = null;
            if (args.Length == 3)
            {
                if (!TryParseColour(args[2], out Color parsed))
                {
                    return Usage();
                }

                colour = parsed;
            }

            ApplyPreset(preset, colour);
            return 0;
        }

        return Usage();
    }

    /// <summary>Accepts #RRGGBB, RRGGBB and the common colour names.</summary>
    private static bool TryParseColour(string value, out Color colour)
    {
        string text = value.Trim().TrimStart('#');

        if (text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            colour = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }

        colour = Color.FromName(value.Trim());
        return colour.IsKnownColor;
    }

    private static int Usage()
    {
        WriteError(Strings.UsageLine);
        WriteError(string.Format(CultureInfo.CurrentCulture, Strings.UsagePresets, AuraPresets.Names));
        return 2;
    }

    /// <summary>Accepts -on, --on, /on and on, in any casing.</summary>
    private static string Normalise(string argument) => argument.TrimStart('-', '/').ToLowerInvariant();

    /// <summary>
    /// Applies the stored effect or switches every channel off. Nothing is written to the
    /// controller flash, so a reboot always brings the mainboard lighting back.
    /// </summary>
    public static AuraState Switch(bool on)
    {
        AuraState state = AuraState.Load();
        Send(on ? state.Mode : AuraState.ModeOff, state);

        AuraState next = state with { On = on };
        next.Save();
        return next;
    }

    /// <summary>Switches to a lighting effect and remembers it as the state to restore.</summary>
    public static AuraState ApplyPreset(AuraPreset preset, Color? colour = null)
    {
        AuraState state = AuraState.Load() with { On = true, Mode = preset.Mode };

        if (colour is Color chosen)
        {
            state = state with { Red = chosen.R, Green = chosen.G, Blue = chosen.B };
        }

        Send(preset.Mode, state);
        state.Save();
        return state;
    }

    private static void Send(byte mode, AuraState state)
    {
        var devices = AuraDevice.DiscoverAll();

        try
        {
            foreach (AuraDevice device in devices)
            {
                device.Apply(mode, state.Red, state.Green, state.Blue);
            }
        }
        finally
        {
            foreach (AuraDevice device in devices)
            {
                device.Dispose();
            }
        }
    }

    /// <summary>Reports to the console of the calling shell - this is a WinExe and owns none.</summary>
    private static void WriteError(string message)
    {
        AttachConsole(AttachParentProcess);
        Console.Error.WriteLine(message);
    }
}
