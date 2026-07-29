using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AuraToggle;

internal static class Program
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // colour mode support is still marked experimental
            Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001
            Application.Run(new ToggleForm());
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

        if (args.Length == 2 && command == "preset")
        {
            AuraPreset? preset = AuraPresets.Find(args[1]);
            if (preset == null)
            {
                return Usage();
            }

            ApplyPreset(preset);
            return 0;
        }

        return Usage();
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
    public static AuraState ApplyPreset(AuraPreset preset)
    {
        AuraState state = AuraState.Load() with { On = true, Mode = preset.Mode };
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
