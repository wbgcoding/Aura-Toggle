using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AuraToggle;

/// <summary>
/// The effect, colour, brightness and on/off state one channel was last set to. Switching a
/// channel off keeps the rest, so switching it back on has something to return to.
/// </summary>
/// <param name="Brightness">
/// The percentage this channel's colour is scaled to, or 0 while it has none of its own and
/// follows the board-wide brightness instead. Zero is free for that: the usable range starts at
/// <see cref="AuraState.MinBrightness"/>.
/// </param>
/// <param name="Seen">
/// The day this channel was last written, counted from the Unix epoch, so records for hardware
/// that is long gone can be cleared out. Days rather than seconds: a 30 day policy needs nothing
/// finer, and the count stays small enough to be an ordinary number for centuries. Zero means a
/// record from before the file carried the field.
/// </param>
internal sealed record ChannelLighting(byte Mode, byte Red, byte Green, byte Blue, bool On = true,
    byte Brightness = 0, int Seen = 0);

/// <summary>
/// What each channel was last set to, kept in %LOCALAPPDATA%\aura-toggle\channel-state.json.
/// The controller cannot be asked what a channel is running, so selecting a single channel in
/// the window can only show its own colour again if the tool wrote it down when it set it.
/// </summary>
internal static class AuraChannelStates
{
    internal const string FileName = "channel-state.json";

    /// <summary>
    /// How long a channel's record outlives the last time anything wrote to it. The point of the
    /// file is that a controller unplugged for a while still comes back to its own look, so this
    /// has to be generous - but without it the file keeps a row for every header of every
    /// controller ever plugged into the machine, for good. Any channel still attached is written
    /// again by any whole-board switch, so only genuinely departed hardware ages out.
    /// </summary>
    private const int KeepDays = 30;

    private static int Today => (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400);

    private static string Key(string deviceKey, int channel) => AuraFiles.ChannelKey(deviceKey, channel);

    /// <summary>
    /// Every channel's remembered look, read once. Callers that need more than a single channel
    /// take this and index it, rather than re-reading and re-parsing the file per channel.
    /// </summary>
    public static Dictionary<string, ChannelLighting> All() => Load();

    /// <summary>Looks one channel up in an already loaded set.</summary>
    public static ChannelLighting? Get(Dictionary<string, ChannelLighting> states, string deviceKey, int channel) =>
        states.TryGetValue(Key(deviceKey, channel), out ChannelLighting? lighting) ? lighting : null;

    /// <summary>
    /// Records one look against every channel it was applied to, in a single write - a switch
    /// covering every channel of every controller would otherwise rewrite the file per channel.
    /// </summary>
    public static void Remember(IEnumerable<(string DeviceKey, int Channel)> targets, ChannelLighting lighting)
    {
        Update(targets, (states, key) => Keep(states, key, lighting));
    }

    /// <summary>
    /// A new effect or colour does not carry a brightness, so a channel that was given one of its
    /// own keeps it rather than falling back to the board-wide value on the next colour change.
    /// </summary>
    private static ChannelLighting Keep(Dictionary<string, ChannelLighting> states, string key,
        ChannelLighting lighting) =>
        lighting.Brightness == 0 && states.TryGetValue(key, out ChannelLighting? existing)
            ? lighting with { Brightness = existing.Brightness }
            : lighting;

    /// <summary>
    /// Records a different look per channel in one write, for a custom preset that sets each
    /// channel to something of its own.
    /// </summary>
    /// <param name="keepBrightness">
    /// False only for the whole-board brightness slider applying a preset: every look it passes
    /// already carries the new value (0, meaning "follow the board" - the same rule every other
    /// brightness-0 record follows), and that has to stick rather than be read back as "unset" and
    /// replaced with whatever the channel had before. Keeping it true for every other caller is
    /// what makes an effect or colour change leave a channel's own brightness alone.
    /// </param>
    public static void Remember(IEnumerable<(string DeviceKey, int Channel, ChannelLighting Look)> looks,
        bool keepBrightness = true)
    {
        using IDisposable guard = AuraFiles.Lock();

        Dictionary<string, ChannelLighting> states = Load();
        int today = Today;
        var changed = false;

        foreach ((string deviceKey, int channel, ChannelLighting look) in looks)
        {
            string key = Key(deviceKey, channel);
            states[key] = (keepBrightness ? Keep(states, key, look) : look) with { Seen = today };
            changed = true;
        }

        if (changed)
        {
            Save(states);
        }
    }

    /// <summary>
    /// Flips only the on/off flag of the given channels, keeping the effect and colour they
    /// remember, so switching a channel off and on again returns it to its own look.
    /// </summary>
    /// <param name="fallback">
    /// What to record for a channel that has never been set on its own - the lighting the board
    /// is running, so a channel switched off and on again does not come back as something else.
    /// </param>
    public static void SetPower(IEnumerable<(string DeviceKey, int Channel)> targets, bool on,
        ChannelLighting fallback)
    {
        Update(targets, (states, key) => states.TryGetValue(key, out ChannelLighting? existing)
            ? existing with { On = on }
            : fallback with { On = on });
    }

    /// <summary>
    /// Gives the named channels a brightness of their own, keeping their effect, colour and
    /// power. A percentage of 0 hands them back to the board-wide brightness.
    /// </summary>
    public static void SetBrightness(IEnumerable<(string DeviceKey, int Channel)> targets, byte percent,
        ChannelLighting fallback)
    {
        Update(targets, (states, key) => states.TryGetValue(key, out ChannelLighting? existing)
            ? existing with { Brightness = percent }
            : fallback with { Brightness = percent });
    }

    private static void Update(IEnumerable<(string DeviceKey, int Channel)> targets,
        Func<Dictionary<string, ChannelLighting>, string, ChannelLighting> next)
    {
        // Locked: a switch from the command line and one from the window would otherwise each
        // read, change and save their own copy, and one of the two changes would vanish.
        using IDisposable guard = AuraFiles.Lock();

        Dictionary<string, ChannelLighting> states = Load();
        int today = Today;
        var changed = false;

        foreach ((string deviceKey, int channel) in targets)
        {
            string key = Key(deviceKey, channel);
            states[key] = next(states, key) with { Seen = today };
            changed = true;
        }

        if (changed)
        {
            Save(states);
        }
    }

    private static Dictionary<string, ChannelLighting> Load()
    {
        var states = new Dictionary<string, ChannelLighting>();

        using JsonDocument? document = AuraFiles.Read(FileName, JsonValueKind.Object);
        if (document == null)
        {
            return states;
        }

        try
        {
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                byte brightness = AuraFiles.JsonByte(property.Value, "brightness");

                // Same rule AuraDevice.Verified applies before a mode reaches the hardware: only
                // a mode this build actually knows - or "off" - is trusted from a hand-edited or
                // outdated file, otherwise the window would name an effect the board never runs.
                // Falls back to static, not AuraState.Default.Mode (rainbow): this is one channel's
                // own entry, and rainbow is one of the four firmware-generated modes that pulls
                // every channel on the controller into it the moment one channel gets it -
                // exactly what this validation exists to prevent.
                byte rawMode = AuraFiles.JsonByte(property.Value, "mode");
                byte mode = rawMode == AuraState.ModeOff || AuraPresets.ByMode(rawMode) != null
                    ? rawMode
                    : AuraState.ModeStatic;

                states[property.Name] = new ChannelLighting(
                    mode,
                    AuraFiles.JsonByte(property.Value, "red"),
                    AuraFiles.JsonByte(property.Value, "green"),
                    AuraFiles.JsonByte(property.Value, "blue"),
                    // Written before channels remembered their own power state: treat as on,
                    // which is what it meant back then.
                    AuraFiles.JsonFlag(property.Value, "on", true),
                    // Missing means the channel follows the board-wide brightness, which is also
                    // what every record written before brightness went per channel means - left
                    // as zero rather than clamped into range, unlike a genuine stored value.
                    brightness == 0
                        ? (byte)0
                        : Math.Clamp(brightness, AuraState.MinBrightness, AuraState.MaxBrightness),
                    AuraFiles.JsonNumber(property.Value, "seen", 0));
            }

            return states;
        }
        catch (Exception ex) when (AuraFiles.IsExpected(ex))
        {
            return states;
        }
    }

    private static void Save(Dictionary<string, ChannelLighting> states) => AuraFiles.Write(FileName, writer =>
    {
        int today = Today;
        int oldest = today - KeepDays;

        writer.WriteStartObject();
        foreach ((string key, ChannelLighting lighting) in states)
        {
            // A record from a version that did not stamp its writes starts ageing from now rather
            // than counting as ancient, so upgrading never throws away what was remembered.
            int seen = lighting.Seen == 0 ? today : lighting.Seen;
            if (seen < oldest)
            {
                continue;
            }

            writer.WriteStartObject(key);
            writer.WriteNumber("mode", lighting.Mode);
            writer.WriteNumber("red", lighting.Red);
            writer.WriteNumber("green", lighting.Green);
            writer.WriteNumber("blue", lighting.Blue);
            writer.WriteBoolean("on", lighting.On);
            writer.WriteNumber("brightness", lighting.Brightness);
            writer.WriteNumber("seen", seen);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    });
}
