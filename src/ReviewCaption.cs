#if REVIEW
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AuraToggle;

internal static partial class Program
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowInfo
    {
        public int Size;
        public Rect Window;
        public Rect Client;
        public int Style;
        public int ExStyle;
        public int Status;
        public int BorderX;
        public int BorderY;
        public ushort Atom;
        public ushort Version;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowInfo(IntPtr window, ref WindowInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int size);

    [DllImport("user32.dll", EntryPoint = "GetActiveWindow")]
    private static extern IntPtr ActiveWindowOfThread();

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, int x, int y, uint data, UIntPtr extra);

    private const int ActiveCaption = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;

    /// <summary>
    /// "-review caption": opens the real window against stand-in controllers, then opens the effect
    /// list and closes it again each way a user does - Escape, picking a row, a click back on the
    /// window - and reads the title bar after every step, both as Windows keeps it (the active
    /// caption flag) and as it is drawn (how dark the title text is). The caption has to stay active
    /// throughout: the list belongs to the window. PASS/FAIL to %TEMP%\aura-caption.txt, exit 1 on
    /// any FAIL.
    /// </summary>
    private static int ReviewCaption()
    {
        ToggleForm.ReviewDevices = ReviewControllers();

        // Every message that can repaint or move the window, interleaved with the steps below.
        string trace = Path.Combine(Path.GetTempPath(), "aura-caption-trace.txt");
        File.Delete(trace);
        Environment.SetEnvironmentVariable("AURA_TRACE", trace);

        var form = new ToggleForm();
        var lines = new List<string>();
        int failures = 0;

        void Check(string step)
        {
            var info = new WindowInfo { Size = Marshal.SizeOf<WindowInfo>() };
            GetWindowInfo(form.Handle, ref info);
            bool flag = (info.Status & ActiveCaption) != 0;
            int ink = TitleInk(form);
            IntPtr foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out uint process);
            var className = new System.Text.StringBuilder(256);
            GetClassName(foreground, className, className.Capacity);
            string owner = foreground == form.Handle ? "window"
                : Control.FromHandle(foreground) is Form other ? other.GetType().Name
                : process == Environment.ProcessId ? $"own window {className}"
                : $"another program ({className})";

            // The flag alone is not enough: DWM can draw the caption from its own idea of activation.
            bool ok = flag && ink < 90;
            failures += ok ? 0 : 1;
            lines.Add($"{(ok ? "PASS" : "FAIL")} {step}: caption flag {(flag ? "active" : "inactive")}, " +
                      $"title ink {ink}, foreground {owner}, thread active " +
                      $"{(ActiveWindowOfThread() == form.Handle ? "window" : ActiveWindowOfThread() == IntPtr.Zero ? "none" : "popup")}");
        }

        async Task Step(string name, Action act)
        {
            File.AppendAllText(trace, $"=== {name}{Environment.NewLine}");
            act();
            await Task.Delay(600);
            Check(name);
        }

        form.Shown += async (_, _) =>
        {
            await Task.Delay(800);
            SetForegroundWindow(form.Handle);
            form.Activate();
            await Task.Delay(400);
            Check("start");

            await Step("list open", form.ClickEffectsForReview);
            await Step("list closed by Escape", () => SendKeys.SendWait("{ESC}"));

            await Step("list open again", form.ClickEffectsForReview);
            // The row already selected: a different one would switch the stand-in controllers,
            // which have no hardware behind them and answer with an error dialog.
            await Step("list closed by picking a row", () => SendKeys.SendWait("{ENTER}"));

            static void ClickAt(Point at)
            {
                Cursor.Position = at;
                mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
            }

            // An empty spot of the client area, low on the right: the list hangs down on the
            // left, under the drop down it opened from.
            void ClickWindow() => ClickAt(form.PointToScreen(
                new Point(form.ClientSize.Width - form.Scaled(4), form.ClientSize.Height - form.Scaled(4))));

            void ClickDropDown()
            {
                Rectangle box = form.EffectsOnScreenForReview;
                ClickAt(new Point(box.Left + (box.Width / 2), box.Top + (box.Height / 2)));
            }

            // Without a list open first: proves the click lands on the window at all.
            await Step("a click on the window, no list open", ClickWindow);

            await Step("list open again", form.ClickEffectsForReview);
            await Step("list closed by a click on the window", ClickWindow);

            await Step("list opened by a real click on its drop down", ClickDropDown);
            await Step("list closed by Escape", () => SendKeys.SendWait("{ESC}"));

            lines.Add(failures == 0 ? "all PASS" : $"{failures} FAIL");
            string report = string.Join(Environment.NewLine, lines);
            WriteLine(report);
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "aura-caption.txt"), report);
            }
            catch (Exception ex) when (AuraFiles.IsExpected(ex))
            {
                // The verdict is on stdout as well.
            }

            Application.Exit();
        };

        Application.Run(form);
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// The darkest pixel of the title text, read off the window as DWM draws it: near black while
    /// the caption is active, a light grey while it is not (light theme; dark draws the other way
    /// round, which this does not cover).
    /// </summary>
    private static int TitleInk(Form form)
    {
        Rectangle visible = VisibleBounds(form);
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

        // The title text: right of the icon, inside the caption band above the client area.
        Point client = form.PointToScreen(Point.Empty);
        int left = visible.Left - form.Left + form.Scaled(40);
        int top = visible.Top - form.Top + form.Scaled(6);
        int bottom = client.Y - form.Top - form.Scaled(6);
        int right = Math.Min(left + form.Scaled(100), layer.Width);

        int darkest = 255;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                Color c = layer.GetPixel(x, y);
                darkest = Math.Min(darkest, (c.R + c.G + c.B) / 3);
            }
        }

        return darkest;
    }
}
#endif
