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

                string name = AuraFiles.Caption(Text(item, "name"), AuraFiles.MaxPresetName);
                if (name.Length == 0 ||
                    !item.TryGetProperty("entries", out JsonElement entriesElement) ||
                    entriesElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var entries = new List<CustomPresetEntry>();
                foreach (JsonElement entry in entriesElement.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    entries.Add(new CustomPresetEntry(
                        Text(entry, "deviceKey"),
                        // Missing channel means an entry written before presets went
                        // per-channel: -1 keeps its old meaning of "the whole controller".
                        entry.TryGetProperty("channel", out JsonElement c) && c.TryGetInt32(out int index)
                            ? index
                            : -1,
                        AuraFiles.Caption(Text(entry, "label"), AuraFiles.MaxChannelLabel),
                        Byte(entry, "mode"), Byte(entry, "red"), Byte(entry, "green"), Byte(entry, "blue"),
                        // Missing brightness means an entry written before presets carried one:
                        // 0 leaves the channel at whatever brightness it already has.
                        Byte(entry, "brightness")));
                }

                if (entries.Count > 0)
                {
                    presets.Add(new CustomPreset(name, entries));
                }
            }

            return presets;
        }
        catch (Exception ex) when (AuraFiles.IsExpected(ex))
        {
            return presets;
        }
    }

    private static string Text(JsonElement element, string name) => AuraFiles.JsonText(element, name);

    private static byte Byte(JsonElement element, string name) => AuraFiles.JsonByte(element, name);

    public static void Save(List<CustomPreset> presets) => AuraFiles.Write(FileName, writer =>
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
