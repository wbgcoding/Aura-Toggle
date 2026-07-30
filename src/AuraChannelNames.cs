using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AuraToggle;

/// <summary>
/// User-chosen names for individual channels, kept in %LOCALAPPDATA%\aura-toggle\channel-names.json.
/// A channel with no override here falls back to its computed name (Onboard, ARGB 1, ...).
/// </summary>
internal static class AuraChannelNames
{
    private const string FileName = "channel-names.json";

    private static string Key(string deviceKey, int channel) => $"{deviceKey}|{channel}";

    /// <summary>
    /// Every chosen name, read once. Labelling a whole selector otherwise re-read and re-parsed
    /// the file for each channel in turn.
    /// </summary>
    public static Dictionary<string, string> All() => Load();

    /// <summary>The name chosen for this channel, or null if it still uses the default one.</summary>
    public static string? Get(string deviceKey, int channel) => Get(Load(), deviceKey, channel);

    /// <summary>Looks one channel up in an already loaded set.</summary>
    public static string? Get(Dictionary<string, string> names, string deviceKey, int channel) =>
        names.TryGetValue(Key(deviceKey, channel), out string? name) && name.Length > 0 ? name : null;

    /// <summary>Sets a channel's name, or clears it back to the default when <paramref name="name"/> is empty.</summary>
    public static void Set(string deviceKey, int channel, string name)
    {
        // Locked: the window and a command line invocation can be doing this at the same time,
        // and read-modify-write without a lock loses one of the two changes.
        using IDisposable guard = AuraFiles.Lock();

        Dictionary<string, string> names = Load();
        string key = Key(deviceKey, channel);

        if (name.Trim().Length == 0)
        {
            names.Remove(key);
        }
        else
        {
            names[key] = name.Trim();
        }

        Save(names);
    }

    private static Dictionary<string, string> Load()
    {
        var names = new Dictionary<string, string>();

        using JsonDocument? document = AuraFiles.Read(FileName, JsonValueKind.Object);
        if (document == null)
        {
            return names;
        }

        try
        {
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    names[property.Name] = property.Value.GetString() ?? "";
                }
            }

            return names;
        }
        catch (Exception ex) when (AuraFiles.IsExpected(ex))
        {
            return names;
        }
    }

    private static void Save(Dictionary<string, string> names) => AuraFiles.Write(FileName, writer =>
    {
        writer.WriteStartObject();
        foreach ((string key, string name) in names)
        {
            writer.WriteString(key, name);
        }

        writer.WriteEndObject();
    });
}
