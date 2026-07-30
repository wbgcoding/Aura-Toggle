using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
internal sealed record ChannelLighting(byte Mode, byte Red, byte Green, byte Blue, bool On = true,
    byte Brightness = 0);

/// <summary>
/// What each channel was last set to, kept in %LOCALAPPDATA%\aura-toggle\channel-state.json.
/// The controller cannot be asked what a channel is running, so selecting a single channel in
/// the window can only show its own colour again if the tool wrote it down when it set it.
/// </summary>
internal static class AuraChannelStates
{
    private const string FileName = "channel-state.json";

    private static string Key(string deviceKey, int channel) => $"{deviceKey}|{channel}";

    /// <summary>
    /// Every channel's remembered look, read once. Callers that need more than a single channel
    /// take this and index it, rather than re-reading and re-parsing the file per channel.
    /// </summary>
    public static Dictionary<string, ChannelLighting> All() => Load();

    /// <summary>What this channel last ran, or null if it has never been set on its own.</summary>
    public static ChannelLighting? Get(string deviceKey, int channel) => Get(Load(), deviceKey, channel);

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
    public static void Remember(IEnumerable<(string DeviceKey, int Channel, ChannelLighting Look)> looks)
    {
        using IDisposable guard = AuraFiles.Lock();

        Dictionary<string, ChannelLighting> states = Load();
        var changed = false;

        foreach ((string deviceKey, int channel, ChannelLighting look) in looks)
        {
            string key = Key(deviceKey, channel);
            states[key] = Keep(states, key, look);
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

    /// <summary>One locked read-modify-write for every channel named, then a single save.</summary>
    private static void Update(IEnumerable<(string DeviceKey, int Channel)> targets,
        Func<Dictionary<string, ChannelLighting>, string, ChannelLighting> next)
    {
        // Locked: a switch from the command line and one from the window would otherwise each
        // read, change and save their own copy, and one of the two changes would vanish.
        using IDisposable guard = AuraFiles.Lock();

        Dictionary<string, ChannelLighting> states = Load();
        var changed = false;

        foreach ((string deviceKey, int channel) in targets)
        {
            string key = Key(deviceKey, channel);
            states[key] = next(states, key);
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

                states[property.Name] = new ChannelLighting(
                    Byte(property.Value, "mode"),
                    Byte(property.Value, "red"),
                    Byte(property.Value, "green"),
                    Byte(property.Value, "blue"),
                    // Written before channels remembered their own power state: treat as on,
                    // which is what it meant back then.
                    !property.Value.TryGetProperty("on", out JsonElement on) ||
                        on.ValueKind != JsonValueKind.False,
                    // Missing means the channel follows the board-wide brightness, which is also
                    // what every record written before brightness went per channel means.
                    Byte(property.Value, "brightness"));
            }

            return states;
        }
        catch (Exception ex) when (AuraFiles.IsExpected(ex))
        {
            return states;
        }
    }

    private static byte Byte(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetByte(out byte parsed) ? parsed : (byte)0;

    private static void Save(Dictionary<string, ChannelLighting> states) => AuraFiles.Write(FileName, writer =>
    {
        writer.WriteStartObject();
        foreach ((string key, ChannelLighting lighting) in states)
        {
            writer.WriteStartObject(key);
            writer.WriteNumber("mode", lighting.Mode);
            writer.WriteNumber("red", lighting.Red);
            writer.WriteNumber("green", lighting.Green);
            writer.WriteNumber("blue", lighting.Blue);
            writer.WriteBoolean("on", lighting.On);
            writer.WriteNumber("brightness", lighting.Brightness);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    });
}
