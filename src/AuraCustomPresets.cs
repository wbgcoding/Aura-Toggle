using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AuraToggle;

/// <summary>
/// What one channel should be set to as part of a custom preset. Matched at apply time by
/// <see cref="DeviceKey"/> (the HID path, stable for as long as the controller stays in the
/// same USB port) and <see cref="Channel"/>; <see cref="Label"/> is only for display, in case
/// that controller is not connected when the preset is shown again.
/// </summary>
/// <param name="Brightness">
/// The percentage this channel's colour is scaled to, or 0 to leave the channel at whatever
/// brightness it already has - the same meaning as in <see cref="ChannelLighting"/>.
/// </param>
internal sealed record CustomPresetEntry(
    string DeviceKey, int Channel, string Label, byte Mode, byte Red, byte Green, byte Blue,
    byte Brightness = 0);

/// <summary>
/// A user-defined bundle of effects, one per channel, applied together under one name. Lets the
/// onboard zone, each ARGB header and each further controller run a different effect and
/// colour at the same time.
/// </summary>
internal sealed record CustomPreset(string Name, List<CustomPresetEntry> Entries);

/// <summary>Custom presets, kept in %LOCALAPPDATA%\aura-toggle\presets.json.</summary>
internal static class AuraCustomPresets
{
    internal const string FileName = "presets.json";

    public static List<CustomPreset> Load()
    {
        var presets = new List<CustomPreset>();
        var taken = new HashSet<string>();

        using JsonDocument? document = AuraFiles.Read(FileName, JsonValueKind.Array);
        if (document == null)
        {
            return presets;
        }

        try
        {
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                // Every element and every property is checked for its kind: this file is plain
                // text in the user's profile, so anything at all can be in it.
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string name = AuraFiles.Caption(AuraFiles.JsonText(item, "name"), AuraFiles.MaxPresetName);
                if (name.Length == 0 ||
                    !item.TryGetProperty("entries", out JsonElement entriesElement) ||
                    entriesElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var entries = new List<CustomPresetEntry>();
                foreach (JsonElement entry in entriesElement.EnumerateArray())
                {
                    if (ReadEntry(entry) is CustomPresetEntry read)
                    {
                        entries.Add(read);
                    }
                }

                if (entries.Count > 0)
                {
                    presets.Add(new CustomPreset(UniqueName(name, taken), entries));
                }
            }

            return presets;
        }
        catch (Exception ex) when (AuraFiles.IsExpected(ex))
        {
            return presets;
        }
    }

    /// <summary>
    /// One stored or imported entry, or null when it is not one. Both presets.json and a preset
    /// file picked for import are plain text anything can be written into, so every property is
    /// checked for its kind and an unusable entry is dropped on its own instead of ending the read.
    /// </summary>
    internal static CustomPresetEntry? ReadEntry(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Same rule AuraChannelStates applies before a mode reaches the hardware: only a mode this
        // build actually knows - or "off" - is trusted from a hand-edited or imported file,
        // otherwise the window names an effect the board never runs while sending the board
        // something else entirely.
        byte mode = AuraPresets.ValidChannelMode(AuraFiles.JsonByte(entry, "mode"));

        // Brightness gets the same treatment as in AuraChannelStates: 0 stays "leave the channel
        // as it is", anything else is held to the range the slider offers. Missing brightness
        // means an entry written before presets carried one, which reads as that same 0.
        byte brightness = AuraFiles.JsonByte(entry, "brightness");
        if (brightness != 0)
        {
            brightness = Math.Clamp(brightness, AuraState.MinBrightness, AuraState.MaxBrightness);
        }

        // Missing channel means an entry written before presets went per-channel: -1 keeps its old
        // meaning of "the whole controller". A channel that is not a whole number, or one below
        // -1, is not a channel at all, and the entry is dropped rather than read as "the whole
        // controller" by the `< 0` checks downstream.
        int channel = -1;
        if (entry.TryGetProperty("channel", out JsonElement channelElement) &&
            (channelElement.ValueKind != JsonValueKind.Number || !channelElement.TryGetInt32(out channel)))
        {
            return null;
        }

        if (channel < -1)
        {
            return null;
        }

        return new CustomPresetEntry(
            AuraFiles.JsonText(entry, "deviceKey"),
            channel,
            AuraFiles.Caption(AuraFiles.JsonText(entry, "label"), AuraFiles.MaxChannelLabel),
            mode, AuraFiles.JsonByte(entry, "red"),
            AuraFiles.JsonByte(entry, "green"), AuraFiles.JsonByte(entry, "blue"),
            brightness);
    }

    /// <summary>
    /// The name to list a preset under: the name itself, or with " (2)", " (3)" and so on when an
    /// earlier preset already has it. Cutting a long name to the cap can make two different ones
    /// equal, and every lookup and delete goes by name. The cut base keeps the result inside the
    /// cap. The chosen name is added to <paramref name="taken"/>.
    /// </summary>
    internal static string UniqueName(string name, HashSet<string> taken)
    {
        string candidate = name;

        for (int count = 2; !taken.Add(candidate); count++)
        {
            string suffix = $" ({count})";
            string cut = name.Length + suffix.Length > AuraFiles.MaxPresetName
                ? name[..(AuraFiles.MaxPresetName - suffix.Length)]
                : name;
            candidate = AuraFiles.Caption(cut, AuraFiles.MaxPresetName) + suffix;
        }

        return candidate;
    }

    public static bool Save(List<CustomPreset> presets) => AuraFiles.Write(FileName, writer =>
    {
        writer.WriteStartArray();
        foreach (CustomPreset preset in presets)
        {
            writer.WriteStartObject();
            writer.WriteString("name", preset.Name);
            writer.WriteStartArray("entries");
            foreach (CustomPresetEntry entry in preset.Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("deviceKey", entry.DeviceKey);
                writer.WriteNumber("channel", entry.Channel);
                writer.WriteString("label", entry.Label);
                writer.WriteNumber("mode", entry.Mode);
                writer.WriteNumber("red", entry.Red);
                writer.WriteNumber("green", entry.Green);
                writer.WriteNumber("blue", entry.Blue);
                writer.WriteNumber("brightness", entry.Brightness);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    });
}
