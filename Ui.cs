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

    public static Color Background => Dark ? Color.FromArgb(30, 31, 34) : Color.FromArgb(245, 246, 248);

    public static Color Surface => Dark ? Color.FromArgb(42, 44, 48) : Color.White;

    public static Color Border => Dark ? Color.FromArgb(58, 61, 66) : Color.FromArgb(226, 229, 234);

    public static Color Text => Dark ? Color.FromArgb(232, 234, 237) : Color.FromArgb(22, 24, 29);

    public static Color TextMuted => Dark ? Color.FromArgb(154, 160, 166) : Color.FromArgb(107, 114, 128);

    public static Color Accent => Dark ? Color.FromArgb(77, 139, 255) : Color.FromArgb(37, 99, 235);

    public static Color AccentHover => Dark ? Color.FromArgb(102, 158, 255) : Color.FromArgb(59, 118, 240);

    public static Color AccentPressed => Dark ? Color.FromArgb(58, 116, 219) : Color.FromArgb(29, 78, 216);

    public static Color Neutral => Dark ? Color.FromArgb(63, 66, 72) : Color.FromArgb(124, 132, 143);

    public static Color NeutralHover => Dark ? Color.FromArgb(74, 78, 85) : Color.FromArgb(140, 148, 159);

    public static Color NeutralPressed => Dark ? Color.FromArgb(54, 57, 62) : Color.FromArgb(105, 112, 122);

    public static Color Secondary => Dark ? Color.FromArgb(46, 55, 74) : Color.FromArgb(232, 238, 252);

    public static Color SecondaryHover => Dark ? Color.FromArgb(56, 67, 90) : Color.FromArgb(219, 229, 250);

    public static Color SecondaryPressed => Dark ? Color.FromArgb(40, 48, 65) : Color.FromArgb(203, 217, 246);

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
}

/// <summary>A flat button with rounded corners and hover and pressed states.</summary>
internal sealed class RoundedButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public RoundedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
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
        g.Clear(Parent?.BackColor ?? BackColor);

        Color fill = !Enabled ? Theme.Neutral : _pressed ? FillPressed : _hovered ? FillHover : Fill;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

        using (GraphicsPath path = Theme.RoundedRectangle(bounds, Radius))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);

            if (Focused)
            {
                using var pen = new Pen(Color.FromArgb(120, Theme.Text), 2f);
                g.DrawPath(pen, path);
            }
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? Label : Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A rounded surface panel used to group content.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Background;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 10;

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? BackColor);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, Radius);
        using var fill = new SolidBrush(Theme.Surface);
        using var pen = new Pen(Theme.Border);

        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }
}

/// <summary>
/// Small colour indicator. Effects that use the stored colour show it directly, the others
/// show a spectrum, because that is what they actually do.
/// </summary>
internal sealed class Swatch : Control
{
    private Color _colour = Color.White;
    private bool _spectrum;

    public Swatch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void Show(Color colour, bool spectrum)
    {
        _colour = colour;
        _spectrum = spectrum;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? BackColor);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, 4);

        if (_spectrum)
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
            using var brush = new SolidBrush(_colour);
            g.FillPath(brush, path);
        }

        using var pen = new Pen(Theme.Border);
        g.DrawPath(pen, path);
    }
}
