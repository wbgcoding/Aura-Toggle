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
