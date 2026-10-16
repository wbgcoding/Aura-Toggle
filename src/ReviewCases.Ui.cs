#if REVIEW
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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
        ListHoverRepaintCases(check);
    }

    /// <summary>
    /// A hover change repaints only the two rows it touches. Painting the old hover in full and
    /// then just those two rows with the new one must give the same picture as painting the new
    /// hover in full, or a row would keep a stale highlight or icon on screen.
    /// </summary>
    private static void ListHoverRepaintCases(Action<string, string, string> check)
    {
        var items = AuraPresets.All
            .Select(p => new SelectItem(p.Key, p.DisplayName, p.Mode, Hint: p.HintText))
            .ToList();
        foreach (int dpi in new[] { 96, 144 })
        {
            using var popup = new SelectPopup(items, items[0], Color.White, 260, Theme.Ui, dpi);
            var all = new Rectangle(Point.Empty, popup.ClientSize);
            int differing = 0;
            for (int from = 1; from + 1 < items.Count; from++)
            {
                int to = from + 1;
                using Bitmap expected = popup.PaintForReview(to, null, all);
                using Bitmap partial = popup.PaintForReview(from, null, all);
                popup.PaintForReview(to, partial, popup.HoverAreaForReview(from));
                popup.PaintForReview(to, partial, popup.HoverAreaForReview(to));
                differing += SamePixels(expected, partial) ? 0 : 1;
            }

            check($"list: hover repaint matches a full paint at {dpi} dpi", "0",
                differing.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static bool SamePixels(Bitmap a, Bitmap b)
    {
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                {
                    return false;
                }
            }
        }

        return true;
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
