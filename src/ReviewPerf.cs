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
            // Above every other program: a window some other app opened on top of this one would
            // rightly pause the button and fail the timing for a reason that is not under test.
            form.TopMost = true;
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
                    + $"paint avg {paints.Average(),5:0.00} max {paints.Max(),5:0.00} ms  "
                    + (FrameClock.Synced ? "vblank" : "free"));
            }

            // Twenty seconds of the rainbow, the effect a hitch shows worst in: every gap over one
            // and a half frames is listed with when it happened.
            button.Show(true, 5, Color.FromArgb(140, 80, 255));
            await Task.Delay(400);
            frames.Clear();
            await Task.Delay(20000);
            List<(long At, double PaintMs)> run = frames.ToList();
            var hitches = run.Zip(run.Skip(1), (a, b) => (
                    At: Stopwatch.GetElapsedTime(run[0].At, b.At).TotalSeconds,
                    Gap: Stopwatch.GetElapsedTime(a.At, b.At).TotalMilliseconds))
                .Where(hitch => hitch.Gap > 25).ToList();
            lines.Add($"rainbow 20 s  frames {run.Count}  gaps over 25 ms {hitches.Count}"
                + string.Concat(hitches.Take(10).Select(hitch => $"  {hitch.At:0.0}s:{hitch.Gap:0}ms")));

            // A window of another process laid over the button: the animation has to stop, and
            // start again within a frame or two of that window going away.
            Rectangle area = button.RectangleToScreen(button.ClientRectangle);
            area.Inflate(20, 20);
            File.Delete(Path.Combine(Path.GetTempPath(), "aura-cover.txt"));
            using (Process cover = Process.Start(Environment.ProcessPath!,
                       $"-review cover {area.X} {area.Y} {area.Width} {area.Height}"))
            {
                // A fresh process takes a few seconds to show its window on a slow machine.
                // Its own trace says when; a window kept out of the taskbar has no MainWindowHandle.
                string coverTrace = Path.Combine(Path.GetTempPath(), "aura-cover.txt");
                for (int i = 0; i < 100 && !cover.HasExited && !CoverShown(coverTrace); i++)
                {
                    await Task.Delay(100);
                }

                await Task.Delay(500);
                bool paused = button.Covered;
                string coverReport = $"cover window {cover.MainWindowHandle:X} exited {cover.HasExited}"
                    + Environment.NewLine + OcclusionWatch.DescribeProcessWindows(cover.Id)
                    + Environment.NewLine + form.DescribeOcclusion();
                frames.Clear();
                await Task.Delay(1000);
                int whileCovered = frames.Count;

                frames.Clear();
                long gone = Stopwatch.GetTimestamp();
                cover.Kill();
                cover.WaitForExit(5000);
                await Task.Delay(1000);
                double resumed = frames.Count > 0 ? Stopwatch.GetElapsedTime(gone, frames[0].At).TotalMilliseconds : -1;

                // One paint is allowed while covered: the pause itself repaints the button once.
                bool coverOk = paused && whileCovered <= 1 && !button.Covered && resumed is >= 0 and < 150;
                lines.Add($"cover   paused {paused}  frames while covered {whileCovered}  "
                    + $"first frame {resumed:0} ms after the window went  {(coverOk ? "PASS" : "FAIL")}");
                exitCode |= coverOk ? 0 : 1;
                if (!coverOk)
                {
                    lines.Add(coverReport);
                    lines.Add($"after the window went: covered {button.Covered}");
                    lines.Add(form.DescribeOcclusion());
                }
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

    private static bool CoverShown(string trace)
    {
        try
        {
            return File.Exists(trace) && File.ReadAllText(trace).Contains("shown", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>"cover x y w h": a plain opaque window at that screen rectangle, above everything,
    /// for <c>-review perf</c> to lay over its own window from another process.</summary>
    private static int ReviewCover(string[] args)
    {
        string trace = Path.Combine(Path.GetTempPath(), "aura-cover.txt");
        File.WriteAllText(trace, $"args {string.Join(' ', args)}{Environment.NewLine}");
        ApplicationConfiguration.Initialize();
        int[] at = args.Skip(2).Take(4).Select(arg => int.TryParse(arg, out int value) ? value : 0).ToArray();
        if (at.Length < 4)
        {
            return 2;
        }

        using var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(at[0], at[1], at[2], at[3]),
            ShowInTaskbar = false,
            BackColor = Color.DimGray,
        };
        form.Shown += (_, _) =>
        {
            // Set once the window exists: given up front, it never reached the window, which then
            // opened behind the one it was meant to cover - a process in the background may not
            // put its window in front by being shown.
            form.TopMost = true;
            File.AppendAllText(trace, $"shown {form.Bounds} topmost {form.TopMost}{Environment.NewLine}");
        };
        Application.Run(form);
        return 0;
    }
}
#endif
