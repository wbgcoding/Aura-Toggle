using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AuraToggle;

/// <summary>One lighting effect the controller can run.</summary>
internal sealed record AuraPreset(string Key, byte Mode, string ResourceKey)
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
        new AuraPreset("static", 1, "PresetStatic"),
        new AuraPreset("breathing", 2, "PresetBreathing"),
        new AuraPreset("flashing", 3, "PresetFlashing"),
        new AuraPreset("spectrum-cycle", 4, "PresetSpectrumCycle"),
        new AuraPreset("rainbow", 5, "PresetRainbow"),
        new AuraPreset("rainbow-breathing", 6, "PresetRainbowBreathing"),
        new AuraPreset("chase-fade", 7, "PresetChaseFade"),
        new AuraPreset("chase", 9, "PresetChase"),
        new AuraPreset("wave", 11, "PresetWave"),
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
