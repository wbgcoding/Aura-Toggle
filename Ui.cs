using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
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

    public static Color Hue(double degrees)
    {
        double h = (((degrees % 360) + 360) % 360) / 60.0;
        double x = 1 - Math.Abs((h % 2) - 1);
        (double r, double g, double b) = h switch
        {
            < 1 => (1.0, x, 0.0),
            < 2 => (x, 1.0, 0.0),
            < 3 => (0.0, 1.0, x),
            < 4 => (0.0, x, 1.0),
            < 5 => (x, 0.0, 1.0),
            _ => (1.0, 0.0, x),
        };

        return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
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
    /// <summary>Fills a shape with the effect as it looks at <paramref name="seconds"/>.</summary>
    public static void Paint(Graphics g, GraphicsPath shape, RectangleF bounds, byte mode, Color colour, double seconds)
    {
        Region clip = g.Clip;
        g.SetClip(shape, CombineMode.Intersect);

        switch (mode)
        {
            case 1: // static
                Fill(g, bounds, colour);
                break;

            case 2: // breathing
                Fill(g, bounds, Theme.Scale(colour, Breath(seconds, 3.2)));
                break;

            case 3: // flashing
                Fill(g, bounds, Theme.Scale(colour, (seconds % 1.1) < 0.55 ? 1.0 : 0.16));
                break;

            case 4: // spectrum cycle
                Fill(g, bounds, Theme.Hue(seconds * 70));
                break;

            case 5: // rainbow
                Spectrum(g, bounds, seconds * 70, 1.0);
                break;

            case 6: // rainbow breathing
                Fill(g, bounds, Theme.Scale(Theme.Hue(seconds * 70), Breath(seconds, 3.2)));
                break;

            case 7: // chase fade
                Chase(g, bounds, colour, seconds, fade: true);
                break;

            case 9: // chase
                Chase(g, bounds, colour, seconds, fade: false);
                break;

            case 11: // wave
                Spectrum(g, bounds, seconds * 45, 2.0);
                break;

            default: // off and anything the controller may know that this tool does not
                Fill(g, bounds, Theme.Neutral);
                break;
        }

        g.Clip = clip;
    }

    private static double Breath(double seconds, double period) =>
        0.25 + (0.75 * ((Math.Sin(seconds / period * 2 * Math.PI) * 0.5) + 0.5));

    private static void Fill(Graphics g, RectangleF bounds, Color colour)
    {
        using var brush = new SolidBrush(colour);
        g.FillRectangle(brush, bounds);
    }

    /// <summary>Moving spectrum. <paramref name="cycles"/> is how often it repeats across the width.</summary>
    private static void Spectrum(Graphics g, RectangleF bounds, double offsetDegrees, double cycles)
    {
        const int steps = 24;
        float width = bounds.Width / steps;

        for (int i = 0; i < steps; i++)
        {
            Color colour = Theme.Hue((i * 360.0 * cycles / steps) - offsetDegrees);
            using var brush = new SolidBrush(colour);

            // One pixel of overlap, otherwise the seams show as thin dark lines.
            g.FillRectangle(brush, bounds.X + (i * width), bounds.Y, width + 1, bounds.Height);
        }
    }

    private static void Chase(Graphics g, RectangleF bounds, Color colour, double seconds, bool fade)
    {
        Fill(g, bounds, Theme.Scale(colour, 0.12));

        float band = Math.Max(bounds.Width / 5f, 12f);
        float travel = bounds.Width + band;
        float head = (float)((seconds * travel / 2.2) % travel) - band;

        if (!fade)
        {
            using var brush = new SolidBrush(colour);
            g.FillRectangle(brush, bounds.X + head, bounds.Y, band, bounds.Height);
            return;
        }

        var tail = new RectangleF(bounds.X + head - band, bounds.Y, band * 2, bounds.Height);
        if (tail.Width <= 0)
        {
            return;
        }

        using var gradient = new LinearGradientBrush(tail, Theme.Scale(colour, 0.12), colour,
            LinearGradientMode.Horizontal);
        g.FillRectangle(gradient, tail);
    }

    /// <summary>Small round icon for the effect list.</summary>
    public static void PaintIcon(Graphics g, Rectangle bounds, byte mode, Color colour)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath shape = Theme.RoundedRectangle(bounds, bounds.Height / 2f);

        // A fixed moment in time that shows each effect at its most recognisable.
        Paint(g, shape, bounds, mode, colour, seconds: mode == 3 ? 0.1 : 0.55);

        using var pen = new Pen(Color.FromArgb(46, 0, 0, 0));
        g.DrawPath(pen, shape);
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
        if (!Focused)
        {
            return;
        }

        using var pen = new Pen(Color.FromArgb(130, Theme.Accent), 2f);
        g.DrawPath(pen, path);
    }
}
