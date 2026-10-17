#if REVIEW
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AuraToggle;

internal static partial class Program
{
    /// <summary>
    /// "perf": runs every animated effect on the real window's big button for a few seconds and
    /// reports how evenly the frames arrive and how long each paint takes, to
    /// <c>%TEMP%\aura-perf.txt</c>. A stutter is an interval far above the average; a slow effect
    /// is a paint time near the frame budget. Ends with a click on the button, which must flip it
    /// straight away rather than once the controller has been written.
    /// </summary>
    private static int ReviewPerf()
    {
        ApplicationConfiguration.Initialize();
        ToggleForm.ReviewDevices = ReviewControllers();

        var form = new ToggleForm();
        EffectButton button = form.ToggleForReview;
        var frames = new List<(long At, double PaintMs)>();
        button.PaintTimedForReview = ms => frames.Add((Stopwatch.GetTimestamp(), ms));

        var lines = new List<string>();
        int exitCode = 0;

        form.Shown += async (_, _) =>
        {
            await Task.Delay(1500);
            lines.Add($"button {button.Width}x{button.Height}  dpi {form.DeviceDpi}");

            foreach (byte mode in new byte[] { 2, 3, 4, 5, 6, 7, 9, 11 })
            {
                button.Show(true, mode, Color.FromArgb(140, 80, 255));
                await Task.Delay(400);
                frames.Clear();
                await Task.Delay(3000);

                List<(long At, double PaintMs)> taken = frames.ToList();
                if (taken.Count < 3)
                {
                    lines.Add($"mode {mode,2}  FAIL only {taken.Count} frames");
                    exitCode = 1;
                    continue;
                }

                double[] gaps = taken.Zip(taken.Skip(1), (a, b) => Stopwatch.GetElapsedTime(a.At, b.At).TotalMilliseconds)
                    .OrderBy(gap => gap).ToArray();
                double[] paints = taken.Select(frame => frame.PaintMs).ToArray();
                double seconds = Stopwatch.GetElapsedTime(taken[0].At, taken[^1].At).TotalSeconds;

                lines.Add($"mode {mode,2}  fps {(taken.Count - 1) / seconds,5:0.0}  "
                    + $"gap avg {gaps.Average(),5:0.0} p95 {gaps[(int)(gaps.Length * 0.95)],5:0.0} max {gaps[^1],5:0.0} ms  "
                    + $"paint avg {paints.Average(),5:0.00} max {paints.Max(),5:0.00} ms");
            }

            // No controller behind the stand-ins, so the switch itself fails: the button has to
            // flip on the click, not after the hardware, and flip back once the switch reports
            // the failure.
            string before = button.Text;
            form.ClickToggleForReview();
            string atOnce = button.Text;
            await Task.Delay(4000);
            string settled = button.Text;
            bool clickOk = atOnce != before && settled == before;
            lines.Add($"click   before '{before}'  at once '{atOnce}'  after the failed switch '{settled}'  "
                + (clickOk ? "PASS" : "FAIL"));
            exitCode |= clickOk ? 0 : 1;

            foreach (Form open in Application.OpenForms.Cast<Form>().Where(f => f != form).ToList())
            {
                open.Close();
            }

            string report = string.Join(Environment.NewLine, lines);
            WriteLine(report);
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "aura-perf.txt"), report);
            }
            catch (Exception ex) when (AuraFiles.IsExpected(ex))
            {
                // Printed above already.
            }

            form.Close();
        };

        Application.Run(form);
        return exitCode;
    }
}
#endif
