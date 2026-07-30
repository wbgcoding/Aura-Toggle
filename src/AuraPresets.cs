using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AuraToggle;

/// <summary>One lighting effect the controller can run.</summary>
/// <param name="PerChannel">
/// Whether a single channel can run this effect while its neighbours run something else. The
/// effects the firmware generates itself take the whole controller with them - setting the
/// rainbow on one header puts every header of that controller into it - so the window leaves
/// them out while a single channel is selected instead of letting the choice quietly spread.
/// </param>
internal sealed record AuraPreset(string Key, byte Mode, string ResourceKey, bool UsesColour,
    bool PerChannel = true)
{
    public string DisplayName => Strings.Preset(ResourceKey);

    /// <summary>Used as the entry text in the drop down.</summary>
    public override string ToString() => DisplayName;
}

/// <summary>
/// The effects offered to the user. Deliberately limited to the modes that are known to work
/// on the Aura mainboard controllers - unverified mode numbers are not exposed.
/// </summary>
internal static class AuraPresets
{
    public static readonly IReadOnlyList<AuraPreset> All = new[]
    {
        new AuraPreset("static", 1, "PresetStatic", UsesColour: true),
        new AuraPreset("breathing", 2, "PresetBreathing", UsesColour: true),
        new AuraPreset("flashing", 3, "PresetFlashing", UsesColour: true),
        new AuraPreset("spectrum-cycle", 4, "PresetSpectrumCycle", UsesColour: false, PerChannel: false),
        new AuraPreset("rainbow", 5, "PresetRainbow", UsesColour: false, PerChannel: false),
        new AuraPreset("rainbow-breathing", 6, "PresetRainbowBreathing", UsesColour: false, PerChannel: false),
        new AuraPreset("chase-fade", 7, "PresetChaseFade", UsesColour: true),
        new AuraPreset("chase", 9, "PresetChase", UsesColour: true),
        new AuraPreset("wave", 11, "PresetWave", UsesColour: false, PerChannel: false),
    };

    /// <summary>All preset names, for the usage line.</summary>
    public static string Names => string.Join(", ", All.Select(preset => preset.Key));

    /// <summary>
    /// Finds a preset by name. Spelling is forgiving: casing, spaces, hyphens and underscores
    /// are ignored, and the translated display name is accepted as well.
    /// </summary>
    public static AuraPreset? Find(string name)
    {
        string wanted = Normalise(name);

        return All.FirstOrDefault(preset =>
            Normalise(preset.Key) == wanted || Normalise(preset.DisplayName) == wanted);
    }

    public static AuraPreset? ByMode(byte mode) => All.FirstOrDefault(preset => preset.Mode == mode);

    private static string Normalise(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
