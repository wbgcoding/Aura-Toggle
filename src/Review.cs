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
    /// exception involved), "gear" (the real
    /// window and its settings panel together, to measure how the panel sits against the gear and
    /// the toggle button it opened from), or "tip" (the
    /// real effect list with its first hinted row's tooltip already showing, since the review
    /// harness cannot reliably hover this control). Interacting with the settings panel still
    /// saves to the real settings.json, same as it would from the main window - this is a shortcut
    /// to the real control, not a sandboxed mock of it.
    /// </summary>
    private static int RunReview(string surface, string argument)
    {
        if (surface is not ("settings" or "error" or "editor" or "rename" or "layout" or "gear"
            or "tip" or "preview"))
        {
            WriteError("Usage: AuraToggle -review settings|error|editor|rename|layout|gear|tip|preview|engine|ids [scale%]");
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
        else if (surface == "rename")
        {
            var rename = new RenamePopup("Sample channel name");
            rename.FormClosed += (_, _) => Application.Exit();
            rename.Open(new Point(60, 60), owner: null);
            shown = rename;
        }
        else if (surface == "error")
        {
            shown = ErrorDialog.Report(
                new IOException("Sample error for review - the LED controller did not answer in time."),
                "Review", owner: null, onClosed: Application.Exit, requireMessageLoop: false);
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
                // %TEMP%\aura-tip.png below is the tip's face drawn by the same code onto a bitmap,
                // checkable without capturing the tip window. Shown a while after the list, as a hover does, so a recording of the screen can
                // tell the tooltip's own first frames apart from the list appearing.
                var later = new System.Windows.Forms.Timer { Interval = 1500 };
                later.Tick += (_, _) =>
                {
                    later.Dispose();
                    popup.ShowTipForReview();
                };
                later.Start();

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

        List<string> clipped = FindClipping(form);
        body += Environment.NewLine + (clipped.Count == 0
            ? "clipping      none"
            : "clipping      " + string.Join(Environment.NewLine + "              ", clipped));

        // The editor's own pinned-header/pinned-buttons proof: spacing alone measures fine even
        // when a wrong Controls.Add order left the header drawn under the scrolling panel.
        if (form is CustomPresetEditor editor)
        {
            body += Environment.NewLine + editor.DescribeAnchoring();
        }

        return body;
    }

    /// <summary>
    /// Every visible control that sticks out of the control it sits in or covers part of a visible
    /// sibling, and every button narrower than its own label. A control sitting over the lower part
    /// of a text field measures fine on every spacing total, which is how a rename field cut off
    /// on a scaled display went unnoticed; this names it. Children of a scrolling panel are only
    /// held to its width, since running past its bottom is what the scrolling is for.
    /// </summary>
    internal static List<string> FindClipping(Form form)
    {
        var found = new List<string>();

        static string Name(Control control)
        {
            string label = control.AccessibleName ?? control.Text;
            return label.Length == 0 ? control.GetType().Name
                : $"{control.GetType().Name} \"{(label.Length > 24 ? label[..24] : label)}\"";
        }

        void Walk(Control parent)
        {
            List<Control> children = parent.Controls.Cast<Control>()
                .Where(c => c.Visible && c.Width > 0 && c.Height > 0)
                .ToList();
            bool scrolls = parent is ScrollableControl { AutoScroll: true };
            Rectangle room = parent.ClientRectangle;

            foreach (Control child in children)
            {
                Rectangle bounds = child.Bounds;
                bool outside = scrolls
                    ? bounds.Left < 0 || bounds.Right > room.Width
                    : !room.Contains(bounds);

                if (outside)
                {
                    found.Add($"{Name(child)} {bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height} "
                        + $"outside {Name(parent)} {room.Width}x{room.Height}");
                }

                // EM_POSFROMCHAR for the first character: left of the box means the text is
                // scrolled, and with the whole text fitting that only hides its start.
                if (child is TextBox box && box.IsHandleCreated && box.TextLength > 0 &&
                    TextRenderer.MeasureText(box.Text, box.Font).Width < box.ClientSize.Width &&
                    (short)(SendMessage(box.Handle, 0x00D6, IntPtr.Zero, IntPtr.Zero).ToInt64() & 0xFFFF) < 0)
                {
                    found.Add($"{Name(child)} scrolled although its text fits");
                }

                if (child is PillButton button && Theme.MeasuredWidth(button.Text, button.Font) > button.Width)
                {
                    found.Add($"{Name(child)} label {Theme.MeasuredWidth(button.Text, button.Font)} px "
                        + $"in {button.Width} px");
                }

                Walk(child);
            }

            for (int i = 0; i < children.Count; i++)
            {
                for (int j = i + 1; j < children.Count; j++)
                {
                    Rectangle overlap = Rectangle.Intersect(children[i].Bounds, children[j].Bounds);
                    if (overlap.Width > 0 && overlap.Height > 0)
                    {
                        found.Add($"{Name(children[i])} overlaps {Name(children[j])} by "
                            + $"{overlap.Width}x{overlap.Height}");
                    }
                }
            }
        }

        Walk(form);
        return found;
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
            // exactly the step that walks it out from under the gear on a second monitor.
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
    /// down, so the layout must not wait for a release that has not happened yet.
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
        ("channels", "channel list, opened from the main window"),
        ("tray", "notification area menu with its effect list open, shown over the main window"),
        ("editor", "new custom preset, opened from the main window"),
        ("rename", "channel rename, opened from the main window"),
        ("colour", "colour picker, opened from the custom colour chip"),
        ("error", "error dialog"),
    };

    private static readonly Dictionary<string, string[]> ShotGroups = new()
    {
        ["all"] = ShotSurfaces.Select(s => s.Id).ToArray(),
        ["popups"] = new[] { "settings", "settings-start", "effects", "channels", "tray", "editor", "rename", "colour", "error" },
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

        if (surface == "follow")
        {
            return ReviewFollow();
        }

        if (surface == "engine")
        {
            return ReviewEngine();
        }

        Strings.Override = AuraSettings.Load().Language;
        return RunReview(surface, args.Length >= 3 ? args[2] : "");
    }

    /// <summary>
    /// The start lighting following its preset, against the seeded sample data: a rename of
    /// another preset leaves it alone, a rename of its own preset carries it along, deleting that
    /// preset falls back to leaving the lighting unchanged. Printed and written to
    /// <c>%TEMP%\aura-follow.txt</c>.
    /// </summary>
    private static int ReviewFollow()
    {
        var lines = new List<string>();
        bool ok = true;

        void Step(string what, Action act, string expected)
        {
            act();
            string actual = AuraSettings.Load().StartAction;
            ok &= actual == expected;
            lines.Add($"{(actual == expected ? "ok  " : "FAIL")} {what}: start action \"{actual}\" (expected \"{expected}\")");
        }

        Step("seeded", () => { }, AuraSettings.CustomStartAction("Desk"));
        Step("rename another preset", () => AuraSettings.FollowPresetRename("Night", "Late"), AuraSettings.CustomStartAction("Desk"));
        Step("rename its preset", () => AuraSettings.FollowPresetRename("Desk", "Work"), AuraSettings.CustomStartAction("Work"));
        Step("delete another preset", () => AuraSettings.FollowPresetRename("Late", null), AuraSettings.CustomStartAction("Work"));
        Step("delete its preset", () => AuraSettings.FollowPresetRename("Work", null), AuraSettings.StartActionNone);

        string report = string.Join(Environment.NewLine, lines);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "aura-follow.txt"), report + Environment.NewLine);
        WriteLine(report);
        return ok ? 0 : 1;
    }

    /// <summary>
    /// "engine": fixed cases against the pure half of the firmware-engine sharing (what
    /// <see cref="MixDevice"/> and <see cref="ShareEngine"/> work out) and against the preset
    /// editor's row sync. No controller is touched and no window is shown. PASS/FAIL per case,
    /// also written to %TEMP%\aura-engine.txt; exit 0 when all pass, 1 otherwise.
    /// </summary>
    /// <summary>
    /// The tooltip after its list is gone: a mouse move queued for a closing list reached a
    /// disposed tip window and took the app down, and so did a tip window closed from outside.
    /// </summary>
    private static void ReviewTipCases(Action<string, string, string> check)
    {
        static string Outcome(Action action)
        {
            try
            {
                action();
                return "ok";
            }
            catch (Exception e)
            {
                return e.GetType().Name;
            }
        }

        using var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
        host.Show();

        // The list closes when it loses the activation, so the tip must never take it.
        int deactivated = 0;
        host.Activate();
        Application.DoEvents();
        host.Deactivate += (_, _) => deactivated++;

        var tip = new ThemedTip();
        check("tip: shows", "ok", Outcome(() => tip.Show("tip", host, 4, 4, 1000)));
        Application.DoEvents();
        check("tip: owner keeps the activation", "0", deactivated.ToString(CultureInfo.InvariantCulture));
        check("tip: on screen", "True", tip.ShowingForReview.ToString());
        host.Hide();
        Application.DoEvents();
        check("tip: hides with its window", "False", tip.ShowingForReview.ToString());
        host.Show();
        Application.DoEvents();
        tip.Dispose();
        check("tip: show after dispose", "ok", Outcome(() => tip.Show("tip", host, 4, 4, 1000)));
        check("tip: hide after dispose", "ok", Outcome(() => tip.Hide(host)));
        check("tip: dispose twice", "ok", Outcome(tip.Dispose));

        using var live = new ThemedTip();
        live.Show("tip", host, 4, 4, 1000);
        foreach (Form form in Application.OpenForms.Cast<Form>().Where(f => f != host).ToList())
        {
            form.Close();
        }

        check("tip: show after its window closed", "ok", Outcome(() => live.Show("tip", host, 4, 4, 1000)));
    }

    private static int ReviewEngine()
    {
        ApplicationConfiguration.Initialize();

        var lines = new List<string>();
        int failed = 0;

        void Check(string name, string expected, string actual)
        {
            bool ok = expected == actual;
            failed += ok ? 0 : 1;
            lines.Add(ok ? $"PASS {name}" : $"FAIL {name}: expected {expected}, got {actual}");
        }

        const byte Static = 1, Chase = 9, Rainbow = 5, Wave = 11;
        var state = new AuraState(On: true, Static, 255, 255, 255, "", 100);
        IReadOnlyList<int> channels = new[] { 0, 1, 2 };

        // Modes sent, in channel order, e.g. "5,5,5".
        static string Sent(DeviceMix mix) => string.Join(",", mix.Looks.OrderBy(l => l.Channel).Select(l => l.Mode));
        // Records to write, e.g. "0:5 1:5" (channel:mode, plus "off" for a record that stays off).
        static string Recorded(DeviceMix mix) => string.Join(" ", mix.Records.OrderBy(r => r.Channel)
            .Select(r => $"{r.Channel}:{r.Look.Mode}{(r.Look.On ? "" : "off")}"));

        ChannelLighting Colour(byte mode, bool on = true) => new(mode, 0, 0, 255, on);
        var pickRainbow = new ChannelLighting(Rainbow, 255, 255, 255);

        // Static channels, rainbow picked on channel 2: every channel takes it.
        var statics = new Dictionary<int, ChannelLighting>
        {
            [0] = Colour(Static), [1] = Colour(Static), [2] = Colour(Static),
        };
        DeviceMix mix = MixDevice(channels, i => statics.GetValueOrDefault(i), 2, state, pickRainbow, null, false);
        Check("rainbow on 2 takes every channel: sent", "5,5,5", Sent(mix));
        Check("rainbow on 2 takes every channel: records", "0:5 1:5", Recorded(mix));
        Check("rainbow on 2: named", "2", string.Join(",", mix.Named));

        // A record that is off stays dark, its record carries the mode and stays off.
        var withOff = new Dictionary<int, ChannelLighting>
        {
            [0] = Colour(Static, on: false), [1] = Colour(Static), [2] = Colour(Static),
        };
        mix = MixDevice(channels, i => withOff.GetValueOrDefault(i), 2, state, pickRainbow, null, false);
        Check("off channel stays dark: sent", "0,5,5", Sent(mix));
        Check("off channel stays dark: records", "0:5off 1:5", Recorded(mix));

        // A channel with no record is written too and gets a record.
        var noRecord = new Dictionary<int, ChannelLighting> { [1] = Colour(Static), [2] = Colour(Static) };
        mix = MixDevice(channels, i => noRecord.GetValueOrDefault(i), 2, state, pickRainbow, null, false);
        Check("no-record channel joins: sent", "5,5,5", Sent(mix));
        Check("no-record channel joins: records", "0:5 1:5", Recorded(mix));

        // Without a firmware pick a channel with no record is left alone.
        mix = MixDevice(channels, i => noRecord.GetValueOrDefault(i), 2, state, Colour(Chase), null, false);
        Check("colour pick leaves a no-record channel alone: sent", "1,9", Sent(mix));

        // Then static on channel 1 changes only channel 1.
        var rainbows = new Dictionary<int, ChannelLighting>
        {
            [0] = Colour(Rainbow), [1] = Colour(Rainbow), [2] = Colour(Rainbow),
        };
        mix = MixDevice(channels, i => rainbows.GetValueOrDefault(i), 1, state, Colour(Static), null, false);
        Check("static on 1 only changes channel 1: sent", "5,1,5", Sent(mix));
        Check("static on 1 only changes channel 1: records", "", Recorded(mix));

        // A brightness change must not turn colour channels into the engine's mode.
        var mixed = new Dictionary<int, ChannelLighting>
        {
            [0] = Colour(Rainbow), [1] = Colour(Static), [2] = Colour(Static),
        };
        mix = MixDevice(channels, i => mixed.GetValueOrDefault(i), -1, state, null, 50, keepPower: true);
        Check("brightness change keeps colour channels: sent", "5,1,1", Sent(mix));

        // Preset: wave on channel 0 and rainbow on channel 2 -> both wave, static channel 1 stays.
        var entries = new[]
        {
            new EngineChannel(0, true, null), new EngineChannel(1, true, null), new EngineChannel(2, true, null),
        };
        var looks = new[]
        {
            new ChannelLook(0, Wave, 0, 0, 0), new ChannelLook(1, Static, 0, 0, 255),
            new ChannelLook(2, Rainbow, 0, 0, 0),
        };
        EngineShare share = ShareEngine(entries, looks, spreadToAll: false, new ChannelLighting(Static, 255, 255, 255), 100);
        Check("preset wave on 0, rainbow on 2: sent", "11,1,11", string.Join(",", share.Looks.Select(l => l.Mode)));
        Check("preset wave on 0, rainbow on 2: records", "2:11", string.Join(" ", share.Records.Select(r => $"{r.Channel}:{r.Look.Mode}")));

        // Lowest channel decides, not the order the entries came in.
        var reversed = new[] { looks[2], looks[1], looks[0] };
        share = ShareEngine(entries, reversed, spreadToAll: false, new ChannelLighting(Static, 255, 255, 255), 100);
        Check("preset order does not matter: sent", "11,1,11", string.Join(",", share.Looks.Select(l => l.Mode)));

        // Editor: a firmware pick takes the same controller along, the other controller stays.
        var editor = new CustomPresetEditor(null, ReviewControllers(), AuraState.Load());
        string Rows() => string.Join(" ", editor.ReviewRows().Select(r => $"{r.DeviceKey}/{r.Channel}={r.Effect}"));
        for (int i = 0; i < 5; i++)
        {
            editor.ReviewPick(i, "static");
        }

        editor.ReviewPick(1, "rainbow");
        Check("editor: rainbow row takes its controller",
            "review-1/0=rainbow review-1/1=rainbow review-1/2=rainbow review-2/0=static review-2/1=static", Rows());

        editor.ReviewPick(2, "chase");
        Check("editor: colour pick changes only its row",
            "review-1/0=rainbow review-1/1=rainbow review-1/2=chase review-2/0=static review-2/1=static", Rows());
        editor.Dispose();

        // Editor opened on a preset whose rows carry differing firmware modes: shown normalised.
        var saved = new CustomPreset("engine-review", new List<CustomPresetEntry>
        {
            new("review-1", 0, "Onboard", Wave, 0, 0, 0),
            new("review-1", 1, "ARGB 1", Static, 0, 0, 255, 100),
            new("review-1", 2, "ARGB 2", Rainbow, 0, 0, 0),
            new("review-2", 0, "Onboard", Rainbow, 0, 0, 0),
            new("review-2", 1, "ARGB 1", Wave, 0, 0, 0),
        });
        editor = new CustomPresetEditor(saved, ReviewControllers(), AuraState.Load());
        Check("editor: opened preset is normalised",
            "review-1/0=wave review-1/1=static review-1/2=wave review-2/0=rainbow review-2/1=rainbow",
            string.Join(" ", editor.ReviewRows().Select(r => $"{r.DeviceKey}/{r.Channel}={r.Effect}")));
        editor.Dispose();

        ReviewCoreCases(Check);
        ReviewUiCases(Check);
        ReviewTipCases(Check);

        lines.Add(failed == 0 ? "engine: all passed" : $"engine: {failed} failed");
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "aura-engine.txt"), lines);
        foreach (string line in lines)
        {
            WriteLine(line);
        }

        return failed == 0 ? 0 : 1;
    }

    private static int PrintSurfaceIds()
    {
        WriteLine("Review surfaces (-review <id> [scale%]):");
        WriteLine("  settings  error  editor  rename  layout  gear  tip  preview  follow  engine");
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

                    string clipFile = Path.ChangeExtension(file, ".clip.txt");
                    string[] clipped = File.Exists(clipFile) ? File.ReadAllLines(clipFile) : Array.Empty<string>();
                    File.Delete(clipFile);

                    string line;
                    if (finished && child.ExitCode == 0 && File.Exists(file) && clipped.Length > 0)
                    {
                        failures++;
                        line = $"CLIP  {Path.GetFileName(file)}  " + string.Join(" | ", clipped);
                    }
                    else if (finished && child.ExitCode == 0 && File.Exists(file))
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
            // AURA_SHOT_WAIT holds the window this many seconds longer before the surface opens: time
            // to change the display scale while the process runs, which leaves it measuring text at
            // the scale it started with - the mixed state a second monitor at another scale puts a
            // user in, and the one case the simulated move cannot produce for a freshly opened popup.
            int wait = int.TryParse(Environment.GetEnvironmentVariable("AURA_SHOT_WAIT"), out int seconds)
                ? Math.Clamp(seconds, 0, 120) * 1000 : 0;
            await Task.Delay(1500 + wait);
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

                List<string> clipped = Application.OpenForms.Cast<Form>()
                    .Where(f => f.Visible)
                    .SelectMany(f => FindClipping(f).Select(line => $"{f.GetType().Name}: {line}"))
                    .ToList();

                // The settings panel's own placement proof: a panel that fits but sits over the
                // gear it opened from measures clean above. Only where the panel and the window
                // share a scale - after a simulated move the panel keeps the real display's, a
                // mix no user ever sees, and its edge then follows the window's late re-measures.
                if (id.StartsWith("settings", StringComparison.Ordinal) &&
                    Application.OpenForms.OfType<ToggleForm>().FirstOrDefault() is ToggleForm owner &&
                    Application.OpenForms.OfType<SettingsPopup>().FirstOrDefault()?.DeviceDpi == owner.DeviceDpi)
                {
                    clipped.AddRange(owner.DescribeSettingsAnchor()
                        .Split(Environment.NewLine)
                        .Where(line => line.StartsWith("COVERS", StringComparison.Ordinal) ||
                            line.StartsWith("OVERFLOW", StringComparison.Ordinal) ||
                            line.StartsWith("CLIPPED", StringComparison.Ordinal))
                        .Select(line => "SettingsPopup: " + line));
                    File.WriteAllText(Path.ChangeExtension(file, ".anchor.txt"), owner.DescribeSettingsAnchor());
                }
                if (clipped.Count > 0)
                {
                    File.WriteAllLines(Path.ChangeExtension(file, ".clip.txt"), clipped);
                }

                result = 0;
            }
            finally
            {
                Application.Exit();
            }
        }

        if (id == "error")
        {
            void OpenAlone()
            {
                if (ErrorDialog.Report(
                        new IOException("Sample error for review - the LED controller did not answer in time."),
                        "Review", owner: null, onClosed: () => { }, requireMessageLoop: false) is Form dialog)
                {
                    dialog.Shown += async (_, _) =>
                    {
                        await Settle(dialog);
                        await Capture();
                    };
                }
                else
                {
                    Application.Exit();
                }
            }

            // This one has no window to open from, so with AURA_SHOT_WAIT set it is created
            // only once the wait is over - after the display scale changed, like an error raised
            // while the main window sits on a second monitor.
            if (int.TryParse(Environment.GetEnvironmentVariable("AURA_SHOT_WAIT"), out int held) && held > 0)
            {
                var later = new System.Windows.Forms.Timer { Interval = Math.Clamp(held, 1, 120) * 1000 };
                later.Tick += (_, _) =>
                {
                    later.Stop();
                    later.Dispose();
                    OpenAlone();
                };
                later.Start();
            }
            else
            {
                OpenAlone();
            }

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
                case "channels":
                    form.OpenChannelsForReview();
                    break;
                case "tray":
                    form.OpenTrayMenuForReview();
                    break;
                case "editor":
                    form.OpenEditorForReview();
                    break;
                case "rename":
                    form.OpenRenameForReview();
                    break;
                case "colour":
                    form.OpenColourPickerForReview();
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
    private static Rectangle CapturedBounds(Control control) =>
        control is Form form ? PaintedBounds(form) : control.Bounds;

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
        // Open menus are windows of their own but not forms; the tray menu is the one there is.
        List<Control> forms = Application.OpenForms.Cast<Form>()
            .Where(f => f.Visible && f.Opacity > 0 && f.Width > 0 && f.Height > 0)
            .Cast<Control>()
            .Concat(ToggleForm.ReviewMenus.Where(m => m.Visible))
            .ToList();
        if (forms.Count == 0)
        {
            throw new InvalidOperationException("nothing visible to capture");
        }

        Rectangle union = forms.Select(CapturedBounds).Aggregate(Rectangle.Union);
        var shot = new Bitmap(union.Width, union.Height, PixelFormat.Format32bppArgb);
        using Graphics canvas = Graphics.FromImage(shot);
        canvas.Clear(Color.Transparent);

        // Creation order: the main window first, then what was opened from it on top.
        foreach (Control form in forms)
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

            Rectangle visible = CapturedBounds(form);
            canvas.DrawImage(layer,
                new Rectangle(visible.Left - union.Left, visible.Top - union.Top, visible.Width, visible.Height),
                new Rectangle(visible.Left - form.Left, visible.Top - form.Top, visible.Width, visible.Height),
                GraphicsUnit.Pixel);
        }

        return shot;
    }
}
#endif
