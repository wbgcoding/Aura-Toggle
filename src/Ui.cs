using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// Every fixed distance a surface sets, kept as the call that produced it so a display-scale
/// change can run them all again at the new scale. The main window computes its metrics in one
/// place and calls that method again (<c>ApplyScaledMetrics</c>); a panel that builds its rows
/// through helpers has them spread over the construction instead and records them here as it
/// goes. Recomputed from the 96 dpi constants every time, never scaled from whatever is there
/// now, so a trip to another display and back lands on the same numbers it started with.
/// </summary>
internal sealed class ScaledMetrics
{
    private readonly List<Action> _metrics = new();

    /// <summary>Applies one metric now and keeps it for the next display-scale change.</summary>
    public void Add(Action metric)
    {
        _metrics.Add(metric);
        metric();
    }

    public void Reapply()
    {
        foreach (Action metric in _metrics)
        {
            metric();
        }
    }
}

/// <summary>Colours and drawing helpers, following the Windows light or dark theme.</summary>
internal static class Theme
{
    /// <summary>
    /// What a colour means, rather than what it is. Both palettes are listed in this order, so a
    /// control's current colour is enough to tell which role it was given - which is what lets
    /// a whole window be re-coloured after Windows switches theme.
    /// </summary>
    private enum Role
    {
        Background,
        Surface,
        SurfaceHover,
        Border,
        Text,
        TextMuted,
        Accent,
        AccentSoft,
        Neutral,
        NeutralSoft,
        Danger,
    }

    private static readonly Color[] DarkPalette =
    {
        Color.FromArgb(32, 33, 36),    // Background
        Color.FromArgb(45, 47, 51),    // Surface
        Color.FromArgb(54, 56, 61),    // SurfaceHover
        Color.FromArgb(62, 65, 70),    // Border
        Color.FromArgb(235, 237, 240), // Text
        Color.FromArgb(150, 156, 163), // TextMuted
        Color.FromArgb(90, 150, 255),  // Accent
        Color.FromArgb(43, 55, 78),    // AccentSoft
        Color.FromArgb(68, 71, 77),    // Neutral
        Color.FromArgb(61, 64, 70),    // NeutralSoft - lighter than Surface/SurfaceHover so a
                                       // filled button stays visible against the popup background
        Color.FromArgb(255, 116, 108), // Danger
    };

    private static readonly Color[] LightPalette =
    {
        Color.FromArgb(250, 250, 251), // Background
        Color.White,                   // Surface
        Color.FromArgb(243, 244, 246), // SurfaceHover
        Color.FromArgb(222, 225, 230), // Border
        Color.FromArgb(24, 26, 31),    // Text
        Color.FromArgb(115, 122, 133), // TextMuted
        Color.FromArgb(37, 99, 235),   // Accent
        Color.FromArgb(234, 240, 254), // AccentSoft
        Color.FromArgb(140, 147, 158), // Neutral
        Color.FromArgb(227, 229, 233), // NeutralSoft
        Color.FromArgb(198, 40, 40),   // Danger
    };

    /// <summary>
    /// The roles a text colour may hold. White is the light theme's surface and also the label
    /// of a filled accent button, so translating a foreground has to stay out of the fills.
    /// </summary>
    private static readonly Role[] InkRoles = { Role.Text, Role.TextMuted, Role.Accent, Role.Danger };

    // Asked once and then remembered: every custom control reads this on every repaint, and the
    // Windows setting behind it only changes when Forget() is called from the window that heard
    // the system say so.
    private static bool? _dark;

    /// <summary>
    /// The fonts this window uses, shared rather than created per control. Every popup used to
    /// build its own and none of them were ever released.
    /// </summary>
    public static readonly Font Ui = new("Segoe UI", 9F);

    public static readonly Font Menu = new("Segoe UI", 9.5F);

    public static readonly Font Input = new("Segoe UI", 10F);

    public static readonly Font Heading = new("Segoe UI", 11F, FontStyle.Bold);

    /// <summary>Names a group inside a panel that already has a <see cref="Heading"/> of its own -
    /// one step down, so the two do not compete for the same rank.</summary>
    public static readonly Font Subheading = new("Segoe UI", 10F, FontStyle.Bold);

    /// <summary>The state on the big switch, which is what the window is read from across a room.</summary>
    public static readonly Font Display = new("Segoe UI", 30F, FontStyle.Bold);

    /// <summary>The label on a panel's primary button.</summary>
    public static readonly Font Action = new("Segoe UI", 11F, FontStyle.Bold);

    public static bool Dark => _dark ??= SystemDark();

    /// <summary>Forgets the cached theme, so the next read follows Windows again.</summary>
    public static void Forget() => _dark = null;

    private static bool SystemDark()
    {
#pragma warning disable WFO5001 // colour mode support is still marked experimental
        // A mode forced through SetColorMode wins, which is what the render checks rely on.
        // Following the system is the normal case, and then the Windows setting is read directly:
        // WinForms latches its own dark mode at startup, so asking it would never see a switch.
        return Application.ColorMode == SystemColorMode.System
            ? AppsUseDarkTheme()
            : Application.IsDarkModeEnabled;
#pragma warning restore WFO5001
    }

    /// <summary>The Windows "app mode" setting. Its value is 0 for dark, and missing means light.</summary>
    private static bool AppsUseDarkTheme()
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or
                                       UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Color Of(Role role) => (Dark ? DarkPalette : LightPalette)[(int)role];

    public static Color Background => Of(Role.Background);

    public static Color Surface => Of(Role.Surface);

    public static Color SurfaceHover => Of(Role.SurfaceHover);

    public static Color Border => Of(Role.Border);

    public static Color Text => Of(Role.Text);

    public static Color TextMuted => Of(Role.TextMuted);

    public static Color Accent => Of(Role.Accent);

    public static Color AccentSoft => Of(Role.AccentSoft);

    public static Color Neutral => Of(Role.Neutral);

    public static Color NeutralSoft => Of(Role.NeutralSoft);

    /// <summary>For the one thing in this window that cannot be undone: deleting a preset.</summary>
    public static Color Danger => Of(Role.Danger);

    /// <summary>
    /// Re-colours a window, everything on it and every panel it owns, after Windows switched
    /// between light and dark. Controls that paint straight from the palette only need the
    /// repaint; the colours that were copied into a property at construction are translated by
    /// role, which is why nothing has to remember what it once asked for.
    /// </summary>
    public static void Retint(Form window)
    {
        Retint((Control)window, Dark ? LightPalette : DarkPalette, Dark ? DarkPalette : LightPalette);

        foreach (Form owned in window.OwnedForms)
        {
            Retint(owned);
        }
    }

    /// <summary>The counterpart of a colour from the other theme, for colours held in a field.</summary>
    public static Color Retint(Color colour) =>
        Translate(colour, Dark ? LightPalette : DarkPalette, Dark ? DarkPalette : LightPalette, inkOnly: false)
            ?? colour;

    private static void Retint(Control control, Color[] from, Color[] to)
    {
        // A FlatControl inherits its background by default, but a panel that gave one of its own
        // still holds the old palette's colour in it - and a custom paint starts by clearing to
        // exactly that. Translating it here keeps the relationship the panel asked for instead of
        // leaving white boxes on a window that has already gone dark.
        if (Translate(control.BackColor, from, to, inkOnly: false) is Color background)
        {
            control.BackColor = background;
        }

        if (control is FlatControl flat)
        {
            // Anything the control copied into a field of its own is translated by the control.
            flat.ApplyTheme();
        }
        else if (control is LinkLabel link && Translate(link.LinkColor, from, to, inkOnly: true) is Color accent)
        {
            // A link keeps its colour in three properties of its own, none of which is ForeColor.
            link.LinkColor = accent;
            link.ActiveLinkColor = accent;
            link.VisitedLinkColor = accent;
        }

        if (Translate(control.ForeColor, from, to, inkOnly: true) is Color foreground)
        {
            control.ForeColor = foreground;
        }

        foreach (Control child in control.Controls)
        {
            Retint(child, from, to);
        }

        control.Invalidate(true);
    }

    private static Color? Translate(Color colour, Color[] from, Color[] to, bool inkOnly)
    {
        for (var role = 0; role < from.Length; role++)
        {
            if (inkOnly && Array.IndexOf(InkRoles, (Role)role) < 0)
            {
                continue;
            }

            if (from[role].ToArgb() == colour.ToArgb())
            {
                return to[role];
            }
        }

        return null;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>
    /// Asks Windows for rounded corners on a borderless window. Doing it through the desktop
    /// compositor gives clean, anti-aliased edges and a real shadow; clipping the window to a
    /// rounded region instead is what produced stair-stepped, black looking corners.
    /// Silently does nothing before Windows 11, which then simply keeps square corners.
    /// </summary>
    public static void RoundWindowCorners(IntPtr window)
    {
        const int WindowCornerPreference = 33;
        const int Round = 2;

        int preference = Round;
        DwmSetWindowAttribute(window, WindowCornerPreference, ref preference, sizeof(int));
    }

    /// <summary>
    /// Turns a length written against the 96 dpi baseline into pixels on the display
    /// <paramref name="control"/> is currently on.
    /// </summary>
    /// <remarks>
    /// Nothing is scaled for us. These forms set <see cref="ContainerControl.AutoScaleMode"/> but
    /// never <see cref="ContainerControl.AutoScaleDimensions"/>, and without that WinForms' own
    /// auto-scaling does nothing at all - not to a size assigned in a constructor, not to a
    /// padding, not to a margin. Only fonts, and therefore measured text, grow by themselves.
    /// Every other length stays at 96 dpi until it is put through here: that is a half-size gear
    /// beside full-size text, and labels clipped by a limit written for a 96 dpi screen. Reading
    /// <see cref="Control.DeviceDpi"/> rather than the primary monitor's keeps it right on a
    /// second screen at a different scale.
    /// </remarks>
    public static int Scaled(this Control control, int length) => length * DpiOf(control) / 96;

    /// <summary>
    /// The dpi of the window <paramref name="control"/> sits in, rather than the control's own.
    /// Windows tells a window about a display-scale change before it tells the controls inside it,
    /// so for a while the two disagree - and a layout measured half in one scale and half in the
    /// other is wrong in a way that looks perfectly consistent to any check. One dpi for the whole
    /// window means the measurement is right from the first pass, whenever the children catch up.
    /// </summary>
    private static int DpiOf(Control control) => (control.TopLevelControl ?? control).DeviceDpi;

    /// <summary>
    /// The dpi GDI measures text at, which is the process-wide system dpi and does not follow a
    /// window onto a differently scaled monitor. Fixed for the life of the process (changing it
    /// requires signing out), so it is read once.
    /// </summary>
    private static readonly int SystemDpi = ReadSystemDpi();

    /// <summary>Exposed for <c>-review layout</c>, which reports it next to the window's own dpi.</summary>
    public static int TextDpi => SystemDpi;

    private static int ReadSystemDpi()
    {
        using var screen = Graphics.FromHwnd(IntPtr.Zero);
        return (int)screen.DpiX;
    }

    /// <summary>
    /// Width of <paramref name="text"/> as it will actually be drawn on the display this control
    /// is on.
    /// </summary>
    /// <remarks>
    /// Deliberately just the measurement, with no display-scale correction on top of it. Both
    /// measuring and drawing turn the font's point size into pixels against the system dpi, and
    /// the font itself is the part that follows the display: WinForms replaces every control's
    /// font with a rescaled copy on a scale change (visible in <c>-review layout</c>, where the
    /// same font reads 9 pt on a 150 % display and 6 pt on a 100 % one). Scaling the result by the
    /// display's dpi as well applied that same factor twice, which is why the window came out
    /// half again as wide as it needed to be on a second monitor at 150 % - the text was measured
    /// as if it were both bigger and on a bigger display, and everything in the row was stretched
    /// to fill the extra room. The fixed distances around the text still go through
    /// <see cref="Scaled"/>; that is the one place the display scale belongs.
    /// </remarks>
    public static int MeasuredWidth(this Control control, string text, Font font) =>
        TextRenderer.MeasureText(text, font).Width;

    /// <summary>The drawing quality every custom control in this window paints with.</summary>
    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();

        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    public static Color Hue(double degrees) => FromHsv(degrees, 1.0, 1.0);

    public static Color FromHsv(double hueDegrees, double saturation, double value)
    {
        double h = (((hueDegrees % 360) + 360) % 360) / 60.0;
        double c = value * saturation;
        double x = c * (1 - Math.Abs((h % 2) - 1));
        double m = value - c;
        (double r, double g, double b) = h switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromArgb(
            Math.Clamp((int)((r + m) * 255), 0, 255),
            Math.Clamp((int)((g + m) * 255), 0, 255),
            Math.Clamp((int)((b + m) * 255), 0, 255));
    }

    /// <summary>Parses a bare or #-prefixed 6-digit hex string ("RRGGBB" or "#RRGGBB").</summary>
    public static bool TryParseHex(string text, out Color colour)
    {
        string digits = text.Trim().TrimStart('#');

        // Every character has to be a hex digit: NumberStyles.HexNumber also allows surrounding
        // whitespace, so " 12345" would otherwise pass the length check and parse as 0x12345.
        if (digits.Length == 6 && digits.All(Uri.IsHexDigit) &&
            int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            colour = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }

        colour = default;
        return false;
    }

    /// <summary>Hue in degrees, saturation and value in 0..1.</summary>
    public static (double Hue, double Saturation, double Value) ToHsv(Color colour)
    {
        double r = colour.R / 255.0, g = colour.G / 255.0, b = colour.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0.0;
        if (delta > 0.0001)
        {
            if (max == r)
            {
                h = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                h = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                h = 60 * (((r - g) / delta) + 4);
            }
        }

        if (h < 0)
        {
            h += 360;
        }

        double s = max <= 0.0001 ? 0.0 : delta / max;
        return (h, s, max);
    }

    public static Color Scale(Color colour, double factor) => Color.FromArgb(
        Math.Clamp((int)(colour.R * factor), 0, 255),
        Math.Clamp((int)(colour.G * factor), 0, 255),
        Math.Clamp((int)(colour.B * factor), 0, 255));

    public static Color Blend(Color colour, Color towards, double amount) => Color.FromArgb(
        (int)(colour.R + ((towards.R - colour.R) * amount)),
        (int)(colour.G + ((towards.G - colour.G) * amount)),
        (int)(colour.B + ((towards.B - colour.B) * amount)));
}

/// <summary>
/// Draws what an effect looks like. The same code paints the animated toggle button and the
/// small icons in the effect list, so a list entry always previews the real thing.
/// </summary>
internal static class EffectPainter
{
    private static readonly Color[] SpectrumStops =
    {
        Color.FromArgb(255, 0, 0), Color.FromArgb(255, 200, 0), Color.FromArgb(120, 255, 0),
        Color.FromArgb(0, 255, 140), Color.FromArgb(0, 200, 255), Color.FromArgb(60, 90, 255),
        Color.FromArgb(190, 60, 255), Color.FromArgb(255, 0, 160), Color.FromArgb(255, 0, 0),
    };

    // Built once: a fresh ColorBlend was previously allocated on every single animation
    // frame, which was the main cost behind the animation feeling laggy.
    private static readonly ColorBlend SpectrumBlend = Blend(SpectrumStops);

    // The brush wrapping that blend was left allocating a fresh LinearGradientBrush every frame
    // regardless - rebuilt only when the paint size changes, same as EffectSurface's own buffer.
    private static LinearGradientBrush? _spectrumBrush;
    private static SizeF _spectrumBrushSize;

    private static bool UsesColour(byte mode) => AuraPresets.ByMode(mode)?.UsesColour ?? false;

    /// <summary>
    /// Draws the effect as it looks at <paramref name="seconds"/>, filling
    /// <paramref name="bounds"/> edge to edge. With <paramref name="animate"/> false, every
    /// mode collapses to a single flat fill instead - cheaper to paint, and unambiguously
    /// reads as paused rather than as a frozen mid-motion gradient.
    /// </summary>
    /// <remarks>
    /// Deliberately unclipped. Rounding is applied once, by <see cref="EffectSurface"/>, which
    /// masks the finished frame through an anti-aliased path. Clipping here instead - as this
    /// used to - meant a <see cref="Graphics.SetClip(GraphicsPath)"/> region, and a region has
    /// hard, unsmoothed edges: that was the stair-stepped rim on the button and on the effect
    /// icons.
    /// </remarks>
    public static void Render(Graphics g, RectangleF bounds, byte mode, Color colour,
        double seconds, bool animate = true)
    {
        if (!animate)
        {
            Fill(g, bounds, mode == 0 ? Theme.Neutral : UsesColour(mode) ? colour : Theme.Accent);
            return;
        }

        switch (mode)
        {
            case 1: // static
                Fill(g, bounds, colour);
                break;

            case 2: // breathing
                Fill(g, bounds, Theme.Scale(colour, Breath(seconds, 3.2)));
                break;

            case 3: // flashing
                Fill(g, bounds, Theme.Scale(colour, (seconds % 1.1) < 0.55 ? 1.0 : 0.14));
                break;

            case 4: // spectrum cycle
                Fill(g, bounds, Theme.Hue(seconds * 70));
                break;

            case 5: // rainbow
                Spectrum(g, bounds, seconds / 3.0, cycles: 1f);
                break;

            case 6: // rainbow breathing
                Fill(g, bounds, Theme.Scale(Theme.Hue(seconds * 70), Breath(seconds, 3.2)));
                break;

            case 7: // chase fade
                Chase(g, bounds, colour, seconds, tail: 0.42f);
                break;

            case 9: // chase
                Chase(g, bounds, colour, seconds, tail: 0.16f);
                break;

            case 11: // wave: the same spectrum, drifting slowly across the whole strip
                Spectrum(g, bounds, seconds / 7.0, cycles: 1f);
                break;

            default: // off and anything the controller may know that this tool does not
                Fill(g, bounds, Theme.Neutral);
                break;
        }
    }

    private static double Breath(double seconds, double period) =>
        0.22 + (0.78 * ((Math.Sin(seconds / period * 2 * Math.PI) * 0.5) + 0.5));

    // Shared rather than allocated per call: painting only ever happens on the UI thread, each
    // call sets the colour and uses the brush immediately, and animated effects call this every
    // frame at up to 30 fps per visible control.
    private static readonly SolidBrush FillBrush = new(Color.Black);

    private static void Fill(Graphics g, RectangleF bounds, Color colour)
    {
        FillBrush.Color = colour;
        g.FillRectangle(FillBrush, bounds);
    }

    /// <summary>
    /// A seamless spectrum that scrolls. The gradient is tiled and shifted rather than drawn
    /// in steps, so there is no banding and no seam where it wraps.
    /// </summary>
    private static void Spectrum(Graphics g, RectangleF bounds, double phase, float cycles)
    {
        float period = Math.Max(bounds.Width / cycles, 4f);
        var size = new SizeF(period, bounds.Height);

        if (_spectrumBrush == null || _spectrumBrushSize != size)
        {
            _spectrumBrush?.Dispose();
            _spectrumBrush = new LinearGradientBrush(new RectangleF(PointF.Empty, size),
                SpectrumStops[0], SpectrumStops[^1], LinearGradientMode.Horizontal)
            {
                WrapMode = WrapMode.Tile,
                InterpolationColors = SpectrumBlend,
            };
            _spectrumBrushSize = size;
        }

        // The brush's own rectangle only ever holds the size above; position - the animation
        // shift and the caller's bounds.X/Y - lives entirely in the transform, reset and
        // reapplied every call rather than left to accumulate on the reused brush.
        double shift = ((phase % 1.0) + 1.0) % 1.0;
        _spectrumBrush.ResetTransform();
        _spectrumBrush.TranslateTransform(bounds.X + (float)(shift * period) - period, bounds.Y);

        g.FillRectangle(_spectrumBrush, bounds);
    }

    private static ColorBlend Blend(Color[] colours)
    {
        var positions = new float[colours.Length];
        for (int i = 0; i < colours.Length; i++)
        {
            positions[i] = (float)i / (colours.Length - 1);
        }

        return new ColorBlend { Colors = colours, Positions = positions };
    }

    /// <summary>
    /// A lit segment travelling along the strip, with a tail that fades out behind it. The
    /// tail is drawn as a gradient into the dim base colour, so nothing smears past it.
    /// </summary>
    private static void Chase(Graphics g, RectangleF bounds, Color colour, double seconds, float tail)
    {
        Color dim = Theme.Scale(colour, 0.10);
        Fill(g, bounds, dim);

        float tailWidth = Math.Max(bounds.Width * tail, 10f);
        float head = (float)(((seconds / 3.8) % 1.0) * (bounds.Width + tailWidth)) - tailWidth;

        // Drawn twice so the comet re-enters on the left exactly as it leaves on the right.
        foreach (float offset in new[] { head, head - bounds.Width - tailWidth })
        {
            var band = new RectangleF(bounds.X + offset, bounds.Y, tailWidth, bounds.Height);
            if (band.Right < bounds.X || band.X > bounds.Right)
            {
                continue;
            }

            using var comet = new LinearGradientBrush(
                new RectangleF(band.X - 1, band.Y, band.Width + 2, band.Height), dim, colour,
                LinearGradientMode.Horizontal);
            g.FillRectangle(comet, band);
        }
    }

    /// <summary>
    /// Small round icon for the effect list. Pass a <paramref name="surface"/> to reuse one
    /// buffer for a whole list; without one it allocates and frees its own.
    /// </summary>
    public static void PaintIcon(Graphics g, Rectangle bounds, byte mode, Color colour,
        EffectSurface? surface = null)
    {
        // A fixed moment in time that shows each effect at its most recognisable.
        double seconds = mode switch
        {
            3 => 0.1,
            7 or 9 => 2.4,
            _ => 0.55,
        };

        Using(surface, shared => shared.Paint(g, bounds, bounds.Height / 2f, buffer =>
            Render(buffer, new RectangleF(0, 0, bounds.Width, bounds.Height), mode, colour, seconds),
            Outline(mode == 0 ? Theme.Neutral : UsesColour(mode) ? colour : Theme.Accent)));
    }

    /// <summary>Runs <paramref name="body"/> with the surface given, or with a throwaway one.</summary>
    private static void Using(EffectSurface? surface, Action<EffectSurface> body)
    {
        if (surface != null)
        {
            body(surface);
            return;
        }

        using var own = new EffectSurface();
        body(own);
    }

    /// <summary>
    /// A hairline along the icon's edge, but only for an icon that would otherwise disappear into
    /// the panel it sits on - a white pill on a white window, a near-black one in dark mode. On a
    /// coloured icon the same line reads as a rim around it, which is what made some of them look
    /// outlined and others not.
    /// </summary>
    private static Color? Outline(Color icon)
    {
        double distance = Math.Abs(Luminance(icon) - Luminance(Theme.Surface));

        return distance > 0.22
            ? null
            : Theme.Dark ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(58, 0, 0, 0);
    }

    internal static double Luminance(Color colour) =>
        ((colour.R * 0.299) + (colour.G * 0.587) + (colour.B * 0.114)) / 255.0;

    /// <summary>
    /// Icon for a custom preset: a small person glyph over the preset's own colour, so it
    /// reads at a glance as user-made rather than as one of the controller's built-in effects.
    /// </summary>
    public static void PaintUserIcon(Graphics g, Rectangle bounds, Color[] colours,
        EffectSurface? surface = null)
    {
        Color background = colours.Length > 0 ? colours[0] : Theme.Accent;
        Color ink = Luminance(background) > 0.6 ? Color.FromArgb(215, 20, 20, 24) : Color.FromArgb(235, 255, 255, 255);

        Using(surface, shared => shared.Paint(g, bounds, bounds.Height / 2f, buffer =>
        {
            using (var fill = new SolidBrush(background))
            {
                buffer.FillRectangle(fill, 0, 0, bounds.Width, bounds.Height);
            }

            float cx = bounds.Width / 2f;
            float headRadius = bounds.Height * 0.19f;
            float headCy = bounds.Height * 0.34f;
            float shoulderWidth = bounds.Height * 0.66f;
            float shoulderHeight = bounds.Height * 0.72f;

            using var brush = new SolidBrush(ink);
            buffer.FillEllipse(brush, cx - headRadius, headCy - headRadius, headRadius * 2, headRadius * 2);
            buffer.FillEllipse(brush, cx - (shoulderWidth / 2f), bounds.Height - shoulderHeight + 2,
                shoulderWidth, shoulderHeight);
        }, Outline(background)));
    }

    /// <summary>An ordinary gear: eight teeth around a ring with an open centre.</summary>
    public static GraphicsPath GearPath(PointF centre, float radius)
    {
        const int teeth = 8;
        double step = Math.PI * 2 / teeth;
        float root = radius * 0.74f;
        float hole = radius * 0.34f;

        var path = new GraphicsPath();
        var points = new List<PointF>(teeth * 4);

        for (int i = 0; i < teeth; i++)
        {
            double centreAngle = i * step;
            points.Add(At(centre, centreAngle - (step * 0.19), radius));
            points.Add(At(centre, centreAngle + (step * 0.19), radius));
            points.Add(At(centre, centreAngle + (step * 0.31), root));
            points.Add(At(centre, centreAngle + (step * 0.69), root));
        }

        path.AddPolygon(points.ToArray());
        path.AddEllipse(centre.X - hole, centre.Y - hole, hole * 2, hole * 2);
        path.FillMode = FillMode.Alternate;

        return path;
    }

    private static PointF At(PointF centre, double angle, float radius) => new(
        centre.X + (float)(Math.Cos(angle) * radius),
        centre.Y + (float)(Math.Sin(angle) * radius));
}

/// <summary>
/// Draws a rounded shape whose contents take several passes, with exactly one anti-aliased
/// edge. The passes go into an offscreen buffer first, and the finished frame is then masked
/// through the rounded path in a single fill.
/// </summary>
/// <remarks>
/// This is what keeps the button and the effect icons smooth. Painting straight onto the
/// control needed a clip region for the rounding, and a region is not anti-aliased at all;
/// layering a second pass (the label wash, the busy dim) over the same path afterwards then
/// blended into the edge pixels again and left a dark rim outside it. One buffer, one masked
/// fill, no matter how many passes the effect itself takes.
/// </remarks>
internal sealed class EffectSurface : IDisposable
{
    private Bitmap? _buffer;

    /// <summary>Rounds <paramref name="bounds"/> itself, for callers that need no path of their own.</summary>
    /// <param name="outline">
    /// Optional hairline along the inside of the edge, so a pale icon still reads on a pale
    /// window. Drawn into the buffer rather than over the finished shape: a stroke on top would
    /// blend into the anti-aliased edge and fringe it.
    /// </param>
    public void Paint(Graphics g, Rectangle bounds, float radius, Action<Graphics> draw, Color? outline = null)
    {
        using GraphicsPath shape = Theme.RoundedRectangle(bounds, radius);

        Paint(g, shape, bounds, surface =>
        {
            draw(surface);

            if (outline is not Color colour)
            {
                return;
            }

            using GraphicsPath inner = Theme.RoundedRectangle(
                new RectangleF(0.5f, 0.5f, bounds.Width - 1f, bounds.Height - 1f), radius);
            using var pen = new Pen(colour, 1.2f);
            surface.DrawPath(pen, inner);
        });
    }

    /// <summary>
    /// Hands <paramref name="draw"/> a surface the size of <paramref name="bounds"/>, with the
    /// origin at its top left, and masks the result through <paramref name="shape"/>.
    /// </summary>
    public void Paint(Graphics g, GraphicsPath shape, Rectangle bounds, Action<Graphics> draw)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (_buffer == null || _buffer.Width < bounds.Width || _buffer.Height < bounds.Height)
        {
            _buffer?.Dispose();
            _buffer = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        }

        using (Graphics surface = Graphics.FromImage(_buffer))
        {
            Theme.Prepare(surface);

            // The buffer is reused and can be larger than this shape, so it starts empty rather
            // than showing whatever the last, bigger caller left in it.
            surface.Clear(Color.Transparent);
            draw(surface);
        }

        using var brush = new TextureBrush(_buffer, WrapMode.Clamp);
        brush.TranslateTransform(bounds.X, bounds.Y);

        // The buffer is already at the target scale, so sampling it 1:1 keeps it pin sharp;
        // only the path's own edge is smoothed.
        InterpolationMode interpolation = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.FillPath(brush, shape);
        g.InterpolationMode = interpolation;
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}

/// <summary>Double buffered container, so resizing and repainting never flicker.</summary>
internal sealed class Layout : TableLayoutPanel
{
    public Layout()
    {
        DoubleBuffered = true;
        BackColor = Theme.Background;
    }
}

/// <summary>
/// Shared chrome for every borderless popup: no border, no taskbar entry, manual position,
/// <see cref="Theme.Surface"/> background, double buffering, a native drop shadow
/// (<c>CS_DROPSHADOW</c>) instead of a drawn one, and DWM-rounded corners once the handle
/// exists. Each popup still owns its own <c>AutoScaleMode</c>, <c>OnDeactivate</c> and
/// <c>OnKeyDown</c> - see DESIGN.md for why those differ (a caller-sized popup would double-
/// scale under Dpi mode; a couple of popups hold content that must survive an outside click).
/// </summary>
internal abstract class PopupForm : Form
{
    protected PopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.RoundWindowCorners(Handle);
    }

    /// <summary>
    /// Puts the window where it was asked for, but never off the screen it opens on.
    /// </summary>
    /// <param name="flipAbove">
    /// The height of the control this drops from, plus the gap below it. A list that would run
    /// past the bottom edge opens upward instead - above that control, not over it. Zero for a
    /// panel that only has to stay on screen.
    /// </param>
    protected void Place(Point at, int flipAbove = 0)
    {
        Rectangle screen = Screen.FromPoint(at).WorkingArea;
        int y = flipAbove > 0 && at.Y + Height > screen.Bottom ? at.Y - Height - flipAbove : at.Y;

        Location = OnScreen(new Point(at.X, y), screen);
    }

    /// <summary>
    /// Pulls the window back onto its screen after it has grown. Every panel here sizes itself to
    /// its contents, and contents arrive after it was placed - a hotkey row switched on, an effect
    /// that brings colour chips with it - so the position it was given is no longer the one it
    /// needs. Without this the buttons at the bottom end up under the taskbar.
    /// </summary>
    protected void KeepOnScreen()
    {
        if (IsHandleCreated && Visible)
        {
            Location = OnScreen(Location, Screen.FromControl(this).WorkingArea);
        }
    }

    private Point OnScreen(Point at, Rectangle screen) => new(
        Math.Max(screen.Left + 4, Math.Min(at.X, screen.Right - Width - 4)),
        Math.Max(screen.Top + 4, Math.Min(at.Y, screen.Bottom - Height - 4)));
}

/// <summary>Base for the flat, rounded controls in this window.</summary>
internal abstract class FlatControl : Control
{
    private bool _hovered;
    private bool _pressed;

    protected FlatControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        // BackColor is deliberately left alone so it keeps inheriting from whatever this sits
        // on. Pinning it to the window colour painted that colour into the corners outside the
        // rounded shape, which on a panel of a different shade showed up as a square halo -
        // the "ugly border" around buttons inside the popups.
        ForeColor = Theme.Text;
    }

    protected bool Hovered => _hovered;

    protected bool Pressed => _pressed;

    /// <summary>
    /// Called after Windows switched theme. Everything here paints straight from
    /// <see cref="Theme"/>, so the repaint is all it takes; a control that copies a palette
    /// colour into a field of its own translates it here.
    /// </summary>
    public virtual void ApplyTheme() => Invalidate();

    /// <summary>
    /// Corner rounding, written as the 96 dpi value and read back scaled for the display this is
    /// on. Every painter takes it straight to <see cref="Theme.RoundedRectangle"/>, so a raw
    /// number here left a 10 px corner on a control that had otherwise doubled.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius
    {
        get => this.Scaled(_radius);
        set => _radius = value;
    }

    private int _radius = 10;

    /// <summary>
    /// The control's size at 96 dpi. Assigning it applies the scaled size now and again whenever
    /// the display scale changes - these controls are laid out by size rather than by measuring
    /// their own content, and WinForms will not resize them here (the forms set
    /// <see cref="ContainerControl.AutoScaleMode"/> but never
    /// <see cref="ContainerControl.AutoScaleDimensions"/>, which makes its auto-scaling inert).
    /// </summary>
    protected Size DesignSize
    {
        set
        {
            _designSize = value;
            ApplyDesignSize();
        }
    }

    /// <summary>
    /// As <see cref="DesignSize"/>, for the controls whose width belongs to the layout they sit
    /// in (docked or stretched) and only whose height is a fixed design number.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int DesignHeight
    {
        set
        {
            _designHeight = value;
            ApplyDesignSize();
        }
    }

    private Size _designSize;
    private int _designHeight;

    private void ApplyDesignSize()
    {
        if (!_designSize.IsEmpty)
        {
            Size = new Size(this.Scaled(_designSize.Width), this.Scaled(_designSize.Height));
        }
        else if (_designHeight > 0)
        {
            Height = this.Scaled(_designHeight);
        }
    }

    /// <summary>Fires when the window this sits on moves to a display with a different scale.</summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyDesignSize();
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Focus();
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected void DrawFocusRing(Graphics g, GraphicsPath path)
    {
        // ShowFocusCues is false until the user navigates by keyboard, so a mouse click does
        // not leave a ring behind.
        if (!Focused || !ShowFocusCues)
        {
            return;
        }

        using var pen = new Pen(Color.FromArgb(130, Theme.Accent), 2f * DeviceDpi / 96f);
        g.DrawPath(pen, path);
    }
}

/// <summary>
/// Lets a borderless window be dragged by parts of its own contents. The gesture is handed to
/// Windows rather than moved from mouse events, so it behaves like any other title bar - it
/// snaps, it keeps up with the pointer, and releasing it needs no bookkeeping here.
/// </summary>
internal static class WindowDrag
{
    private const int NonClientLeftButtonDown = 0x00A1;
    private const int HitCaption = 2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    /// <param name="grips">
    /// The controls that act as the title bar. Child controls do not pass their mouse events on,
    /// so anything that should be draggable has to be named here.
    /// </param>
    public static void Enable(Form window, params Control[] grips)
    {
        foreach (Control grip in grips)
        {
            grip.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left || window.IsDisposed)
                {
                    return;
                }

                ReleaseCapture();
                SendMessage(window.Handle, NonClientLeftButtonDown, (IntPtr)HitCaption, IntPtr.Zero);
            };
        }
    }
}

/// <summary>
/// Brings this process's own window out in front when it is first shown. DO NOT REMOVE OR WEAKEN
/// THIS - a window opening behind Setup has come back three times, and each of the two mechanisms
/// below is here because the other one is not enough on its own:
/// <list type="number">
/// <item>
/// Windows only lets the current foreground process, or one it started directly, activate a
/// window. Setup hands off through a de-elevated relaunch, so this process is neither. Attaching
/// this thread's input queue to the foreground window's for the one call is the standard way
/// around that - the same trick the taskbar relies on to switch windows for the user.
/// </item>
/// <item>
/// That trick cannot work against Setup itself: Setup runs elevated, and a process at a lower
/// integrity level is not allowed to attach to its input queue or take the foreground from it.
/// Raising the window to the top of the z-order does not need any of those rights, so that is
/// what actually puts it in front of the wizard. Set and immediately cleared, so this ends up a
/// normal window rather than one that stays on top of everything.
/// </item>
/// </list>
/// </summary>
internal static class ForegroundWindow
{
    private static readonly IntPtr TopMost = new(-1);
    private static readonly IntPtr NoTopMost = new(-2);

    private const uint NoMove = 0x0002;
    private const uint NoSize = 0x0001;
    private const uint NoActivate = 0x0010;
    private const uint ShowWindow = 0x0040;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachId, uint attachToId, bool attach);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
        int width, int height, uint flags);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    public static void Claim(Form window)
    {
        IntPtr handle = window.Handle;
        if (GetForegroundWindow() == handle)
        {
            return;
        }

        uint currentThread = GetCurrentThreadId();
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
            AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            SetForegroundWindow(handle);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        if (GetForegroundWindow() == handle)
        {
            return;
        }

        // Still behind something that would not hand the foreground over - an elevated Setup is
        // exactly that case. Being visible in front is what matters here, not owning the focus.
        SetWindowPos(handle, TopMost, 0, 0, 0, 0, NoMove | NoSize | NoActivate | ShowWindow);
        SetWindowPos(handle, NoTopMost, 0, 0, 0, 0, NoMove | NoSize | NoActivate | ShowWindow);
    }
}
