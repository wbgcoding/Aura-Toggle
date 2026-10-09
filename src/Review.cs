#if REVIEW
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// The surface tool: opens any window or popup by a stable id against stand-in controllers and a
/// throwaway data folder, to look at it, measure it or write it to a PNG. Compiled only into the
/// review build (<c>build.bat review</c>), never into what is published.
/// </summary>
internal static partial class Program
{
    /// <summary>
    /// Shows one surface standalone: "settings" (the gear panel, hotkey row forced visible
    /// regardless of the real setting), "error" (a sample <see cref="ErrorDialog"/>, no real
    /// exception involved), "update" (the update notice for a made-up version), "gear" (the real
    /// window and its settings panel together, to measure how the panel sits against the gear and
    /// the toggle button it opened from), or "tip" (the
    /// real effect list with its first hinted row's tooltip already showing, since the review
    /// harness cannot reliably hover this control). Interacting with the settings panel still
    /// saves to the real settings.json, same as it would from the main window - this is a shortcut
    /// to the real control, not a sandboxed mock of it.
    /// </summary>
    private static int RunReview(string surface, string argument)
    {
        if (surface is not ("settings" or "error" or "editor" or "layout" or "update" or "gear" or "tip"
            or "preview"))
        {
            WriteError("Usage: AuraToggle -review settings|error|editor|layout|update|gear|tip|preview|ids [scale%]");
            return 2;
        }

        ApplicationConfiguration.Initialize();

        // "preview light" and "preview dark" pick the theme outright rather than following
        // Windows, so both README screenshots can be taken on one machine without touching its
        // display settings. Every other surface reviews what a user would actually see.
        SystemColorMode colours = argument.Trim().ToLowerInvariant() switch
        {
            "light" when surface == "preview" => SystemColorMode.Classic,
            "dark" when surface == "preview" => SystemColorMode.Dark,
            _ => SystemColorMode.System,
        };
#pragma warning disable WFO5001 // colour mode support is still marked experimental
        Application.SetColorMode(colours);
#pragma warning restore WFO5001

        if (surface == "preview")
        {
            return ReviewPreview();
        }

        if (surface == "layout")
        {
            return ReviewLayout(argument);
        }

        if (surface == "gear")
        {
            return ReviewGear(argument);
        }

        Form? shown = null;

        if (surface == "editor")
        {
            var editor = new CustomPresetEditor(null, ReviewControllers(), AuraState.Load());
            editor.FormClosed += (_, _) => Application.Exit();
            editor.Open(new Point(60, 60), owner: null);
            shown = editor;
        }
        else if (surface == "error")
        {
            shown = ErrorDialog.Report(
                new IOException("Sample error for review - the LED controller did not answer in time."),
                "Review", owner: null, onClosed: Application.Exit, requireMessageLoop: false);
        }
        else if (surface == "update")
        {
            // A made-up version, no network access - this is the review surface, not a real check.
            var popup = new UpdatePopup("9.9.9");
            popup.FormClosed += (_, _) => Application.Exit();
            popup.Open(owner: null);
            shown = popup;
        }
        else if (surface == "tip")
        {
            // The real effect list and its real hints, so both the window and the rendered proof
            // below show what a user actually reads, not a placeholder string.
            var items = AuraPresets.All
                .Select(p => new SelectItem(p.Key, p.DisplayName, p.Mode, Hint: p.HintText))
                .ToList();
            var popup = new SelectPopup(items, items[0], Color.White, 260, Theme.Ui, 96)
            {
                KeepOpenOnDeactivate = true,
            };
            popup.FormClosed += (_, _) => Application.Exit();
            popup.Open(new Point(60, 60), owner: null, flipAbove: 0);

            popup.Shown += (_, _) =>
            {
                // A manually shown ToolTip only ever paints for the foreground window, which a
                // headless/automated session does not reliably hand a freshly opened one - shown
                // for anyone testing this by hand, but %TEMP%\aura-tip.png below is what actually
                // proves the colours: RenderTipForReview calls the exact same drawing code onto a
                // bitmap, with nothing about window focus in the way.
                popup.ShowTipForReview();

                using var bmp = new Bitmap(280, 40);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    popup.RenderTipForReview(g, new Rectangle(0, 0, 280, 40), items[0].Hint!);
                }

                bmp.Save(Path.Combine(Path.GetTempPath(), "aura-tip.png"), ImageFormat.Png);
            };
            shown = popup;
        }
        else
        {
            var popup = new SettingsPopup(AuraSettings.Load() with { HotkeyEnabled = true })
            {
                KeepOpenOnDeactivate = true,
            };
            popup.Location = new Point(
                (Screen.PrimaryScreen!.WorkingArea.Width - popup.Width) / 2,
                (Screen.PrimaryScreen.WorkingArea.Height - popup.Height) / 2);
            popup.FormClosed += (_, _) => Application.Exit();
            popup.Show();
            popup.Activate();
            shown = popup;
        }

        // These two carry no measurement report of their own - what matters for them is that a
        // display-scale change puts their spacing back at the new scale rather than leaving it as
        // the display they opened on had it, which the totals below make visible.
        Queue<int> scales = ParseScales(argument);
        if (shown != null)
        {
            var reports = new List<string>();
            void Report(string heading) => WriteReport(reports, heading, DescribeSpacing(shown));

            // Written even with no scale given - without this, plain "-review editor" left the
            // file untouched, and the anchoring proof below it never ran either.
            Report($"as opened, {shown.DeviceDpi * 100 / 96}%");

            if (scales.Count > 0)
            {
                MoveThroughScales(shown, scales, Report);
            }
        }

        Application.Run();
        return 0;
    }

    /// <summary>
    /// Every scaled distance a surface holds, added up, next to the size it fitted itself to. Two
    /// numbers rather than a layout report: for a panel of stacked rows, spacing that did not
    /// follow a display-scale change is exactly what this total shows.
    /// </summary>
    private static string DescribeSpacing(Form form)
    {
        static IEnumerable<Control> Tree(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;

                foreach (Control descendant in Tree(child))
                {
                    yield return descendant;
                }
            }
        }

        int margins = Tree(form).Sum(control => control.Margin.Horizontal + control.Margin.Vertical);

        string body = $"dpi           {form.DeviceDpi} ({form.DeviceDpi * 100 / 96}%)" + Environment.NewLine
            + $"clientsize    {form.ClientSize.Width}x{form.ClientSize.Height}" + Environment.NewLine
            + $"padding       {form.Padding.Left},{form.Padding.Top}" + Environment.NewLine
            + $"margins       {margins} px over {Tree(form).Count()} controls";

        // The editor's own pinned-header/pinned-buttons proof: spacing alone measures fine even
        // when a wrong Controls.Add order left the header drawn under the scrolling panel.
        if (form is CustomPresetEditor editor)
        {
            body += Environment.NewLine + editor.DescribeAnchoring();
        }

        return body;
    }

    /// <summary>
    /// One scale, or several separated by commas for a round trip (<c>-review layout 150,100</c>).
    /// Anything outside the range a display can actually be set to is ignored rather than
    /// rejected: this is a review switch, not a documented option.
    /// </summary>
    private static Queue<int> ParseScales(string argument) =>
        new(argument.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int scale) ? scale : 0)
            .Where(scale => scale is >= 100 and <= 400));

    /// <summary>
    /// Prints one measurement pass and keeps every pass so far in
    /// <c>%TEMP%\aura-layout.txt</c> - this is a WinExe, so when it is started by anything that
    /// owns no console (a scheduled task, a test runner) the console output goes nowhere.
    /// </summary>
    private static void WriteReport(List<string> reports, string heading, string body, string fileName = "aura-layout.txt")
    {
        string report = $"--- {heading} ---{Environment.NewLine}{body}";
        WriteLine(report);
        reports.Add(report);

        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), fileName),
                string.Join(Environment.NewLine, reports));
        }
        catch (Exception ex) when (AuraFiles.IsExpected(ex))
        {
            // A report that cannot be written is not worth failing the review over.
        }
    }

    /// <summary>
    /// Two controllers with three and two channels: enough to force the channel selector into the
    /// main window's top row - the widest that row ever gets, and so the case that overflows
    /// first - and enough to give the preset editor more than one block of rows.
    /// </summary>
    private static List<AuraDeviceSummary> ReviewControllers() => new()
    {
        new("review-1", "Aura Controller 1", new List<AuraChannel>
        {
            new(0, Onboard: true, Header: 0),
            new(1, Onboard: false, Header: 1),
            new(2, Onboard: false, Header: 2),
        }),
        new("review-2", "Aura Controller 2", new List<AuraChannel>
        {
            new(0, Onboard: true, Header: 0),
            new(1, Onboard: false, Header: 1),
        }),
    };

    /// <summary>
    /// The window as the README shows it: stand-in controllers, the state a first run starts on,
    /// and English regardless of this machine's language. Nothing stored is read and nothing is
    /// written, so the screenshot carries no trace of the machine it was taken on.
    /// </summary>
    private static int ReviewPreview()
    {
        Strings.Override = "en";
        AuraState.ReviewState = AuraState.Default;
        ToggleForm.ReviewDevices = ReviewControllers();

        Application.Run(new ToggleForm());
        return 0;
    }

    /// <summary>
    /// Opens the real window against stand-in controllers and prints what it measured, so a
    /// "cut off on the right" report can be reproduced and proved fixed at any display scale
    /// without a controller and without reading pixels off a screenshot.
    /// </summary>
    private static int ReviewLayout(string scaleArgument)
    {
        ToggleForm.ReviewDevices = ReviewControllers();

        var form = new ToggleForm();
        var reports = new List<string>();

        void Report(string heading) => WriteReport(reports, heading, form.DescribeLayout());

        // The size the window will first become visible at. Discovery may take longer than the
        // reveal backstop waits, so anything that changes between here and "as opened" is growth
        // the user gets to watch. Only the client size is read - measuring the controls would
        // force the handle open before Show() and change the very thing under review.
        WriteReport(reports, "before shown",
            $"clientsize    {form.ClientSize.Width}x{form.ClientSize.Height}");

        form.Shown += async (_, _) =>
        {
            // The window measures itself after discovery, which OnShown runs asynchronously -
            // reading straight away would report the sizes from before the channel selector
            // appeared, which is exactly the measurement in question.
            await Task.Yield();
            Report($"as opened, {form.DeviceDpi * 100 / 96}%");
            MoveThroughScales(form, ParseScales(scaleArgument), Report);

            // Left open on purpose: the numbers above say whether anything overflows, the window
            // itself is what shows how the result actually looks at this display scale.
        };

        Application.Run(form);
        return 0;
    }

    /// <summary>
    /// Opens the real window then its settings panel exactly as a gear click would, and prints the
    /// gear/toggle/panel geometry - the regression proof for the panel leaving a strip of the big
    /// button visible on its right, without a controller and without reading pixels off a
    /// screenshot.
    /// </summary>
    private static int ReviewGear(string scaleArgument)
    {
        ToggleForm.ReviewDevices = ReviewControllers();

        var form = new ToggleForm();
        var reports = new List<string>();

        void Report(string heading) => WriteReport(reports, heading, form.DescribeSettingsAnchor(), "aura-gear.txt");

        // Snaps the window flush against the screen's right edge - an Aero-snapped or dragged-to-
        // the-edge window, which is what actually triggers PopupForm.OnScreen's clamp and the
        // reported sliver of the toggle button staying visible next to the panel. Centred, the
        // panel never gets near the clamp and the report would read all zeroes. Anchored on the
        // client area's right edge - the same one the panel hangs from - rather than the window's
        // outer bounds, which include DWM's invisible resize border and were off by two dozen px in
        // practice; tracked through Resize because the window still has measuring left to do at this
        // point, same reason as ReviewLayout's first yield.
        async Task SnapAndReopenSettings(string heading)
        {
            form.CloseSettingsForReview();

            void SnapToRightEdge(object? s, EventArgs e)
            {
                Rectangle screen = Screen.FromControl(form).WorkingArea;
                int shift = screen.Right - form.RectangleToScreen(form.ClientRectangle).Right;
                form.Location = new Point(form.Location.X + shift, form.Location.Y);
            }

            SnapToRightEdge(null, EventArgs.Empty);
            form.Resize += SnapToRightEdge;
            await Task.Delay(300);
            form.Resize -= SnapToRightEdge;

            form.OpenSettingsForReview();
            await Task.Yield();
            Report(heading);

            // The panel is its own top-level window, so a simulated move only ever changes the main
            // window's scale - the panel keeps the real screen's dpi and never re-fits, which is
            // exactly the step that used to walk it out from under the gear on a second monitor.
            // Re-fitting it half again as wide stands in for that resize on a single screen.
            form.RefitSettingsForReview(150);
            await Task.Yield();
            Report($"{heading}, panel refitted 150 % wider");
        }

        form.Shown += async (_, _) =>
        {
            await Task.Yield();
            await SnapAndReopenSettings($"as opened, {form.DeviceDpi * 100 / 96}%, snapped to the right screen edge");

            // Each scale is a fresh gear click after the simulated move settles, not a read of the
            // panel that was already open - the panel is an owned top-level window, not a child of
            // the main one, so Windows never moves it along on its own and reading its stale
            // position would report a huge, unrelated misalignment instead of this bug.
            Queue<int> scales = ParseScales(scaleArgument);
            while (scales.Count > 0)
            {
                int scale = scales.Dequeue();
                var settled = new TaskCompletionSource();
                MoveToSimulatedScale(form, scale, _ => { }, () => settled.TrySetResult());
                await settled.Task;
                await SnapAndReopenSettings($"reopened at {scale}%, snapped to the right screen edge");
            }

            // Left open on purpose, same as ReviewLayout - the numbers above say whether the panel
            // clips the button, the window itself is what shows how it actually looks.
        };

        Application.Run(form);
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, ref Rect lParam);

    /// <summary>The two parameterless messages below take no rectangle, unlike WM_DPICHANGED.</summary>
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>Windows' "your window is now on a display at another scale" notification.</summary>
    private const uint DpiChangedMessage = 0x02E0;

    /// <summary>Windows' "the user just started/stopped dragging or resizing this window"
    /// notifications - sent here around <see cref="DpiChangedMessage"/> so a scale change mid-drag
    /// is reproduced without a second monitor (see <see cref="ToggleForm.SettleAfterDpiChange"/>).</summary>
    private const uint EnterSizeMoveMessage = 0x0231;

    private const uint ExitSizeMoveMessage = 0x0232;

    /// <summary>
    /// Walks the window through several scales in turn, so a round trip - out to the second
    /// monitor and back - is one run. A window that does not come back to the size it started at
    /// is exactly what the last report of the run says.
    /// </summary>
    private static void MoveThroughScales(Form form, Queue<int> scales, Action<string> report)
    {
        if (scales.Count == 0)
        {
            return;
        }

        MoveToSimulatedScale(form, scales.Dequeue(), report, () => MoveThroughScales(form, scales, report));
    }

    /// <summary>
    /// Puts the window through exactly what dragging it onto a second monitor at
    /// <paramref name="scale"/> percent does - the messages Windows itself sends, with the window
    /// rectangle it would suggest - and reports what the layout measured afterwards. The point of
    /// doing it this way: the process keeps the system dpi it started with, so the mismatch
    /// between window dpi and text dpi that only ever appeared on a real second monitor is
    /// reproduced on a single display, and a "cut off on the right" report becomes a number here
    /// instead of something only a second physical monitor could show. WM_DPICHANGED arrives while
    /// WM_ENTERSIZEMOVE is still in effect and WM_EXITSIZEMOVE does not follow until well after -
    /// a real drag between two monitors crosses the scale change with the mouse button still
    /// down, which is the case that used to wait for a release that had not happened yet.
    /// </summary>
    private static void MoveToSimulatedScale(Form form, int scale, Action<string> report, Action? next = null)
    {
        int target = 96 * scale / 100;
        int from = form.DeviceDpi;
        Rectangle bounds = form.Bounds;
        var suggested = new Rect
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Right = bounds.Left + bounds.Width * target / from,
            Bottom = bounds.Top + bounds.Height * target / from,
        };

        SendMessage(form.Handle, EnterSizeMoveMessage, IntPtr.Zero, IntPtr.Zero);
        SendMessage(form.Handle, DpiChangedMessage, new IntPtr((target << 16) | target), ref suggested);

        // The window re-measures itself on a timer after a scale change, so one reading straight
        // afterwards proves nothing; these cover every attempt it makes plus a margin. The
        // simulated drag stays "in progress" through the 600 ms pass, then releases - so the same
        // run proves both halves: the layout catching up while still dragging, and the final
        // settle once WM_EXITSIZEMOVE follows.
        var passes = new Queue<int>(new[] { 0, 250, 600, 1200, 2000 });
        bool dragging = true;
        var clock = new System.Windows.Forms.Timer { Interval = 50 };
        int elapsed = 0;
        clock.Tick += (_, _) =>
        {
            elapsed += clock.Interval;
            if (passes.Count == 0 || form.IsDisposed)
            {
                clock.Stop();
                clock.Dispose();

                if (!form.IsDisposed)
                {
                    next?.Invoke();
                }

                return;
            }

            if (elapsed >= passes.Peek())
            {
                int pass = passes.Dequeue();
                report($"{pass} ms after the move to {scale}% ({(dragging ? "dragging" : "released")})");

                if (dragging && pass == 600)
                {
                    dragging = false;
                    SendMessage(form.Handle, ExitSizeMoveMessage, IntPtr.Zero, IntPtr.Zero);
                }
            }
        };
        clock.Start();
    }

    /// <summary>Points the shot children at the parent's throwaway folder.</summary>
    private const string ReviewDataVariable = "AURA_REVIEW_DATA";

    private const string ReviewFolderPrefix = "aura-review-";

    /// <summary>
    /// Every surface the shot mode can open, along the path a user takes to it. Groups use names
    /// no id starts with, so a group can never be mistaken for a surface.
    /// </summary>
    private static readonly (string Id, string Description)[] ShotSurfaces =
    {
        ("window", "main window, two stand-in controllers"),
        ("window-nodevice", "main window, no controller found"),
        ("settings", "gear panel, opened from the main window"),
        ("settings-start", "gear panel with its start lighting list open"),
        ("effects", "effect list, opened from the main window"),
        ("editor", "new custom preset, opened from the main window"),
        ("error", "error dialog"),
        ("update", "update notice"),
    };

    private static readonly Dictionary<string, string[]> ShotGroups = new()
    {
        ["all"] = ShotSurfaces.Select(s => s.Id).ToArray(),
        ["popups"] = new[] { "settings", "settings-start", "effects", "editor", "error", "update" },
    };

    private static int RunSurfaceTool(string[] args)
    {
        UseThrowawayData();

        if (Normalise(args[0]) == "shot")
        {
            return args.Length >= 2 && args[1] == "--one"
                ? ShootOne(args)
                : RunShots(args.Skip(1).ToList());
        }

        string surface = args.Length >= 2 ? Normalise(args[1]) : "";
        if (surface == "ids")
        {
            return PrintSurfaceIds();
        }

        if (surface == "swap")
        {
            return args.Length >= 3 ? ReviewSwap(args[2]) : 2;
        }

        Strings.Override = AuraSettings.Load().Language;
        return RunReview(surface, args.Length >= 3 ? args[2] : "");
    }

    /// <summary>
    /// The portable self-update without GitHub: verifies a local file against its <c>.sig</c> with
    /// the same gate the download uses, then swaps this running exe for it and starts the new copy.
    /// The result goes to <c>%TEMP%\aura-swap.txt</c> as well, since this is a WinExe.
    /// </summary>
    private static int ReviewSwap(string file)
    {
        string result;
        int code;

        try
        {
            byte[] data = File.ReadAllBytes(file);
            string signature = File.Exists(file + ".sig") ? File.ReadAllText(file + ".sig") : "";

            if (!UpdateSignature.Verify(data, signature, UpdatePublicKey.Value))
            {
                (result, code) = ("not verified, nothing swapped", 1);
            }
            else if (!AuraUpdate.SwapPortable(new UpdateDownload(UpdateResult.Verified, data, Path.GetFileName(file))))
            {
                (result, code) = ("verified, swap failed", 3);
            }
            else
            {
                StartReplacement(RestartPath!);
                (result, code) = ("verified, swapped, restarted " + RestartPath, 0);
            }
        }
        catch (IOException ex)
        {
            (result, code) = ("cannot read " + file + ": " + ex.Message, 2);
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "aura-swap.txt"), result + Environment.NewLine);
        WriteLine(result);
        return code;
    }

    private static int PrintSurfaceIds()
    {
        WriteLine("Review surfaces (-review <id> [scale%]):");
        WriteLine("  settings  error  editor  layout  gear  tip  preview  swap <signed exe>");
        WriteLine("");
        WriteLine("Shot surfaces (-shot [id|group ...] [scale=100,150] [lang=en,de] [out=<folder>]):");
        foreach ((string id, string description) in ShotSurfaces)
        {
            WriteLine(Row(id, description));
        }

        WriteLine("");
        WriteLine("Groups:");
        foreach ((string group, string[] ids) in ShotGroups)
        {
            WriteLine(Row(group, string.Join(" ", ids)));
        }

        return 0;
    }

    /// <summary>
    /// Swaps the data folder for a throwaway one before anything reads it. A shot child reuses its
    /// parent's (named in <see cref="ReviewDataVariable"/>), and so can a test that seeds its own;
    /// otherwise a fresh folder is seeded with sample data and removed again on exit.
    /// </summary>
    private static void UseThrowawayData()
    {
        string? given = Environment.GetEnvironmentVariable(ReviewDataVariable);
        if (!string.IsNullOrEmpty(given))
        {
            Directory.CreateDirectory(given);
            AuraFiles.UseReviewFolder(given);
            return;
        }

        RemoveStaleReviewFolders();

        string folder = Path.Combine(Path.GetTempPath(),
            $"{ReviewFolderPrefix}{Environment.ProcessId}-{Environment.TickCount64}");
        Directory.CreateDirectory(folder);
        AuraFiles.UseReviewFolder(folder);
        Environment.SetEnvironmentVariable(ReviewDataVariable, folder);
        SeedSampleData();

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (AuraFiles.IsExpected(ex))
            {
                // Picked up by the stale sweep of a later run.
            }
        };
    }

    /// <summary>Folders a killed run could not delete itself, once they are a day old.</summary>
    private static void RemoveStaleReviewFolders()
    {
        try
        {
            foreach (string stale in Directory.EnumerateDirectories(Path.GetTempPath(), ReviewFolderPrefix + "*"))
            {
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(stale) > TimeSpan.FromDays(1))
                {
                    Directory.Delete(stale, recursive: true);
                }
            }
        }
        catch (Exception ex) when (AuraFiles.IsExpected(ex))
        {
            // Nothing depends on the sweep.
        }
    }

    /// <summary>
    /// Representative data, written through the tool's own writers: two presets on the stand-in
    /// controllers, one of them the start lighting, so every list and every optional row has
    /// something to show.
    /// </summary>
    private static void SeedSampleData()
    {
        AuraCustomPresets.Save(new List<CustomPreset>
        {
            new("Desk", new List<CustomPresetEntry>
            {
                new("review-1", 0, "Onboard", AuraState.ModeStatic, 0x8A, 0x4D, 0xFF, 100),
                new("review-1", 1, "ARGB 1", 0x02, 0x3D, 0x7B, 0xFF, 100),
                new("review-1", 2, "ARGB 2", AuraState.ModeStatic, 0x00, 0xC8, 0xC8, 80),
                new("review-2", 0, "Onboard", AuraState.ModeStatic, 0x8A, 0x4D, 0xFF, 100),
            }),
            new("Night", new List<CustomPresetEntry>
            {
                new("review-1", -1, "Aura Controller 1", AuraState.ModeStatic, 0xC0, 0x20, 0x10, 30),
                new("review-2", -1, "Aura Controller 2", AuraState.ModeOff, 0, 0, 0),
            }),
        });

        (AuraState.Default with { Mode = AuraState.ModeStatic, Red = 0x8A, Green = 0x4D, Blue = 0xFF }).Save();

        (AuraSettings.Default with
        {
            StartAction = AuraSettings.CustomStartAction("Desk"),
            CloseAfterStart = true,
            HotkeyEnabled = true,
        }).Save();
    }

    /// <summary>
    /// Shot mode: one child process per surface, scale and language, so each picture starts from
    /// a clean window and its language is set before the first string is read. Writes the PNGs
    /// and a <c>shots.txt</c> list; fails only when a picture could not be produced, never over
    /// what one shows.
    /// </summary>
    private static int RunShots(List<string> args)
    {
        var ids = new List<string>();
        List<int> scales = new() { 100, 150 };
        List<string> languages = new() { "en", "de" };
        string? outDir = null;

        foreach (string raw in args)
        {
            string arg = raw.Trim();
            string lower = arg.ToLowerInvariant();

            if (lower == "ids")
            {
                return PrintSurfaceIds();
            }

            if (lower.StartsWith("scale=", StringComparison.Ordinal))
            {
                scales = ParseScales(arg["scale=".Length..]).ToList();
            }
            else if (lower.StartsWith("lang=", StringComparison.Ordinal))
            {
                languages = lower["lang=".Length..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(code => Strings.Codes.Contains(code))
                    .ToList();
            }
            else if (lower.StartsWith("out=", StringComparison.Ordinal))
            {
                outDir = arg["out=".Length..];
            }
            else if (ShotGroups.TryGetValue(lower, out string[]? group))
            {
                ids.AddRange(group);
            }
            else if (ShotSurfaces.Any(s => s.Id == lower))
            {
                ids.Add(lower);
            }
            else
            {
                WriteError($"Unknown surface or option \"{arg}\".");
                PrintSurfaceIds();
                return 2;
            }
        }

        if (ids.Count == 0)
        {
            ids.AddRange(ShotGroups["all"]);
        }

        if (scales.Count == 0 || languages.Count == 0)
        {
            WriteError("scale= takes 100-400, lang= takes " + string.Join(",", Strings.Codes) + ".");
            return 2;
        }

        outDir = Path.GetFullPath(outDir ?? DefaultShotFolder());
        Directory.CreateDirectory(outDir);

        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
        var manifest = new List<string>();
        int failures = 0;

        foreach (string id in ids.Distinct())
        {
            foreach (int scale in scales)
            {
                foreach (string language in languages)
                {
                    string file = Path.Combine(outDir, $"{id}-{scale}-{language}.png");
                    File.Delete(file);

                    var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
                    foreach (string part in new[]
                             { "-shot", "--one", id, scale.ToString(CultureInfo.InvariantCulture), language, file })
                    {
                        start.ArgumentList.Add(part);
                    }

                    using var child = System.Diagnostics.Process.Start(start)!;
                    bool finished = child.WaitForExit(30000);
                    if (!finished)
                    {
                        child.Kill(entireProcessTree: true);
                    }

                    string line;
                    if (finished && child.ExitCode == 0 && File.Exists(file))
                    {
                        using var image = Image.FromFile(file);
                        line = $"ok    {Path.GetFileName(file)}  {image.Width}x{image.Height}";
                    }
                    else
                    {
                        failures++;
                        line = $"FAIL  {Path.GetFileName(file)}  {(finished ? $"exit {child.ExitCode}" : "timed out")}";
                    }

                    manifest.Add(line);
                    WriteLine(line);
                }
            }
        }

        File.WriteAllLines(Path.Combine(outDir, "shots.txt"), manifest);
        WriteLine($"{manifest.Count - failures} of {manifest.Count} written to {outDir}");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>The project's .tmp\shots when run from a build inside the checkout, else next to
    /// the executable.</summary>
    private static string DefaultShotFolder()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AuraToggle.csproj")))
            {
                return Path.Combine(dir.FullName, ".tmp", "shots");
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "shots");
    }

    /// <summary>
    /// One picture: opens the surface along its real path, lets the window settle (and, for a
    /// scale other than the display's, go through the simulated monitor move), then captures
    /// every window of the surface into one PNG.
    /// </summary>
    private static int ShootOne(string[] args)
    {
        if (args.Length < 6 || !int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int scale))
        {
            return 2;
        }

        string id = args[2];
        Strings.Override = args[4];
        string file = args[5];

        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // colour mode support is still marked experimental
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001

        int result = 1;

        async Task Settle(Form scaled)
        {
            await Task.Delay(1500);
            if (scale != scaled.DeviceDpi * 100 / 96)
            {
                var settled = new TaskCompletionSource();
                MoveToSimulatedScale(scaled, scale, _ => { }, () => settled.TrySetResult());
                await settled.Task;
            }
        }

        async Task Capture()
        {
            await Task.Delay(800);
            try
            {
                using Bitmap shot = CaptureSurface();
                shot.Save(file, ImageFormat.Png);
                result = 0;
            }
            finally
            {
                Application.Exit();
            }
        }

        if (id == "error")
        {
            if (ErrorDialog.Report(
                    new IOException("Sample error for review - the LED controller did not answer in time."),
                    "Review", owner: null, onClosed: () => { }, requireMessageLoop: false) is not Form dialog)
            {
                return 1;
            }

            dialog.Shown += async (_, _) =>
            {
                await Settle(dialog);
                await Capture();
            };
            Application.Run();
            return result;
        }

        if (id == "update")
        {
            // A made-up version, no network access - the popup alone, the way the error dialog is.
            var notice = new UpdatePopup("9.9.9");
            notice.Shown += async (_, _) =>
            {
                await Settle(notice);
                await Capture();
            };
            notice.Open(owner: null);
            Application.Run();
            return result;
        }

        ToggleForm.ReviewDevices = id == "window-nodevice" ? new List<AuraDeviceSummary>() : ReviewControllers();
        var form = new ToggleForm();
        form.Shown += async (_, _) =>
        {
            await Settle(form);

            switch (id)
            {
                case "settings":
                    form.OpenSettingsForReview();
                    break;
                case "settings-start":
                    form.OpenStartActionsForReview();
                    break;
                case "effects":
                    form.OpenEffectsForReview();
                    break;
                case "editor":
                    form.OpenEditorForReview();
                    break;
            }

            await Capture();
        };

        Application.Run(form);
        return result;
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr hdc, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);

    /// <summary>The frame as drawn, without the invisible resize border around it.</summary>
    private const int ExtendedFrameBounds = 9;

    /// <summary>
    /// Where a window is actually drawn. <see cref="Control.Bounds"/> includes DWM's invisible
    /// resize border, which renders as a black band once another window sits next to it.
    /// </summary>
    private static Rectangle VisibleBounds(Form form)
    {
        if (DwmGetWindowAttribute(form.Handle, ExtendedFrameBounds, out Rect frame, Marshal.SizeOf<Rect>()) == 0 &&
            frame.Right > frame.Left && frame.Bottom > frame.Top)
        {
            return Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
        }

        return form.Bounds;
    }

    /// <summary>
    /// <see cref="VisibleBounds"/> cut down to what WinForms actually paints. At a simulated scale
    /// the window rectangle is larger than its client area plus the frame (the non-client part
    /// stays at the real dpi), and the surplus comes out of PrintWindow as black rows at the
    /// bottom and black columns on the right. The frame is as thick on the right and bottom as it
    /// is on the left, so the painted edge is the client edge plus that thickness.
    /// </summary>
    private static Rectangle PaintedBounds(Form form)
    {
        Rectangle visible = VisibleBounds(form);
        Point client = form.PointToScreen(Point.Empty);
        int border = Math.Max(0, client.X - visible.Left);
        int right = Math.Min(visible.Right, client.X + form.ClientSize.Width + border);
        int bottom = Math.Min(visible.Bottom, client.Y + form.ClientSize.Height + border);
        return Rectangle.FromLTRB(visible.Left, visible.Top, right, bottom);
    }

    /// <summary>Asks DWM for the window's real pixels, which a plain WM_PRINT misses for
    /// layered and composited windows.</summary>
    private const uint RenderFullContent = 2;

    /// <summary>
    /// Every visible window of the process, each rendered on its own and laid out where it sits on
    /// screen - so popups land over the window they opened from, and nothing that happens to be in
    /// front of them on the desktop ends up in the picture.
    /// </summary>
    private static Bitmap CaptureSurface()
    {
        List<Form> forms = Application.OpenForms.Cast<Form>()
            .Where(f => f.Visible && f.Opacity > 0 && f.Width > 0 && f.Height > 0)
            .ToList();
        if (forms.Count == 0)
        {
            throw new InvalidOperationException("nothing visible to capture");
        }

        Rectangle union = forms.Select(PaintedBounds).Aggregate(Rectangle.Union);
        var shot = new Bitmap(union.Width, union.Height, PixelFormat.Format32bppArgb);
        using Graphics canvas = Graphics.FromImage(shot);
        canvas.Clear(Color.Transparent);

        // Creation order: the main window first, then what was opened from it on top.
        foreach (Form form in forms)
        {
            using var layer = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(layer))
            {
                IntPtr hdc = g.GetHdc();
                try
                {
                    PrintWindow(form.Handle, hdc, RenderFullContent);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }

            Rectangle visible = PaintedBounds(form);
            canvas.DrawImage(layer,
                new Rectangle(visible.Left - union.Left, visible.Top - union.Top, visible.Width, visible.Height),
                new Rectangle(visible.Left - form.Left, visible.Top - form.Top, visible.Width, visible.Height),
                GraphicsUnit.Pixel);
        }

        return shot;
    }
}
#endif
