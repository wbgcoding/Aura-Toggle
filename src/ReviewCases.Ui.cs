#if REVIEW
using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToggle;

internal static partial class Program
{
    /// <summary>
    /// Engine cases for the window, the preset editor and the popups, run by "-review engine"
    /// against the throwaway data folder.
    /// </summary>
    private static void ReviewUiCases(Action<string, string, string> check)
    {
        EditorKeepsAbsentCases(check);
        EditorRelocaliseCases(check);
    }

    /// <summary>An open editor says its texts again when the language changes.</summary>
    private static void EditorRelocaliseCases(Action<string, string, string> check)
    {
        string before = Strings.Override;
        try
        {
            Strings.Override = "en";
            using var editor = new CustomPresetEditor(null, ReviewControllers(), AuraState.Load());
            string english = editor.ReviewTexts();

            Strings.Override = "de";
            // Two stand-in controllers are attached, so the row caption names its controller.
            string caption = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.ChannelQualified,
                "Aura Controller 1", Strings.ChannelOnboard);
            string expected = $"{Strings.CustomPresetNew}|{caption}|{Strings.CustomPresetCreate}";
            editor.Relocalise();
            check("editor relocalise: texts follow the language", expected, editor.ReviewTexts());
            check("editor relocalise: differs from the old language", "True", (english != editor.ReviewTexts()).ToString());
        }
        finally
        {
            Strings.Override = before;
        }
    }

    /// <summary>A preset saved from the editor keeps the entries of a controller that is not attached.</summary>
    private static void EditorKeepsAbsentCases(Action<string, string, string> check)
    {
        const string name = "engine-absent";
        var original = new CustomPreset(name, new List<CustomPresetEntry>
        {
            new("review-1", 0, "Onboard", 1, 0, 0, 255, 100),
            new("review-9", 0, "Onboard", 11, 0, 0, 0),
            new("review-9", 1, "ARGB 1", 1, 255, 0, 0, 80),
        });
        AuraCustomPresets.Save(new List<CustomPreset>(AuraCustomPresets.Load()) { original });

        using (var editor = new CustomPresetEditor(original, ReviewControllers(), AuraState.Load()))
        {
            editor.ReviewSave();
        }

        CustomPreset? stored = AuraCustomPresets.Load().Find(p => p.Name == name);
        check("editor save: absent controller entries kept", "review-9/0 review-9/1",
            string.Join(" ", stored?.Entries.Where(e => e.DeviceKey == "review-9").Select(e => $"{e.DeviceKey}/{e.Channel}") ?? []));
        check("editor save: attached controller written once", "1",
            (stored?.Entries.Count(e => e.DeviceKey == "review-1" && e.Channel == 0) ?? 0).ToString());

        List<CustomPreset> rest = AuraCustomPresets.Load();
        rest.RemoveAll(p => p.Name == name);
        AuraCustomPresets.Save(rest);
    }
}
#endif
