using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AuraToggle;

/// <summary>One lighting effect the controller can run.</summary>
internal sealed record AuraPreset(string Key, byte Mode, string ResourceKey, bool UsesColour)
{
    public string DisplayName => Strings.Preset(ResourceKey);

    /// <summary>
    /// One half-sentence explaining what the effect does, shown as a tooltip on its row in the
    /// drop down. Derived from <see cref="ResourceKey"/> rather than a second table to look up -
    /// "PresetStatic" always has an "EffectHintStatic" next to it in both .resx files.
    /// </summary>
    public string HintText => Strings.Preset("EffectHint" + ResourceKey["Preset".Length..]);

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
        new AuraPreset("spectrum-cycle", 4, "PresetSpectrumCycle", UsesColour: false),
        new AuraPreset("rainbow", 5, "PresetRainbow", UsesColour: false),
        new AuraPreset("rainbow-breathing", 6, "PresetRainbowBreathing", UsesColour: false),
        new AuraPreset("chase-fade", 7, "PresetChaseFade", UsesColour: true),
        new AuraPreset("chase", 9, "PresetChase", UsesColour: true),
        new AuraPreset("wave", 11, "PresetWave", UsesColour: false),
    };

    /// <summary>All preset names, for the usage line.</summary>
    public static string Names => string.Join(", ", All.Select(preset => preset.Key));

    /// <summary>
    /// Finds a preset by name. Spelling is forgiving: casing, spaces, hyphens and underscores
    /// are ignored, and the display name is accepted in any of the interface languages - not just
    /// whichever one
    /// the interface happens to be set to, so a script written as <c>-preset Regenbogen</c> keeps
    /// working after the user switches to English. Matches how <c>-channel</c> already resolves a
    /// channel's default name. The command line's <c>-custom</c> lookup uses this same helper,
    /// so both spellings behave alike.
    /// </summary>
    public static AuraPreset? Find(string name)
    {
        string wanted = Normalise(name);

        return All.FirstOrDefault(preset =>
            Normalise(preset.Key) == wanted ||
            Strings.Codes.Any(language =>
                Normalise(Strings.InLanguage(preset.ResourceKey, language)) == wanted));
    }

    public static AuraPreset? ByMode(byte mode) => All.FirstOrDefault(preset => preset.Mode == mode);

    public static string Normalise(string value)
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
