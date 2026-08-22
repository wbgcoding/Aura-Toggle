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
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    // Same rule AuraChannelStates applies before a mode reaches the hardware: only
                    // a mode this build actually knows - or "off" - is trusted from a hand-edited
                    // or imported file, otherwise the window names an effect the board never runs
                    // while sending the board something else entirely. Falls back to static, not
                    // AuraState.Default.Mode (rainbow): a per-channel entry is sent to one channel
                    // alone, and rainbow is one of the four firmware-generated modes that pulls
                    // every channel on the controller into it the moment one channel gets it
                    // (docs/INVARIANTS.md) - exactly the kind of surprise this validation exists
                    // to prevent, not reintroduce through its own fallback.
                    byte rawMode = AuraFiles.JsonByte(entry, "mode");
                    byte mode = rawMode == AuraState.ModeOff || AuraPresets.ByMode(rawMode) != null
                        ? rawMode
                        : AuraState.ModeStatic;

                    entries.Add(new CustomPresetEntry(
                        AuraFiles.JsonText(entry, "deviceKey"),
                        // Missing channel means an entry written before presets went
                        // per-channel: -1 keeps its old meaning of "the whole controller".
                        entry.TryGetProperty("channel", out JsonElement c) && c.TryGetInt32(out int index)
                            ? index
                            : -1,
                        AuraFiles.Caption(AuraFiles.JsonText(entry, "label"), AuraFiles.MaxChannelLabel),
                        mode, AuraFiles.JsonByte(entry, "red"),
                        AuraFiles.JsonByte(entry, "green"), AuraFiles.JsonByte(entry, "blue"),
                        // Missing brightness means an entry written before presets carried one:
                        // 0 leaves the channel at whatever brightness it already has.
                        AuraFiles.JsonByte(entry, "brightness")));
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
