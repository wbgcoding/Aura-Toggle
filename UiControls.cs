using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>One entry of a <see cref="Select"/>. A mode of null draws no effect icon.</summary>
internal sealed record SelectItem(string Key, string Text, byte? Mode);

/// <summary>
/// The main switch. While the lighting is on it animates the effect that is running, so the
/// button itself is the status display.
/// </summary>
internal sealed class EffectButton : FlatControl
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 40 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private bool _on;
    private byte _mode;
    private Color _colour = Color.White;
    private bool _busy;
    private bool _paused;

    public EffectButton()
    {
        Radius = 16;
        _timer.Tick += (_, _) => Invalidate();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Busy
    {
        get => _busy;
        set
        {
            _busy = value;
            UpdateAnimation();
        }
    }

    public void Show(bool on, byte mode, Color colour)
    {
        _on = on;
        _mode = mode;
        _colour = colour;
        UpdateAnimation();
    }

    /// <summary>Stops the animation while the window is minimised.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            UpdateAnimation();
        }
    }

    /// <summary>Animating an unlit board would be a lie, and it would burn cycles for nothing.</summary>
    private void UpdateAnimation()
    {
        _timer.Enabled = _on && Visible && Enabled && !_busy && !_paused;
        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        UpdateAnimation();
        base.OnVisibleChanged(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        UpdateAnimation();
        base.OnEnabledChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, Radius);

        if (_on && Enabled)
        {
            EffectPainter.Paint(g, path, bounds, _mode, _colour, _clock.Elapsed.TotalSeconds);

            // A dark wash keeps the label readable over bright and pale effects alike.
            using var wash = new SolidBrush(Color.FromArgb(Hovered ? 52 : 74, 0, 0, 0));
            g.FillPath(wash, path);
        }
        else
        {
            Color fill = !Enabled
                ? Theme.NeutralSoft
                : Hovered ? Theme.Blend(Theme.Neutral, Color.White, 0.12) : Theme.Neutral;

            using var brush = new SolidBrush(Pressed ? Theme.Scale(fill, 0.9) : fill);
            g.FillPath(brush, path);
        }

        if (_busy)
        {
            using var dim = new SolidBrush(Color.FromArgb(110, BackColor));
            g.FillPath(dim, path);
        }

        DrawFocusRing(g, path);

        // A soft shadow keeps the label legible even over the brightest frame of an effect.
        foreach (Point offset in new[] { new Point(0, 1), new Point(1, 0), new Point(0, -1), new Point(-1, 0) })
        {
            TextRenderer.DrawText(g, Text, Font, new Rectangle(offset.X, offset.Y, Width, Height),
                Color.FromArgb(120, 0, 0, 0),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? Color.White : Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>A rounded drop down that opens a themed popup instead of the system list.</summary>
internal sealed class Select : FlatControl
{
    private readonly List<SelectItem> _items = new();

    public Select()
    {
        Radius = 9;
        Height = 34;
    }

    public event EventHandler? SelectionChanged;

    /// <summary>Raised around the drop down, so a hosting popup can stay open meanwhile.</summary>
    public event EventHandler? PopupOpening;

    public event EventHandler? PopupClosed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SelectItem? Selected { get; private set; }

    /// <summary>Colour handed to the effect icons.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Colour { get; set; } = Color.White;

    public void SetItems(IEnumerable<SelectItem> items)
    {
        _items.Clear();
        _items.AddRange(items);

        if (Selected != null)
        {
            Selected = _items.FirstOrDefault(item => item.Key == Selected.Key);
        }

        Invalidate();
    }

    /// <summary>Shows an entry without raising <see cref="SelectionChanged"/>.</summary>
    public void ShowSelection(string key)
    {
        Selected = _items.FirstOrDefault(item => item.Key == key) ?? _items.FirstOrDefault();
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);

        if (_items.Count == 0 || !Enabled)
        {
            return;
        }

        SelectItem? chosen;
        PopupOpening?.Invoke(this, EventArgs.Empty);
        try
        {
            using var popup = new SelectPopup(_items, Selected, Colour, Width, Font);
            chosen = popup.Choose(PointToScreen(new Point(0, Height + 4)));
        }
        finally
        {
            PopupClosed?.Invoke(this, EventArgs.Empty);
        }

        if (chosen == null || chosen == Selected)
        {
            return;
        }

        Selected = chosen;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, Radius);

        using (var fill = new SolidBrush(Enabled && Hovered ? Theme.SurfaceHover : Theme.Surface))
        using (var pen = new Pen(Enabled && Hovered ? Theme.Accent : Theme.Border))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        DrawFocusRing(g, path);

        int left = 11;
        if (Selected?.Mode is byte mode)
        {
            var icon = new Rectangle(left, (Height - 14) / 2, 22, 14);
            EffectPainter.PaintIcon(g, icon, mode, Colour);
            left = icon.Right + 10;
        }

        var text = new Rectangle(left, 0, Math.Max(0, Width - left - 26), Height);
        TextRenderer.DrawText(g, Selected?.Text ?? "", Font, text, Enabled ? Theme.Text : Theme.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        PaintChevron(g, new Point(Width - 16, Height / 2), Theme.TextMuted);
    }

    internal static void PaintChevron(Graphics g, Point centre, Color colour)
    {
        using var pen = new Pen(colour, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, new[]
        {
            new Point(centre.X - 4, centre.Y - 2),
            new Point(centre.X, centre.Y + 2),
            new Point(centre.X + 4, centre.Y - 2),
        });
    }
}

/// <summary>The themed list that <see cref="Select"/> drops down.</summary>
internal sealed class SelectPopup : Form
{
    private const int RowHeight = 30;
    private const int Inset = 6;

    private readonly List<SelectItem> _items;
    private readonly Color _colour;
    private int _highlighted;
    private int _hovered = -1;
    private SelectItem? _result;

    public SelectPopup(List<SelectItem> items, SelectItem? selected, Color colour, int width, Font font)
    {
        _items = items;
        _colour = colour;
        _highlighted = selected == null ? 0 : Math.Max(0, items.IndexOf(selected));

        Font = font;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(width, (items.Count * RowHeight) + (Inset * 2));
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
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

    /// <summary>Opens the list at a screen position and returns what the user picked.</summary>
    public SelectItem? Choose(Point at)
    {
        Rectangle screen = Screen.FromPoint(at).WorkingArea;
        int x = Math.Min(at.X, screen.Right - Width - 4);
        int y = at.Y + Height > screen.Bottom ? at.Y - Height - 42 : at.Y;
        Location = new Point(Math.Max(screen.Left + 4, x), Math.Max(screen.Top + 4, y));

        ShowDialog();
        return _result;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        using GraphicsPath frame = Theme.RoundedRectangle(new RectangleF(0, 0, Width, Height), 10);
        Region = new Region(frame);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                Close();
                break;

            case Keys.Down:
            case Keys.Up:
                _highlighted = Math.Clamp(_highlighted + (e.KeyCode == Keys.Down ? 1 : -1), 0, _items.Count - 1);
                _hovered = -1;
                Invalidate();
                break;

            case Keys.Enter:
            case Keys.Space:
                _result = _items[_highlighted];
                Close();
                break;
        }

        e.Handled = true;
        base.OnKeyDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = (e.Y - Inset) / RowHeight;
        int hovered = e.Y >= Inset && index >= 0 && index < _items.Count ? index : -1;

        if (hovered != _hovered)
        {
            _hovered = hovered;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (_hovered >= 0)
        {
            _result = _items[_hovered];
            Close();
        }

        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var background = new SolidBrush(Theme.Surface))
        {
            g.FillRectangle(background, ClientRectangle);
        }

        using (var border = new Pen(Theme.Border))
        using (GraphicsPath frame = Theme.RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 10))
        {
            g.DrawPath(border, frame);
        }

        for (int i = 0; i < _items.Count; i++)
        {
            var row = new Rectangle(Inset, Inset + (i * RowHeight), Width - (Inset * 2), RowHeight);
            bool active = _hovered >= 0 ? i == _hovered : i == _highlighted;

            if (active)
            {
                using GraphicsPath highlight = Theme.RoundedRectangle(row, 7);
                using var brush = new SolidBrush(Theme.AccentSoft);
                g.FillPath(brush, highlight);
            }

            int left = row.X + 8;
            if (_items[i].Mode is byte mode)
            {
                var icon = new Rectangle(left, row.Y + ((RowHeight - 14) / 2), 22, 14);
                EffectPainter.PaintIcon(g, icon, mode, _colour);
                left = icon.Right + 10;
            }

            var text = new Rectangle(left, row.Y, Math.Max(0, row.Right - left - 6), RowHeight);
            TextRenderer.DrawText(g, _items[i].Text, Font, text, Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>Round colour chips for the effects that run in a colour of your choosing.</summary>
internal sealed class ColourStrip : FlatControl
{
    private static readonly Color[] Palette =
    {
        Color.FromArgb(255, 255, 255),
        Color.FromArgb(255, 45, 45),
        Color.FromArgb(255, 140, 20),
        Color.FromArgb(255, 220, 40),
        Color.FromArgb(60, 210, 90),
        Color.FromArgb(40, 210, 210),
        Color.FromArgb(60, 120, 255),
        Color.FromArgb(200, 70, 235),
    };

    private const int Chip = 22;
    private const int Gap = 8;

    private int _hoveredChip = -1;

    public ColourStrip()
    {
        Height = Chip + 2;
        Width = (Palette.Length + 1) * (Chip + Gap);
    }

    public event EventHandler? ColourPicked;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Colour { get; set; } = Color.White;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = e.X / (Chip + Gap);
        int hovered = index >= 0 && index <= Palette.Length && (e.X % (Chip + Gap)) <= Chip ? index : -1;

        if (hovered != _hoveredChip)
        {
            _hoveredChip = hovered;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoveredChip = -1;
        base.OnMouseLeave(e);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);

        if (_hoveredChip < 0)
        {
            return;
        }

        if (_hoveredChip < Palette.Length)
        {
            Colour = Palette[_hoveredChip];
        }
        else
        {
            using var dialog = new ColorDialog { Color = Colour, FullOpen = true, AnyColor = true };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }

            Colour = dialog.Color;
        }

        Invalidate();
        ColourPicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        for (int i = 0; i <= Palette.Length; i++)
        {
            var box = new Rectangle(i * (Chip + Gap), 1, Chip, Chip);
            bool custom = i == Palette.Length;
            Color colour = custom ? Colour : Palette[i];
            bool active = !custom && SameColour(colour, Colour);

            if (custom)
            {
                PaintCustomChip(g, box);
            }
            else
            {
                using var brush = new SolidBrush(colour);
                g.FillEllipse(brush, box);
            }

            using var outline = new Pen(active ? Theme.Accent : Theme.Border, active ? 2f : 1f);
            g.DrawEllipse(outline, box);

            if (i == _hoveredChip)
            {
                using var hover = new Pen(Color.FromArgb(150, Theme.Accent), 2f);
                g.DrawEllipse(hover, Rectangle.Inflate(box, 2, 2));
            }
        }
    }

    private static void PaintCustomChip(Graphics g, Rectangle box)
    {
        using var brush = new LinearGradientBrush(box, Color.Red, Color.Red, LinearGradientMode.ForwardDiagonal)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = new[]
                {
                    Color.FromArgb(255, 0, 0), Color.FromArgb(255, 255, 0), Color.FromArgb(0, 255, 0),
                    Color.FromArgb(0, 255, 255), Color.FromArgb(0, 0, 255), Color.FromArgb(255, 0, 255),
                },
                Positions = new[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f },
            },
        };

        g.FillEllipse(brush, box);
    }

    private static bool SameColour(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;
}

/// <summary>Small square button carrying a glyph, used for the settings gear.</summary>
internal sealed class GlyphButton : FlatControl
{
    public GlyphButton()
    {
        Radius = 8;
        Size = new Size(28, 28);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, Radius);

        if (Hovered || Focused)
        {
            using var brush = new SolidBrush(Pressed ? Theme.Border : Theme.NeutralSoft);
            g.FillPath(brush, path);
        }

        DrawFocusRing(g, path);
        PaintGear(g, new PointF(Width / 2f, Height / 2f), Math.Min(Width, Height) * 0.26f,
            Hovered ? Theme.Text : Theme.TextMuted);
    }

    private static void PaintGear(Graphics g, PointF centre, float radius, Color colour)
    {
        using var pen = new Pen(colour, 1.5f);

        // Eight teeth as short spokes plus a ring: crisp at small sizes, no icon file needed.
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4;
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);
            g.DrawLine(pen,
                centre.X + (cos * radius * 0.95f), centre.Y + (sin * radius * 0.95f),
                centre.X + (cos * radius * 1.55f), centre.Y + (sin * radius * 1.55f));
        }

        g.DrawEllipse(pen, centre.X - radius, centre.Y - radius, radius * 2, radius * 2);
    }
}

/// <summary>A small on/off switch for the settings popup.</summary>
internal sealed class ToggleSwitch : FlatControl
{
    private bool _checked;

    public ToggleSwitch()
    {
        Size = new Size(38, 22);
    }

    public event EventHandler? CheckedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            Invalidate();
        }
    }

    protected override void OnClick(EventArgs e)
    {
        _checked = !_checked;
        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, Height / 2f);

        Color track = _checked ? Theme.Accent : Theme.Neutral;
        using (var brush = new SolidBrush(Hovered ? Theme.Blend(track, Color.White, 0.12) : track))
        {
            g.FillPath(brush, path);
        }

        DrawFocusRing(g, path);

        float knob = Height - 6;
        float x = _checked ? Width - knob - 3 : 3;
        using var knobBrush = new SolidBrush(Color.White);
        g.FillEllipse(knobBrush, x, 3, knob, knob);
    }
}
