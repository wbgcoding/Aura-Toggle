#if REVIEW
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AuraToggle;

internal static partial class Program
{
    /// <summary>
    /// Engine cases for the data files and the switching logic, run by "-review engine" against
    /// the throwaway data folder.
    /// </summary>
    private static void ReviewCoreCases(Action<string, string, string> check)
    {
        DamagedFileCases(check);
        EntryResolutionCases(check);
        SwitchOnCoverageCases(check);
    }

    private static void SwitchOnCoverageCases(Action<string, string, string> check)
    {
        var preset = new CustomPreset("coverage", new List<CustomPresetEntry>
        {
            new("review-1", 0, "Onboard", 1, 255, 0, 0),
            new("review-1", 1, "ARGB 1", 1, 0, 255, 0),
        });

        (List<string> named, List<string> left) = SplitControllers(preset, new[] { "review-1", "review-2" });
        check("switch on: preset names one of two, it applies", "review-1", string.Join(",", named));
        check("switch on: the other one is restored", "review-2", string.Join(",", left));

        (named, left) = SplitControllers(preset, new[] { "review-3", "review-4" });
        check("switch on: preset names none, nothing applies", "", string.Join(",", named));
        check("switch on: every attached one is restored", "review-3,review-4", string.Join(",", left));
    }

    private static void EntryResolutionCases(Action<string, string, string> check)
    {
        var whole = new CustomPresetEntry("review-1", -1, "All", 1, 10, 0, 0);
        var one = new CustomPresetEntry("review-1", 2, "ARGB 2", 5, 0, 20, 0);
        var other = new CustomPresetEntry("review-2", -1, "All", 3, 0, 0, 30);

        foreach (var order in new[] { new[] { whole, one, other }, new[] { one, whole, other } })
        {
            var entries = order.ToList();
            check("entry: channel entry wins over whole controller", "5",
                ResolveEntry(entries, "review-1", 2)?.Mode.ToString() ?? "none");
            check("entry: other channel takes the whole controller entry", "1",
                ResolveEntry(entries, "review-1", 0)?.Mode.ToString() ?? "none");
            check("entry: other controller keeps its entry", "3",
                ResolveEntry(entries, "review-2", 1)?.Mode.ToString() ?? "none");
            check("entry: unnamed controller has none", "none",
                ResolveEntry(entries, "review-3", 0)?.Mode.ToString() ?? "none");
        }

        var twice = new List<CustomPresetEntry> { whole, whole with { Mode = 7 } };
        check("entry: the later of two equal kinds wins", "7",
            ResolveEntry(twice, "review-1", 0)?.Mode.ToString() ?? "none");
    }

    private static void DamagedFileCases(Action<string, string, string> check)
    {
        string path = AuraFiles.PathTo(AuraCustomPresets.FileName);
        byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        string[] badBefore = Directory.GetFiles(AuraFiles.Folder, "presets.bad-*.json");

        try
        {
            byte[] broken = Encoding.UTF8.GetBytes("[{\"name\":\"one\",\"entries\":[],]");
            File.WriteAllBytes(path, broken);

            List<CustomPreset> loaded = AuraCustomPresets.Load();
            check("damaged presets: load gives none", "0", loaded.Count.ToString());

            var one = new CustomPreset("kept", new List<CustomPresetEntry>
            {
                new("review-1", 0, "Onboard", 1, 255, 0, 0),
            });
            check("damaged presets: save succeeds", "True", AuraCustomPresets.Save(new List<CustomPreset> { one }).ToString());

            string[] copies = Directory.GetFiles(AuraFiles.Folder, "presets.bad-*.json").Except(badBefore).ToArray();
            check("damaged presets: one bad copy", "1", copies.Length.ToString());
            check("damaged presets: copy holds the broken bytes", "True",
                (copies.Length == 1 && File.ReadAllBytes(copies[0]).SequenceEqual(broken)).ToString());
            check("damaged presets: new file holds the one preset", "kept",
                string.Join(",", AuraCustomPresets.Load().Select(p => p.Name)));

            AuraCustomPresets.Save(new List<CustomPreset> { one });
            check("damaged presets: a later save adds no copy", "1",
                Directory.GetFiles(AuraFiles.Folder, "presets.bad-*.json").Except(badBefore).Count().ToString());
        }
        finally
        {
            foreach (string copy in Directory.GetFiles(AuraFiles.Folder, "presets.bad-*.json").Except(badBefore))
            {
                File.Delete(copy);
            }

            if (original != null)
            {
                File.WriteAllBytes(path, original);
            }
            else
            {
                File.Delete(path);
            }
        }
    }
}
#endif
