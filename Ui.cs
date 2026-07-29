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

    public static Color Border => Dark ? Color.FromArgb(62, 65, 70) : Color.FromArgb(222, 225, 230);

    public static Color Text => Dark ? Color.FromArgb(235, 237, 240) : Color.FromArgb(24, 26, 31);

    public static Color TextMuted => Dark ? Color.FromArgb(150, 156, 163) : Color.FromArgb(115, 122, 133);

    public static Color Accent => Dark ? Color.FromArgb(90, 150, 255) : Color.FromArgb(37, 99, 235);

    public static Color AccentHover => Dark ? Color.FromArgb(112, 168, 255) : Color.FromArgb(55, 117, 246);

    public static Color AccentPressed => Dark ? Color.FromArgb(70, 128, 226) : Color.FromArgb(29, 78, 216);

    /// <summary>Faint accent wash, used behind the status line and on the secondary button.</summary>
    public static Color AccentSoft => Dark ? Color.FromArgb(43, 55, 78) : Color.FromArgb(234, 240, 254);

    public static Color AccentSoftHover => Dark ? Color.FromArgb(52, 66, 93) : Color.FromArgb(221, 231, 253);

    public static Color AccentSoftPressed => Dark ? Color.FromArgb(38, 48, 68) : Color.FromArgb(206, 220, 250);

    public static Color Neutral => Dark ? Color.FromArgb(68, 71, 77) : Color.FromArgb(134, 142, 153);

    public static Color NeutralHover => Dark ? Color.FromArgb(79, 83, 90) : Color.FromArgb(148, 156, 167);

    public static Color NeutralPressed => Dark ? Color.FromArgb(58, 61, 66) : Color.FromArgb(115, 123, 134);

    public static Color NeutralSoft => Dark ? Color.FromArgb(48, 50, 55) : Color.FromArgb(240, 241, 243);

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

    /// <summary>
    /// Colour chip for an effect. Effects that use the stored colour show it, the others show
    /// a spectrum, because that is what they actually do.
    /// </summary>
    public static void PaintSwatch(Graphics g, Rectangle bounds, Color colour, bool spectrum)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = RoundedRectangle(bounds, 3.5f);

        if (spectrum)
        {
            using var brush = new LinearGradientBrush(bounds, Color.Red, Color.Red, LinearGradientMode.Horizontal);
            brush.InterpolationColors = new ColorBlend
            {
                Colors = new[]
                {
                    Color.FromArgb(255, 0, 0), Color.FromArgb(255, 255, 0), Color.FromArgb(0, 255, 0),
                    Color.FromArgb(0, 255, 255), Color.FromArgb(0, 0, 255), Color.FromArgb(255, 0, 255),
                },
                Positions = new[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f },
            };
            g.FillPath(brush, path);
        }
        else
        {
            using var brush = new SolidBrush(colour);
            g.FillPath(brush, path);
        }

        using var pen = new Pen(Color.FromArgb(60, 0, 0, 0));
        g.DrawPath(pen, path);
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

/// <summary>A flat button with rounded corners and hover and pressed states.</summary>
internal sealed class RoundedButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private bool _busy;

    public RoundedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = Theme.Background;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 10;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; } = Theme.Accent;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillHover { get; set; } = Theme.AccentHover;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillPressed { get; set; } = Theme.AccentPressed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Label { get; set; } = Color.White;

    /// <summary>Dims the button while a switch is running, without changing its colour.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Busy
    {
        get => _busy;
        set
        {
            _busy = value;
            _hovered = false;
            _pressed = false;
            Invalidate();
        }
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
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        Color fill = !Enabled ? Theme.NeutralSoft : _pressed ? FillPressed : _hovered ? FillHover : Fill;
        if (Busy)
        {
            fill = Blend(fill, BackColor, 0.35f);
        }

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (GraphicsPath path = Theme.RoundedRectangle(bounds, Radius))
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);

            if (Focused && Enabled)
            {
                using var pen = new Pen(Color.FromArgb(110, Theme.Text), 2f);
                g.DrawPath(pen, path);
            }
        }

        Color label = !Enabled ? Theme.TextMuted : Busy ? Blend(Label, fill, 0.35f) : Label;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, label,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static Color Blend(Color colour, Color towards, float amount) => Color.FromArgb(
        (int)(colour.R + ((towards.R - colour.R) * amount)),
        (int)(colour.G + ((towards.G - colour.G) * amount)),
        (int)(colour.B + ((towards.B - colour.B) * amount)));
}

/// <summary>Rounded status line: a coloured dot and a short sentence on a tinted background.</summary>
internal sealed class StatusPill : Control
{
    private Color _dot = Theme.Accent;
    private Color _tint = Theme.AccentSoft;

    public StatusPill()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
    }

    public void Show(string text, Color dot, Color tint, Color foreground)
    {
        Text = text;
        _dot = dot;
        _tint = tint;
        ForeColor = foreground;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (GraphicsPath path = Theme.RoundedRectangle(bounds, Height / 2f))
        using (var brush = new SolidBrush(_tint))
        {
            g.FillPath(brush, path);
        }

        int dotSize = Math.Max(8, Height / 3);
        int dotY = (Height - dotSize) / 2;
        using (var brush = new SolidBrush(_dot))
        {
            g.FillEllipse(brush, 14, dotY, dotSize, dotSize);
        }

        var text = new Rectangle(14 + dotSize + 9, 0, Width - (14 + dotSize + 9) - 12, Height);
        TextRenderer.DrawText(g, Text, Font, text, ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
