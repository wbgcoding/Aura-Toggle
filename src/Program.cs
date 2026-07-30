using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
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

    /// <summary>True when Windows started the tool at logon rather than the user.</summary>
    public static bool LaunchedAtStartup { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        // The language choice also governs usage and error output on the command line.
        Strings.Override = AuraSettings.Load().Language;

        LaunchedAtStartup = args.Length == 1 && args[0] == AuraSettings.AutoStartArgument;

        if (args.Length == 0 || LaunchedAtStartup)
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

        if (command == "brightness" && args.Length == 2)
        {
            if (!byte.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte percent) ||
                percent > AuraState.MaxBrightness)
            {
                return Usage();
            }

            SetBrightness(Math.Max(percent, AuraState.MinBrightness));
            return 0;
        }

        return Usage();
    }

    /// <summary>Accepts #RRGGBB, RRGGBB and the common colour names.</summary>
    private static bool TryParseColour(string value, out Color colour)
    {
        string text = value.Trim().TrimStart('#');

        // Every character has to be a hex digit: NumberStyles.HexNumber also allows surrounding
        // whitespace, so " 12345" would otherwise pass the length check and parse as 0x12345.
        if (text.Length == 6 && text.All(Uri.IsHexDigit) &&
            int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
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
    /// <param name="deviceKey">
    /// Limits the switch to one controller, matched by <see cref="AuraDevice.Key"/>. Null or
    /// empty means every controller - the default, and the only option on the command line.
    /// A remembered custom preset always targets every controller it names, so it is only
    /// re-applied when switching every device on at once.
    /// </param>
    /// <param name="channel">One channel of that controller, or -1 for all of them.</param>
    public static AuraState Switch(bool on, string? deviceKey = null, int channel = -1)
    {
        AuraState state = AuraState.Load();

        if (on && string.IsNullOrEmpty(deviceKey) && state.CustomPreset.Length > 0)
        {
            CustomPreset? preset = AuraCustomPresets.Load().Find(p => p.Name == state.CustomPreset);
            if (preset != null)
            {
                return ApplyCustomPreset(preset);
            }
        }

        // The board counts as on while any single channel still is, so switching one header off
        // does not make the tray claim the whole board went dark.
        bool anyOn = on
            // Each channel comes back to its own last look, not to one colour smeared across the
            // board - and without rewriting what is remembered, which a plain Send would do.
            ? Restore(state, deviceKey, channel)
            : Send(AuraState.ModeOff, state, deviceKey, channel);

        AuraState next = state with { On = anyOn };
        next.Save();
        return next;
    }

    /// <summary>
    /// Puts every channel back to the effect and colour it was last set to, falling back to the
    /// board-wide state for channels that have never been set on their own. Reads the record
    /// rather than writing it, so switching off and on again cannot flatten it.
    /// </summary>
    /// <returns>Whether any channel on the machine is on afterwards.</returns>
    private static bool Restore(AuraState state, string? deviceKey, int channel)
    {
        var devices = AuraDevice.DiscoverAll();

        try
        {
            List<(string, int)> switched = ApplyMix(devices, deviceKey, channel, state, look: null);
            AuraChannelStates.SetPower(switched, on: true, BoardWide(state));

            // Something was just switched on, so the board is lit without having to re-read.
            return switched.Count > 0;
        }
        finally
        {
            Close(devices);
        }
    }

    /// <summary>The lighting the board as a whole is set to, for channels that have no own record.</summary>
    private static ChannelLighting BoardWide(AuraState state) =>
        new(state.Mode, state.Red, state.Green, state.Blue);

    /// <summary>A channel's own brightness, or the board-wide one while it has none of its own.</summary>
    private static byte Brightness(ChannelLighting? look, AuraState state) =>
        look is { Brightness: not 0 } own ? own.Brightness : state.Brightness;

    /// <summary>
    /// Writes a controller's channels in one go: the channels the caller named get
    /// <paramref name="look"/> - or their own record, when it is null - and every other channel
    /// of the same controller is written again exactly as it already stands.
    /// </summary>
    /// <remarks>
    /// Re-asserting the untouched channels is what makes a mix survive. The controller applies a
    /// new effect across its channels unless every one of them is named in the same burst, which
    /// is why a static header next to an animated one used to end up running the same effect as
    /// its neighbour. Channels that have never been set on their own are left alone, since there
    /// is nothing to re-assert and guessing would overwrite the BIOS lighting.
    /// </remarks>
    /// <returns>The channels that were named, for the caller to record.</returns>
    private static List<(string DeviceKey, int Channel)> ApplyMix(List<AuraDevice> devices, string? deviceKey,
        int channel, AuraState state, ChannelLighting? look)
    {
        var targets = string.IsNullOrEmpty(deviceKey)
            ? devices
            : devices.FindAll(device => device.Key == deviceKey);

        if (targets.Count == 0)
        {
            // The selected controller was unplugged since the window last saw it.
            throw new AuraNotFoundException(Strings.ErrorControllerNotFound, 3);
        }

        // Read once, then indexed per channel: this used to re-read and re-parse the whole file
        // for every single channel.
        Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();
        var named = new List<(string, int)>();

        foreach (AuraDevice device in targets)
        {
            foreach (AuraChannel affected in device.Channels)
            {
                ChannelLighting? own = AuraChannelStates.Get(remembered, device.Key, affected.Index);
                bool chosen = channel < 0 || affected.Index == channel;

                ChannelLighting? apply = chosen ? look ?? own ?? BoardWide(state) : own;
                if (apply == null)
                {
                    continue;
                }

                // A new effect or colour carries no brightness, so the one this channel was given
                // keeps applying - the same rule the stored record follows.
                (byte red, byte green, byte blue) = AuraState.Dim(apply.Red, apply.Green, apply.Blue,
                    Brightness(apply.Brightness != 0 ? apply : own, state));

                // A named channel takes the mode it was given, even when its record says it was
                // off - being named is what switches it back on. An untouched one keeps its own
                // state, off included.
                device.Apply(chosen || apply.On ? apply.Mode : AuraState.ModeOff, red, green, blue, affected.Index);

                if (chosen)
                {
                    named.Add((device.Key, affected.Index));
                }
            }
        }

        return named;
    }

    private static void Close(List<AuraDevice> devices)
    {
        foreach (AuraDevice device in devices)
        {
            device.Dispose();
        }
    }

    /// <summary>
    /// Whether at least one channel of the controllers already open is on. A channel with no
    /// record has never been switched on its own, which means it is following the board.
    /// </summary>
    private static bool AnyChannelOn(List<AuraDevice> devices)
    {
        Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();

        foreach (AuraDevice device in devices)
        {
            foreach (AuraChannel channel in device.Channels)
            {
                if (AuraChannelStates.Get(remembered, device.Key, channel.Index) is not ChannelLighting look ||
                    look.On)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Switches to a lighting effect and remembers it as the state to restore.</summary>
    public static AuraState ApplyPreset(AuraPreset preset, Color? colour = null, string? deviceKey = null,
        int channel = -1)
    {
        AuraState state = AuraState.Load() with { On = true, Mode = preset.Mode, CustomPreset = "" };

        if (colour is Color chosen)
        {
            state = state with { Red = chosen.R, Green = chosen.G, Blue = chosen.B };
        }

        Send(preset.Mode, state, deviceKey, channel);
        state.Save();
        return state;
    }

    /// <summary>
    /// Changes the brightness and re-sends the running effect so it takes hold at once.
    /// </summary>
    /// <param name="deviceKey">
    /// One controller, or null for the whole board - which is the only option on the command
    /// line, and also what clears any brightness a single channel was given of its own.
    /// </param>
    /// <param name="channel">One channel of that controller, or -1 for all of them.</param>
    /// <remarks>
    /// The controller has no brightness register: this scales the colour that the effect
    /// command carries. Effects the firmware colours itself - the spectrum and rainbow modes -
    /// therefore cannot be dimmed at all, and the window hides the slider for them.
    /// </remarks>
    public static AuraState SetBrightness(byte percent, string? deviceKey = null, int channel = -1)
    {
        percent = Math.Clamp(percent, AuraState.MinBrightness, AuraState.MaxBrightness);

        AuraState state = AuraState.Load();
        bool wholeBoard = string.IsNullOrEmpty(deviceKey);

        // The board-wide value is the one every channel without a brightness of its own follows.
        if (wholeBoard)
        {
            state = state with { Brightness = percent };
        }

        if (wholeBoard && state.On && state.CustomPreset.Length > 0 &&
            AuraCustomPresets.Load().Find(p => p.Name == state.CustomPreset) is CustomPreset preset)
        {
            state.Save();
            return ApplyCustomPreset(preset);
        }

        var devices = AuraDevice.DiscoverAll();

        try
        {
            // Recorded before anything is re-sent, so the replay below picks the new value up per
            // channel. Setting it board-wide hands the channels back to the board-wide value
            // instead of leaving each one pinned to what it was given individually.
            AuraChannelStates.SetBrightness(Channels(devices, deviceKey, channel), wholeBoard ? (byte)0 : percent,
                BoardWide(state));

            if (state.On)
            {
                ApplyMix(devices, deviceKey, channel, state, look: null);
            }
        }
        finally
        {
            Close(devices);
        }

        state.Save();
        return state;
    }

    /// <summary>Every channel the caller named, as the pairs the stored records are keyed by.</summary>
    private static List<(string DeviceKey, int Channel)> Channels(List<AuraDevice> devices, string? deviceKey,
        int channel)
    {
        var targets = string.IsNullOrEmpty(deviceKey)
            ? devices
            : devices.FindAll(device => device.Key == deviceKey);

        if (targets.Count == 0)
        {
            throw new AuraNotFoundException(Strings.ErrorControllerNotFound, 3);
        }

        var named = new List<(string, int)>();

        foreach (AuraDevice device in targets)
        {
            foreach (AuraChannel affected in device.Channels)
            {
                if (channel < 0 || affected.Index == channel)
                {
                    named.Add((device.Key, affected.Index));
                }
            }
        }

        return named;
    }

    /// <summary>
    /// Applies a named bundle of per-device effects. The remembered state mirrors the first
    /// entry, which is what the button and the effect list show while the preset is active.
    /// </summary>
    public static AuraState ApplyCustomPreset(CustomPreset preset)
    {
        if (preset.Entries.Count == 0)
        {
            throw new AuraNotFoundException(Strings.ErrorControllerNotFound, 3);
        }

        AuraState state = AuraState.Load();
        var devices = AuraDevice.DiscoverAll();
        var applied = new List<(string DeviceKey, int Channel, ChannelLighting Look)>();

        try
        {
            Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();

            foreach (CustomPresetEntry entry in preset.Entries)
            {
                var look = new ChannelLighting(entry.Mode, entry.Red, entry.Green, entry.Blue,
                    On: true, entry.Brightness);

                foreach (AuraDevice device in devices.FindAll(d => d.Key == entry.DeviceKey))
                {
                    // Channel by channel rather than in one call, because each one is dimmed to
                    // its own brightness - a preset carries an effect and a colour, not a
                    // brightness, so what the channel was given keeps applying.
                    foreach (AuraChannel affected in device.Channels)
                    {
                        if (entry.Channel >= 0 && affected.Index != entry.Channel)
                        {
                            continue;
                        }

                        // The preset's own brightness for that channel, or - when it carries none -
                        // whatever the channel is already dimmed to.
                        byte percent = entry.Brightness != 0
                            ? entry.Brightness
                            : Brightness(AuraChannelStates.Get(remembered, device.Key, affected.Index), state);

                        (byte red, byte green, byte blue) =
                            AuraState.Dim(entry.Red, entry.Green, entry.Blue, percent);

                        device.Apply(entry.Mode, red, green, blue, affected.Index);
                        applied.Add((device.Key, affected.Index, look));
                    }
                }
            }
        }
        finally
        {
            Close(devices);
        }

        if (applied.Count == 0)
        {
            // Every controller the preset names has gone. Saying so beats reporting a look that
            // never reached any hardware.
            throw new AuraNotFoundException(Strings.ErrorControllerNotFound, 3);
        }

        // A preset sets each channel individually, so each one remembers its own look - written
        // in one pass instead of once per entry.
        AuraChannelStates.Remember(applied);

        CustomPresetEntry first = preset.Entries[0];
        AuraState next = state with
        {
            On = true,
            Mode = first.Mode,
            Red = first.Red,
            Green = first.Green,
            Blue = first.Blue,
            CustomPreset = preset.Name,
        };

        next.Save();
        return next;
    }

    /// <returns>Whether any channel on the machine is on afterwards.</returns>
    private static bool Send(byte mode, AuraState state, string? deviceKey, int channel)
    {
        var devices = AuraDevice.DiscoverAll();

        try
        {
            // The colour is recorded as chosen rather than as dimmed, so that picking one channel
            // in the window shows the colour that was set for it.
            var look = new ChannelLighting(mode, state.Red, state.Green, state.Blue, On: mode != AuraState.ModeOff);
            List<(string, int)> switched = ApplyMix(devices, deviceKey, channel, state, look);

            if (mode == AuraState.ModeOff)
            {
                // Switching off only clears the power flag: the effect and colour stay on record
                // so that switching back on has its own look to return to. A channel with no
                // record yet keeps the board's current look rather than a made-up one.
                AuraChannelStates.SetPower(switched, on: false, BoardWide(state));
            }
            else
            {
                AuraChannelStates.Remember(switched, look);
            }

            return AnyChannelOn(devices);
        }
        finally
        {
            Close(devices);
        }
    }

    /// <summary>Reports to the console of the calling shell - this is a WinExe and owns none.</summary>
    private static void WriteError(string message)
    {
        AttachConsole(AttachParentProcess);
        Console.Error.WriteLine(message);
    }
}
