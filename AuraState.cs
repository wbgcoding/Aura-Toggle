using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AuraToggle;

/// <summary>
/// The controller cannot report its current effect, so the last known lighting state is kept here.
/// Defaults to the ASUS factory effect when nothing has been stored yet.
/// </summary>
internal sealed record AuraState(bool On, byte Mode, byte Red, byte Green, byte Blue)
{
    public const byte ModeOff = 0x00;
    public const byte ModeRainbow = 0x05;

    /// <summary>White is the colour handed to the effects that use one; the rest ignore it.</summary>
    public static readonly AuraState Default = new(On: true, ModeRainbow, 0xFF, 0xFF, 0xFF);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "aura-toggle", "state.json");

    public static AuraState Load()
    {
        string path = FilePath;
        if (!File.Exists(path))
        {
            return Default;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            JsonElement root = document.RootElement;

            return new AuraState(
                On: !root.TryGetProperty("on", out JsonElement on) || on.ValueKind != JsonValueKind.False,
                Mode: Read(root, "mode", Default.Mode),
                Red: Read(root, "red", Default.Red),
                Green: Read(root, "green", Default.Green),
                Blue: Read(root, "blue", Default.Blue));
        }
        catch (JsonException)
        {
            // A damaged state file must not break switching the lights.
            return Default;
        }
    }

    private static byte Read(JsonElement root, string name, byte fallback) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetByte(out byte parsed) ? parsed : fallback;

    public void Save()
    {
        string path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string json = $"{{\"on\":{(On ? "true" : "false")},\"mode\":{Mode},\"red\":{Red},\"green\":{Green},\"blue\":{Blue}}}";
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
