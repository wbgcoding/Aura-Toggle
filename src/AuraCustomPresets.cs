using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AuraToggle;

/// <summary>
/// What one device should be set to as part of a custom preset. Matched at apply time by
/// <see cref="DeviceKey"/> (the HID path, stable for as long as the controller stays in the
/// same USB port); <see cref="DeviceLabel"/> is only for display, in case that device is not
/// connected when the preset is shown again.
/// </summary>
internal sealed record CustomPresetEntry(string DeviceKey, string DeviceLabel, byte Mode, byte Red, byte Green, byte Blue);

/// <summary>
/// A user-defined bundle of effects, one per device, applied together under one name. Lets a
/// machine with more than one controller run a different effect and colour on each at once.
/// </summary>
internal sealed record CustomPreset(string Name, List<CustomPresetEntry> Entries);

/// <summary>Custom presets, kept in %LOCALAPPDATA%\aura-toggle\presets.json.</summary>
internal static class AuraCustomPresets
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "aura-toggle", "presets.json");

    public static List<CustomPreset> Load()
    {
        string path = FilePath;
        if (!File.Exists(path))
        {
            return new List<CustomPreset>();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var presets = new List<CustomPreset>();

            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                string name = item.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                if (name.Length == 0 || !item.TryGetProperty("entries", out JsonElement entriesElement))
                {
                    continue;
                }

                var entries = new List<CustomPresetEntry>();
                foreach (JsonElement entry in entriesElement.EnumerateArray())
                {
                    entries.Add(new CustomPresetEntry(
                        entry.TryGetProperty("deviceKey", out JsonElement k) ? k.GetString() ?? "" : "",
                        entry.TryGetProperty("deviceLabel", out JsonElement l) ? l.GetString() ?? "" : "",
                        Byte(entry, "mode"), Byte(entry, "red"), Byte(entry, "green"), Byte(entry, "blue")));
                }

                if (entries.Count > 0)
                {
                    presets.Add(new CustomPreset(name, entries));
                }
            }

            return presets;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new List<CustomPreset>();
        }
    }

    private static byte Byte(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetByte(out byte parsed) ? parsed : (byte)0;

    public static void Save(List<CustomPreset> presets)
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using FileStream stream = File.Create(path);
            using var writer = new Utf8JsonWriter(stream);

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
                    writer.WriteString("deviceLabel", entry.DeviceLabel);
                    writer.WriteNumber("mode", entry.Mode);
                    writer.WriteNumber("red", entry.Red);
                    writer.WriteNumber("green", entry.Green);
                    writer.WriteNumber("blue", entry.Blue);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
