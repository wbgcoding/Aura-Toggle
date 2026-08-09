using System;
using System.Text.Json;

namespace AuraToggle;

/// <summary>
/// The controller cannot report its current effect, so the last known lighting state is kept here.
/// Defaults to the ASUS factory effect when nothing has been stored yet.
/// </summary>
internal sealed record AuraState(
    bool On, byte Mode, byte Red, byte Green, byte Blue, string CustomPreset, byte Brightness)
{
    public const byte ModeOff = 0x00;
    public const byte ModeStatic = 0x01;
    public const byte ModeRainbow = 0x05;

    /// <summary>
    /// Brightness is a percentage the effect colour is scaled by before it is sent, not a
    /// setting on the controller - it has none. Ten per cent is the floor, because zero would
    /// be indistinguishable from switching the lighting off.
    /// </summary>
    public const byte MinBrightness = 10;

    public const byte MaxBrightness = 100;

    /// <summary>White is the colour handed to the effects that use one; the rest ignore it.</summary>
    public static readonly AuraState Default =
        new(On: true, ModeRainbow, 0xFF, 0xFF, 0xFF, CustomPreset: "", MaxBrightness);

    /// <summary>Scales a colour to a brightness percentage, clamped to the usable range.</summary>
    public static (byte Red, byte Green, byte Blue) Dim(byte red, byte green, byte blue, byte brightness)
    {
        int percent = Math.Clamp(brightness, MinBrightness, MaxBrightness);
        return ((byte)(red * percent / 100), (byte)(green * percent / 100), (byte)(blue * percent / 100));
    }

    internal const string FileName = "state.json";

    public static AuraState Load()
    {
        using JsonDocument? document = AuraFiles.Read(FileName, JsonValueKind.Object);
        if (document == null)
        {
            return Default;
        }

        try
        {
            JsonElement root = document.RootElement;

            // An effect number no controller of ours runs is put back to the default here rather
            // than carried around: it is sent as the default anyway, and keeping it would leave
            // the window naming one effect while the board runs another.
            byte mode = AuraFiles.JsonByte(root, "mode", Default.Mode);

            return new AuraState(
                On: AuraFiles.JsonFlag(root, "on", Default.On),
                Mode: AuraPresets.ByMode(mode) == null ? Default.Mode : mode,
                Red: AuraFiles.JsonByte(root, "red", Default.Red),
                Green: AuraFiles.JsonByte(root, "green", Default.Green),
                Blue: AuraFiles.JsonByte(root, "blue", Default.Blue),
                CustomPreset: AuraFiles.JsonText(root, "customPreset"),
                Brightness: Math.Clamp(AuraFiles.JsonByte(root, "brightness", Default.Brightness), MinBrightness,
                    MaxBrightness));
        }
        catch (Exception ex) when (AuraFiles.IsExpected(ex))
        {
            // A damaged or unreadable state file must not break switching the lights.
            return Default;
        }
    }

    /// <summary>
    /// Remembers the state. Failing to write it is not worth aborting a switch that already
    /// reached the hardware, so the error is swallowed and the lights stay as they are.
    /// </summary>
    public void Save() => AuraFiles.Write(FileName, writer =>
    {
        writer.WriteStartObject();
        writer.WriteBoolean("on", On);
        writer.WriteNumber("mode", Mode);
        writer.WriteNumber("red", Red);
        writer.WriteNumber("green", Green);
        writer.WriteNumber("blue", Blue);
        writer.WriteString("customPreset", CustomPreset);
        writer.WriteNumber("brightness", Brightness);
        writer.WriteEndObject();
    });
}
