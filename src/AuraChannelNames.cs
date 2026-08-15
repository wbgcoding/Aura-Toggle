using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AuraToggle;

/// <summary>
/// User-chosen names for individual channels, kept in %LOCALAPPDATA%\aura-toggle\channel-names.json.
/// A channel with no override here falls back to its computed name (Onboard, ARGB 1, ...).
/// </summary>
internal static class AuraChannelNames
{
    internal const string FileName = "channel-names.json";

    /// <summary>
    /// Every chosen name, read once. Labelling a whole selector otherwise re-read and re-parsed
    /// the file for each channel in turn.
    /// </summary>
    public static Dictionary<string, string> All() => Load();

    /// <summary>Looks one channel up in an already loaded set.</summary>
    public static string? Get(Dictionary<string, string> names, string deviceKey, int channel) =>
        names.TryGetValue(AuraFiles.ChannelKey(deviceKey, channel), out string? name) && name.Length > 0
            ? name
            : null;

    /// <summary>Sets a channel's name, or clears it back to the default when <paramref name="name"/> is empty.</summary>
    public static void Set(string deviceKey, int channel, string name)
    {
        // Locked: the window and a command line invocation can be doing this at the same time,
        // and read-modify-write without a lock loses one of the two changes.
        using IDisposable guard = AuraFiles.Lock();

        Dictionary<string, string> names = Load();
        string key = AuraFiles.ChannelKey(deviceKey, channel);

        if (name.Trim().Length == 0)
        {
            names.Remove(key);
        }
        else
        {
            names[key] = AuraFiles.Caption(name, AuraFiles.MaxChannelName);
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
                    names[property.Name] = AuraFiles.Caption(property.Value.GetString() ?? "", AuraFiles.MaxChannelName);
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
