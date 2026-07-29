using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>Colours and drawing helpers, following the Windows light or dark theme.</summary>
internal static class Theme
{
    public static bool Dark
    {
        get
        {
#pragma warning disable WFO5001 // colour mode support is still marked experimental
            return Application.IsDarkModeEnabled;
#pragma warning restore WFO5001
        }
    }

    public static Color Background => Dark ? Color.FromArgb(32, 33, 36) : Color.FromArgb(250, 250, 251);

    public static Color Surface => Dark ? Color.FromArgb(45, 47, 51) : Color.White;

    public static Color SurfaceHover => Dark ? Color.FromArgb(54, 56, 61) : Color.FromArgb(243, 244, 246);

    public static Color Border => Dark ? Color.FromArgb(62, 65, 70) : Color.FromArgb(222, 225, 230);

    public static Color Text => Dark ? Color.FromArgb(235, 237, 240) : Color.FromArgb(24, 26, 31);

    public static Color TextMuted => Dark ? Color.FromArgb(150, 156, 163) : Color.FromArgb(115, 122, 133);

    public static Color Accent => Dark ? Color.FromArgb(90, 150, 255) : Color.FromArgb(37, 99, 235);

    public static Color AccentSoft => Dark ? Color.FromArgb(43, 55, 78) : Color.FromArgb(234, 240, 254);

    public static Color Neutral => Dark ? Color.FromArgb(68, 71, 77) : Color.FromArgb(140, 147, 158);

    public static Color NeutralSoft => Dark ? Color.FromArgb(48, 50, 55) : Color.FromArgb(238, 239, 242);

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

    private static bool UsesColour(byte mode) => mode is 1 or 2 or 3 or 7 or 9;

    /// <summary>
    /// Fills a shape with the effect as it looks at <paramref name="seconds"/>. With
    /// <paramref name="animate"/> false, every mode collapses to a single flat fill instead -
    /// cheaper to paint, and unambiguously reads as paused rather than as a frozen mid-motion
    /// gradient.
    /// </summary>
    public static void Paint(Graphics g, GraphicsPath shape, RectangleF bounds, byte mode, Color colour,
        double seconds, bool animate = true)
    {
        Region clip = g.Clip;
        g.SetClip(shape, CombineMode.Intersect);

        if (!animate)
        {
            Fill(g, bounds, mode == 0 ? Theme.Neutral : UsesColour(mode) ? colour : Theme.Accent);
            g.Clip = clip;
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

        g.Clip = clip;
    }

    private static double Breath(double seconds, double period) =>
        0.22 + (0.78 * ((Math.Sin(seconds / period * 2 * Math.PI) * 0.5) + 0.5));

    private static void Fill(Graphics g, RectangleF bounds, Color colour)
    {
        using var brush = new SolidBrush(colour);
        g.FillRectangle(brush, bounds);
    }

    /// <summary>
    /// A seamless spectrum that scrolls. The gradient is tiled and shifted rather than drawn
    /// in steps, so there is no banding and no seam where it wraps.
    /// </summary>
    private static void Spectrum(Graphics g, RectangleF bounds, double phase, float cycles)
    {
        float period = Math.Max(bounds.Width / cycles, 4f);
        var strip = new RectangleF(bounds.X, bounds.Y, period, bounds.Height);

        using var brush = new LinearGradientBrush(strip, SpectrumStops[0], SpectrumStops[^1],
            LinearGradientMode.Horizontal)
        {
            WrapMode = WrapMode.Tile,
            InterpolationColors = SpectrumBlend,
        };

        double shift = ((phase % 1.0) + 1.0) % 1.0;
        brush.TranslateTransform((float)(shift * period) - period, 0);

        g.FillRectangle(brush, bounds);
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
        float head = (float)(((seconds / 2.4) % 1.0) * (bounds.Width + tailWidth)) - tailWidth;

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

    /// <summary>Small round icon for the effect list.</summary>
    public static void PaintIcon(Graphics g, Rectangle bounds, byte mode, Color colour)
    {
        Theme.Prepare(g);
        using GraphicsPath shape = Theme.RoundedRectangle(bounds, bounds.Height / 2f);

        // A fixed moment in time that shows each effect at its most recognisable.
        Paint(g, shape, bounds, mode, colour, seconds: mode switch
        {
            3 => 0.1,
            7 or 9 => 1.55,
            _ => 0.55,
        });

        using var pen = new Pen(Color.FromArgb(46, 0, 0, 0));
        g.DrawPath(pen, shape);
    }

    /// <summary>
    /// Icon for a custom preset: a small person glyph over the preset's own colour, so it
    /// reads at a glance as user-made rather than as one of the controller's built-in effects.
    /// </summary>
    public static void PaintUserIcon(Graphics g, Rectangle bounds, Color[] colours)
    {
        Theme.Prepare(g);
        using GraphicsPath shape = Theme.RoundedRectangle(bounds, bounds.Height / 2f);

        Color background = colours.Length > 0 ? colours[0] : Theme.Accent;
        using (var fill = new SolidBrush(background))
        {
            g.FillPath(fill, shape);
        }

        double luminance = ((background.R * 0.299) + (background.G * 0.587) + (background.B * 0.114)) / 255.0;
        Color ink = luminance > 0.6 ? Color.FromArgb(215, 20, 20, 24) : Color.FromArgb(235, 255, 255, 255);

        Region clip = g.Clip;
        g.SetClip(shape, CombineMode.Intersect);

        float cx = bounds.X + (bounds.Width / 2f);
        float headRadius = bounds.Height * 0.19f;
        float headCy = bounds.Y + (bounds.Height * 0.34f);
        float shoulderWidth = bounds.Height * 0.66f;
        float shoulderHeight = bounds.Height * 0.72f;

        using (var brush = new SolidBrush(ink))
        {
            g.FillEllipse(brush, cx - headRadius, headCy - headRadius, headRadius * 2, headRadius * 2);
            g.FillEllipse(brush, cx - (shoulderWidth / 2f), bounds.Bottom - shoulderHeight + 2,
                shoulderWidth, shoulderHeight);
        }

        g.Clip = clip;

        using var pen = new Pen(Color.FromArgb(46, 0, 0, 0));
        g.DrawPath(pen, shape);
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

/// <summary>Double buffered container, so resizing and repainting never flicker.</summary>
internal sealed class Layout : TableLayoutPanel
{
    public Layout()
    {
        DoubleBuffered = true;
        BackColor = Theme.Background;
    }
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
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
    }

    protected bool Hovered => _hovered;

    protected bool Pressed => _pressed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 10;

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

        using var pen = new Pen(Color.FromArgb(130, Theme.Accent), 2f);
        g.DrawPath(pen, path);
    }
}
