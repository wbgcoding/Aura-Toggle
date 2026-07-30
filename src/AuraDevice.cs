using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace AuraToggle;

/// <summary>The controller was not found on this machine.</summary>
internal sealed class AuraNotFoundException : Exception
{
    public AuraNotFoundException(string message, int exitCode) : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}

/// <summary>
/// One switchable channel of a controller. <paramref name="Header"/> is 0 for the fixed
/// onboard zone and 1..n for the RGB and ARGB headers. Naming is left to the caller, so a
/// language change relabels the selector without having to talk to the hardware again.
/// </summary>
internal sealed record AuraChannel(int Index, bool Onboard, int Header);

/// <summary>One controller for the selector: a stable key, a display name and its channels.</summary>
internal sealed record AuraDeviceSummary(string Key, string Name, List<AuraChannel> Channels);

/// <summary>
/// Names a channel for the selector and the preset editor. Kept out of <see cref="AuraDevice"/>
/// so that a language change can relabel everything without touching the hardware again.
/// </summary>
internal static class ChannelLabels
{
    /// <param name="withDevice">
    /// Prefixes the controller's name, for machines that have more than one.
    /// </param>
    /// <param name="chosen">
    /// The chosen names, when labelling several channels in a row - passing them in reads the
    /// file once instead of once per channel.
    /// </param>
    public static string For(AuraDeviceSummary device, AuraChannel channel, bool withDevice,
        Dictionary<string, string>? chosen = null)
    {
        string? own = chosen == null
            ? AuraChannelNames.Get(device.Key, channel.Index)
            : AuraChannelNames.Get(chosen, device.Key, channel.Index);

        string name = own ?? (channel.Onboard
            ? Strings.ChannelOnboard
            : string.Format(CultureInfo.CurrentCulture, Strings.ChannelHeader, channel.Header));

        return withDevice
            ? string.Format(CultureInfo.CurrentCulture, Strings.ChannelQualified, device.Name, name)
            : name;
    }
}

/// <summary>
/// One ASUS Aura USB LED controller. Speaks the vendor HID protocol directly:
/// 65 byte reports, report id 0xEC, command in the first payload byte.
/// Only volatile commands are used - the controller flash is never written.
/// </summary>
internal sealed class AuraDevice : IDisposable
{
    private const byte ReportId = 0xEC;
    private const byte CmdReadFirmware = 0x82;
    private const byte CmdReadConfigTable = 0xB0;
    private const byte ReplyFirmware = 0x02;
    private const byte ReplyConfigTable = 0x30;
    private const byte CmdMainboardEffect = 0x35;
    private const byte CmdMainboardEffectColor = 0x36;
    private const byte CmdAddressableEffect = 0x3B;

    private const ushort VendorId = 0x0B05;
    private const ushort AuraMainboardUsagePage = 0xFF72;
    private const int ReportLength = 65;
    private const int ReplyTimeoutMs = 1000;
    private const int ReportGapMs = 8;

    /// <summary>Aura mainboard and addressable controllers, covering ASUS boards from X470/Z390 onwards.</summary>
    private static readonly ushort[] KnownProductIds =
    {
        0x1867, 0x1872, 0x18A3, 0x18A5, // addressable controllers
        0x1889,                         // Aura Terminal
        0x18F3, 0x1939, 0x19AF, 0x1AA6, 0x1BED, // mainboard controllers
    };

    private readonly HidStream _stream;
    private readonly bool _addressableOnly;
    private readonly List<Channel> _channels = new();

    private AuraDevice(HidStream stream, byte[] configTable)
    {
        _stream = stream;

        int addressableHeaders = configTable[0x02];
        int mainboardLeds = configTable[0x1B];
        _addressableOnly = mainboardLeds == 0;

        int startLed = 0;
        if (mainboardLeds > 0)
        {
            _channels.Add(new Channel(0, startLed, mainboardLeds, Header: 0));
            startLed += mainboardLeds;
        }

        for (int i = 0; i < addressableHeaders && _channels.Count < byte.MaxValue; i++)
        {
            // Each addressable header contributes exactly one effect color slot.
            _channels.Add(new Channel((byte)_channels.Count, startLed, 1, Header: i + 1));
            startLed++;
        }

        Name = "";
    }

    private readonly record struct Channel(byte Index, int StartLed, int LedCount, int Header);

    /// <summary>The USB product string when the device exposes one, empty otherwise.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// The HID device path. Stable for as long as this physical controller stays in the same
    /// USB port, which is what lets the window remember which single device was selected.
    /// </summary>
    public string Key => _stream.Info.Path;

    public int ChannelCount => _channels.Count;

    /// <summary>The channels this controller drives, in the order the protocol addresses them.</summary>
    public List<AuraChannel> Channels =>
        _channels.ConvertAll(channel => new AuraChannel(channel.Index, channel.Header == 0, channel.Header));

    /// <summary>Every Aura LED controller present. Throws if none answers.</summary>
    public static List<AuraDevice> DiscoverAll()
    {
        // One sweep, not two: enumerating opens and queries every HID interface on the machine,
        // which is the slow part of discovery, and the known and unknown ids were each doing a
        // full pass of their own.
        List<HidInfo> present = Hid.Enumerate((vid, _) => vid == VendorId);

        var candidates = new List<HidInfo>();
        candidates.AddRange(present.Where(info => KnownProductIds.Contains(info.Pid)));

        // Unknown product ids are accepted when they expose the Aura vendor usage page, so that
        // newer boards work without a code change. The handshake below still has to succeed.
        candidates.AddRange(present.Where(info =>
            !KnownProductIds.Contains(info.Pid) && info.UsagePage == AuraMainboardUsagePage));

        var devices = new List<AuraDevice>();
        int accessDenied = 0;

        foreach (HidInfo info in candidates.Where(info => info.OutputReportLength >= ReportLength))
        {
            HidStream? stream = null;
            try
            {
                stream = Hid.Open(info);
                AuraDevice? device = TryHandshake(stream);
                if (device == null)
                {
                    stream.Dispose();
                    continue;
                }

                stream = null; // ownership moved to the device
                devices.Add(device);
            }
            catch (HidAccessException)
            {
                accessDenied++;
                stream?.Dispose();
            }
            catch (IOException)
            {
                // One unresponsive interface must not stop the others from being found.
                stream?.Dispose();
            }
        }

        if (devices.Count == 0)
        {
            throw accessDenied > 0
                ? new AuraNotFoundException(Strings.ErrorControllerBusy, 4)
                : new AuraNotFoundException(Strings.ErrorControllerNotFound, 3);
        }

        return devices;
    }

    /// <summary>
    /// Every controller present, for the title bar and the per-device selector. Empty when
    /// none is reachable - everything here is what the hardware actually reports, since the
    /// running effect cannot be read back.
    /// </summary>
    public static List<AuraDeviceSummary> ListDevices()
    {
        List<AuraDevice> devices;
        try
        {
            devices = DiscoverAll();
        }
        catch (Exception ex) when (ex is AuraNotFoundException or IOException)
        {
            return new List<AuraDeviceSummary>();
        }

        try
        {
            // Unnamed devices of the same kind are numbered, so two Aura Controllers on one
            // machine are still distinguishable in the list.
            var unnamed = 0;
            var summaries = new List<AuraDeviceSummary>();
            foreach (AuraDevice device in devices)
            {
                string name = device.Name.Length > 0
                    ? device.Name
                    : string.Format(CultureInfo.CurrentCulture, Strings.DeviceFallbackName, ++unnamed);

                summaries.Add(new AuraDeviceSummary(device.Key, name, device.Channels));
            }

            return summaries;
        }
        finally
        {
            foreach (AuraDevice device in devices)
            {
                device.Dispose();
            }
        }
    }

    /// <summary>Confirms this interface speaks the Aura protocol and reads its channel layout.</summary>
    private static AuraDevice? TryHandshake(HidStream stream)
    {
        if (!Request(stream, CmdReadFirmware, out byte[] firmwareReply) ||
            firmwareReply.Length < 2 || firmwareReply[1] != ReplyFirmware)
        {
            return null;
        }

        if (!Request(stream, CmdReadConfigTable, out byte[] configReply) ||
            configReply.Length < 2 || configReply[1] != ReplyConfigTable)
        {
            return null;
        }

        // Reply layout: [0] report id, [1] reply code, [2..3] unused, [4..] 60 byte config table.
        // A device that answers with a shorter report gets the rest padded with zeroes rather
        // than crashing the lookup below.
        var configTable = new byte[60];
        configReply.Skip(4).Take(60).ToArray().CopyTo(configTable, 0);

        // The firmware string itself is not kept: answering 0x82 at all is the protocol probe,
        // and nothing in the tool has a use for the version.
        var device = new AuraDevice(stream, configTable) { Name = stream.Info.Product };

        return device.ChannelCount > 0 ? device : null;
    }

    private static bool Request(HidStream stream, byte command, out byte[] reply)
    {
        var request = new byte[stream.Info.OutputReportLength];
        request[0] = ReportId;
        request[1] = command;
        stream.Write(request);

        reply = new byte[stream.Info.InputReportLength];
        return stream.Read(reply, ReplyTimeoutMs);
    }

    /// <summary>
    /// Applies an effect mode to every channel, or to one of them. Volatile only - without the
    /// commit command the controller keeps its stored configuration and a reboot restores it.
    /// </summary>
    /// <param name="channelIndex">
    /// The single channel to switch, or -1 for all of them. Channels the caller does not name
    /// are left exactly as they are, which is how one header can run a different effect from
    /// the rest of the board.
    /// </param>
    /// <remarks>
    /// The whole sequence runs twice. The controller silently drops commands that arrive
    /// while it is still applying the previous one, which showed up as the onboard zone
    /// switching while the ARGB headers kept running. Together with the pause between
    /// reports in <see cref="Write"/> this makes the switch reliable; setting the same mode
    /// again is idempotent, so the second pass costs nothing but time.
    /// </remarks>
    public void Apply(byte mode, byte red, byte green, byte blue, int channelIndex = -1)
    {
        // The mode arrives from state.json, presets.json or channel-state.json, all of which are
        // plain text in the user's profile. Only the modes verified on this hardware may be sent -
        // an unverified number is exactly the fuzzing the project forbids - so anything else
        // falls back to the ASUS default.
        if (mode != AuraState.ModeOff && AuraPresets.ByMode(mode) == null)
        {
            mode = AuraState.ModeRainbow;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            foreach (Channel channel in _channels)
            {
                if (channelIndex >= 0 && channel.Index != channelIndex)
                {
                    continue;
                }

                if (_addressableOnly)
                {
                    Send(CmdAddressableEffect, channel.Index, 0x00, mode, red, green, blue);
                    continue;
                }

                Send(CmdMainboardEffect, channel.Index, 0x00, 0x00, mode);
                SendEffectColor(channel, red, green, blue);
            }
        }
    }

    /// <summary>Colour for the running effect, addressed by an LED bitmask.</summary>
    private void SendEffectColor(Channel channel, byte red, byte green, byte blue)
    {
        // The mask is two bytes wide, so it addresses at most 16 LEDs. Boards with more
        // onboard LEDs than that still switch effects correctly through 0x35; only the
        // colour of the LEDs past the sixteenth is left alone.
        int addressable = Math.Min(channel.LedCount, 16 - Math.Min(channel.StartLed, 16));
        if (addressable <= 0)
        {
            return;
        }

        int mask = ((1 << addressable) - 1) << channel.StartLed;

        var report = new byte[_stream.Info.OutputReportLength];
        report[0] = ReportId;
        report[1] = CmdMainboardEffectColor;
        report[2] = (byte)(mask >> 8);
        report[3] = (byte)(mask & 0xFF);
        report[4] = 0x00; // 1 would target the shutdown effect, which lives in flash

        for (int led = 0; led < addressable; led++)
        {
            int at = 5 + ((channel.StartLed + led) * 3);
            if (at + 2 >= report.Length)
            {
                break;
            }

            report[at] = red;
            report[at + 1] = green;
            report[at + 2] = blue;
        }

        Write(report);
    }

    private void Send(params byte[] payload)
    {
        // The report always has the length the device reported, never a hardcoded one.
        var report = new byte[_stream.Info.OutputReportLength];
        report[0] = ReportId;
        Array.Copy(payload, 0, report, 1, Math.Min(payload.Length, report.Length - 1));
        Write(report);
    }

    /// <summary>
    /// The only commands this tool is ever allowed to send. Everything here is verified on the
    /// reference board and volatile; anything else - above all the commit command 0x3F - writes
    /// the controller flash and can brick it. Enforced rather than merely documented, so a
    /// future edit cannot quietly widen it.
    /// </summary>
    private static readonly byte[] AllowedCommands =
    {
        CmdReadFirmware, CmdReadConfigTable, CmdMainboardEffect, CmdMainboardEffectColor, CmdAddressableEffect,
    };

    private void Write(byte[] report)
    {
        if (report.Length < 2 || Array.IndexOf(AllowedCommands, report[1]) < 0)
        {
            throw new InvalidOperationException(
                $"Refusing to send command 0x{(report.Length > 1 ? report[1] : 0):X2} to the LED controller.");
        }

        // Byte 4 of the colour command selects the shutdown effect, which lives in flash.
        if (report[1] == CmdMainboardEffectColor && report.Length > 4 && report[4] != 0x00)
        {
            throw new InvalidOperationException("Refusing to write the shutdown effect to flash.");
        }

        try
        {
            _stream.Write(report);

            // The controller needs a moment between commands, otherwise later channels are
            // dropped. Eight milliseconds per report keeps a full switch under 150 ms.
            Thread.Sleep(ReportGapMs);
        }
        catch (IOException ex)
        {
            throw new IOException(
                string.Format(CultureInfo.CurrentCulture, Strings.ErrorWriteFailed,
                    report[1].ToString("X2", CultureInfo.InvariantCulture), ex.Message),
                ex);
        }
    }

    public void Dispose() => _stream.Dispose();
}
