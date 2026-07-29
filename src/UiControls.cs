using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// One entry of a <see cref="Select"/>. A mode of null draws no effect icon, unless
/// <paramref name="CustomColours"/> is given - used for custom presets, which mix effects
/// and so have no single mode of their own.
/// </summary>
internal sealed record SelectItem(string Key, string Text, byte? Mode, Color[]? CustomColours = null);

/// <summary>
/// The main switch. While the lighting is on it animates the effect that is running, so the
/// button itself is the status display.
/// </summary>
internal sealed class EffectButton : FlatControl
{
    // 30 fps: smooth for lighting effects that never move faster than about one cycle every
    // two seconds, and roughly halves the GDI+ and GC cost of the 60 fps rate this had before.
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private bool _on;
    private byte _mode;
    private Color _colour = Color.White;
    private bool _busy;
    private bool _paused;
    private bool _animate = true;

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

    /// <summary>Turns the animation off entirely, from the settings.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Animate
    {
        get => _animate;
        set
        {
            _animate = value;
            UpdateAnimation();
        }
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
        _timer.Enabled = _on && _animate && Visible && Enabled && !_busy && !_paused;
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
        Theme.Prepare(g);
        g.Clear(BackColor);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, Radius);

        if (_on && Enabled)
        {
            EffectPainter.Paint(g, path, bounds, _mode, _colour, _clock.Elapsed.TotalSeconds, _animate);

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

        var popup = new SelectPopup(_items, Selected, Colour, Width, Font);

        popup.Picked += (_, item) =>
        {
            if (item == Selected)
            {
                return;
            }

            Selected = item;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };

        popup.FormClosed += (_, _) =>
        {
            PopupClosed?.Invoke(this, EventArgs.Empty);
            popup.Dispose();
        };

        PopupOpening?.Invoke(this, EventArgs.Empty);
        popup.Open(PointToScreen(new Point(0, Height + 4)), FindForm());
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
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
        var icon = new Rectangle(left, (Height - 14) / 2, 22, 14);
        if (Selected?.Mode is byte mode)
        {
            EffectPainter.PaintIcon(g, icon, mode, Colour);
            left = icon.Right + 10;
        }
        else if (Selected?.CustomColours is { Length: > 0 } colours)
        {
            EffectPainter.PaintUserIcon(g, icon, colours);
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

    public event EventHandler<SelectItem>? Picked;

    /// <summary>
    /// Opens the list at a screen position. It is not modal, so a click on the window behind
    /// it, or anywhere else, closes it without picking anything.
    /// </summary>
    public void Open(Point at, IWin32Window? owner)
    {
        Rectangle screen = Screen.FromPoint(at).WorkingArea;
        int x = Math.Min(at.X, screen.Right - Width - 4);
        int y = at.Y + Height > screen.Bottom ? at.Y - Height - 42 : at.Y;
        Location = new Point(Math.Max(screen.Left + 4, x), Math.Max(screen.Top + 4, y));

        if (owner == null)
        {
            Show();
        }
        else
        {
            Show(owner);
        }

        Activate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.RoundWindowCorners(Handle);
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
                Choose(_highlighted);
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
            Choose(_hovered);
        }

        base.OnMouseClick(e);
    }

    private void Choose(int index)
    {
        if (index >= 0 && index < _items.Count)
        {
            Picked?.Invoke(this, _items[index]);
        }

        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);

        // Corner rounding for the window itself comes from the desktop compositor
        // (RoundWindowCorners). Drawing a second, independently anti-aliased rounded border
        // here fought the compositor's own hard clip and produced jagged, dark-fringed edges.
        using (var background = new SolidBrush(Theme.Surface))
        {
            g.FillRectangle(background, ClientRectangle);
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
            var icon = new Rectangle(left, row.Y + ((RowHeight - 14) / 2), 22, 14);
            if (_items[i].Mode is byte mode)
            {
                EffectPainter.PaintIcon(g, icon, mode, _colour);
                left = icon.Right + 10;
            }
            else if (_items[i].CustomColours is { Length: > 0 } colours)
            {
                EffectPainter.PaintUserIcon(g, icon, colours);
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
        Color.FromArgb(255, 56, 56),
        Color.FromArgb(255, 146, 30),
        Color.FromArgb(255, 214, 40),
        Color.FromArgb(60, 210, 96),
        Color.FromArgb(40, 208, 208),
        Color.FromArgb(64, 124, 255),
        Color.FromArgb(198, 76, 236),
    };

    private const int Chip = 24;
    private const int Gap = 9;
    private const int Inset = 4;

    private int _hoveredChip = -1;

    public ColourStrip()
    {
        Height = Chip + (Inset * 2);
        Width = ((Palette.Length + 1) * (Chip + Gap)) - Gap + (Inset * 2);
    }

    public event EventHandler? ColourPicked;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Colour { get; set; } = Color.White;

    private Rectangle ChipAt(int index) => new(Inset + (index * (Chip + Gap)), Inset, Chip, Chip);

    private int IndexAt(Point point)
    {
        for (int i = 0; i <= Palette.Length; i++)
        {
            if (Rectangle.Inflate(ChipAt(i), 2, 2).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int hovered = IndexAt(e.Location);
        if (hovered != _hoveredChip)
        {
            _hoveredChip = hovered;
            Cursor = hovered >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoveredChip = -1;
        Cursor = Cursors.Default;
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
            Invalidate();
            ColourPicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        var box = ChipAt(_hoveredChip);
        var popup = new ColourPickerPopup(Colour);
        popup.ColourChanged += (_, colour) =>
        {
            Colour = colour;
            Invalidate();
            ColourPicked?.Invoke(this, EventArgs.Empty);
        };
        popup.FormClosed += (_, _) => popup.Dispose();
        popup.Open(PointToScreen(new Point(box.X, box.Bottom + 6)), FindForm());
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        g.Clear(BackColor);

        bool custom = !Array.Exists(Palette, entry => Same(entry, Colour));

        for (int i = 0; i <= Palette.Length; i++)
        {
            Rectangle box = ChipAt(i);
            bool isCustomChip = i == Palette.Length;
            bool active = isCustomChip ? custom : Same(Palette[i], Colour);

            if (i == _hoveredChip || active)
            {
                using var ring = new Pen(active ? Theme.Accent : Color.FromArgb(120, Theme.Accent), 2f);
                g.DrawEllipse(ring, Rectangle.Inflate(box, 3, 3));
            }

            if (isCustomChip)
            {
                PaintCustomChip(g, box, custom ? Colour : Color.Empty);
            }
            else
            {
                using var brush = new SolidBrush(Palette[i]);
                g.FillEllipse(brush, box);
            }

            // Pale chips need a firmer outline, otherwise white vanishes on a light window.
            Color chip = isCustomChip ? Colour : Palette[i];
            double luminance = ((chip.R * 0.299) + (chip.G * 0.587) + (chip.B * 0.114)) / 255.0;
            using var outline = new Pen(Color.FromArgb(luminance > 0.75 ? 120 : 52, 0, 0, 0));
            g.DrawEllipse(outline, box);

            if (active)
            {
                PaintTick(g, box, isCustomChip ? Colour : Palette[i]);
            }
        }
    }

    /// <summary>The free choice chip: a spectrum, or the picked colour once one is set.</summary>
    private static void PaintCustomChip(Graphics g, Rectangle box, Color picked)
    {
        if (picked != Color.Empty)
        {
            using var solid = new SolidBrush(picked);
            g.FillEllipse(solid, box);
            return;
        }

        using var brush = new LinearGradientBrush(box, Color.Red, Color.Red, LinearGradientMode.ForwardDiagonal)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = new[]
                {
                    Color.FromArgb(255, 0, 0), Color.FromArgb(255, 220, 0), Color.FromArgb(0, 220, 90),
                    Color.FromArgb(0, 190, 255), Color.FromArgb(90, 80, 255), Color.FromArgb(230, 60, 220),
                },
                Positions = new[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f },
            },
        };

        g.FillEllipse(brush, box);
    }

    /// <summary>Tick in whichever of black or white stays readable on the chip.</summary>
    private static void PaintTick(Graphics g, Rectangle box, Color background)
    {
        double luminance = ((background.R * 0.299) + (background.G * 0.587) + (background.B * 0.114)) / 255.0;
        using var pen = new Pen(luminance > 0.6 ? Color.FromArgb(30, 32, 36) : Color.White, 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        float x = box.X + (box.Width * 0.28f);
        float y = box.Y + (box.Height * 0.52f);
        g.DrawLines(pen, new[]
        {
            new PointF(x, y),
            new PointF(x + (box.Width * 0.15f), y + (box.Height * 0.18f)),
            new PointF(x + (box.Width * 0.44f), y - (box.Height * 0.22f)),
        });
    }

    private static bool Same(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;
}

/// <summary>Small square button carrying the settings gear.</summary>
internal sealed class GlyphButton : FlatControl
{
    public GlyphButton()
    {
        Radius = 8;
        Size = new Size(30, 30);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        g.Clear(BackColor);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, Radius);

        if (Hovered || Focused)
        {
            using var brush = new SolidBrush(Pressed ? Theme.Border : Theme.NeutralSoft);
            g.FillPath(brush, path);
        }

        DrawFocusRing(g, path);

        using GraphicsPath gear = EffectPainter.GearPath(
            new PointF(Width / 2f, Height / 2f), Math.Min(Width, Height) * 0.32f);
        using var ink = new SolidBrush(Hovered ? Theme.Text : Theme.TextMuted);
        g.FillPath(ink, gear);
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
        Theme.Prepare(g);
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

/// <summary>
/// A small, themed colour picker: a hue strip, a saturation/value square and a hex field.
/// Opens where <see cref="ColourStrip"/>'s custom chip is clicked, replacing the plain
/// Windows colour dialog that used to sit there.
/// </summary>
internal sealed class ColourPickerPopup : Form
{
    private const int Pad = 14;
    private const int SvSize = 168;
    private const int HueHeight = 16;
    private const int Gap = 10;
    private const int SwatchSize = 30;

    // The hue strip is the same for every instance and every hue, so it is built once.
    private static readonly Bitmap HueStripBitmap = BuildHueStrip();

    private readonly TextBox _hex;
    private readonly Rectangle _svRect = new(Pad, Pad, SvSize, SvSize);
    private readonly Rectangle _hueRect = new(Pad, Pad + SvSize + Gap, SvSize, HueHeight);

    private double _hue;
    private double _saturation;
    private double _value;
    private Bitmap? _svBitmap;
    private bool _draggingSv;
    private bool _draggingHue;

    public ColourPickerPopup(Color initial)
    {
        (_hue, _saturation, _value) = Theme.ToHsv(initial);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9F);
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(Pad + SvSize + Pad, Pad + SvSize + Gap + HueHeight + Gap + SwatchSize + Pad);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _hex = new TextBox
        {
            Location = new Point(Pad + SwatchSize + 10, Pad + SvSize + Gap + HueHeight + Gap + 6),
            Width = SvSize - SwatchSize - 10,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            MaxLength = 7,
            Text = Hex(Current),
        };
        _hex.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyHex();
                e.SuppressKeyPress = true;
            }
        };
        _hex.Leave += (_, _) => ApplyHex();
        Controls.Add(_hex);

        RebuildSvBitmap();
    }

    public event EventHandler<Color>? ColourChanged;

    public Color Current => Theme.FromHsv(_hue, _saturation, _value);

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return parameters;
        }
    }

    /// <summary>Opens at a screen position. Not modal - a click anywhere else closes it.</summary>
    public void Open(Point at, IWin32Window? owner)
    {
        Rectangle screen = Screen.FromPoint(at).WorkingArea;
        int x = Math.Min(at.X, screen.Right - Width - 4);
        int y = at.Y + Height > screen.Bottom ? at.Y - Height - 6 : at.Y;
        Location = new Point(Math.Max(screen.Left + 4, x), Math.Max(screen.Top + 4, y));

        if (owner == null)
        {
            Show();
        }
        else
        {
            Show(owner);
        }

        Activate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.RoundWindowCorners(Handle);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private static Bitmap BuildHueStrip()
    {
        var bitmap = new Bitmap(SvSize, HueHeight, PixelFormat.Format24bppRgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, SvSize, HueHeight), ImageLockMode.WriteOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            byte[] buffer = new byte[data.Stride * HueHeight];
            for (int x = 0; x < SvSize; x++)
            {
                Color c = Theme.FromHsv(x * 360.0 / SvSize, 1, 1);
                for (int y = 0; y < HueHeight; y++)
                {
                    int i = (y * data.Stride) + (x * 3);
                    buffer[i] = c.B;
                    buffer[i + 1] = c.G;
                    buffer[i + 2] = c.R;
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    /// <summary>
    /// Rebuilt only when the hue changes (not on every paint), using raw pixel writes rather
    /// than SetPixel so dragging the hue strip stays smooth.
    /// </summary>
    private void RebuildSvBitmap()
    {
        _svBitmap?.Dispose();
        var bitmap = new Bitmap(SvSize, SvSize, PixelFormat.Format24bppRgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, SvSize, SvSize), ImageLockMode.WriteOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            byte[] buffer = new byte[data.Stride * SvSize];
            for (int y = 0; y < SvSize; y++)
            {
                double v = 1.0 - (y / (double)(SvSize - 1));
                int rowOffset = y * data.Stride;
                for (int x = 0; x < SvSize; x++)
                {
                    double s = x / (double)(SvSize - 1);
                    Color c = Theme.FromHsv(_hue, s, v);
                    int i = rowOffset + (x * 3);
                    buffer[i] = c.B;
                    buffer[i + 1] = c.G;
                    buffer[i + 2] = c.R;
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        _svBitmap = bitmap;
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private void ApplyHex()
    {
        string text = _hex.Text.Trim().TrimStart('#');
        if (text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            var colour = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            (_hue, _saturation, _value) = Theme.ToHsv(colour);
            RebuildSvBitmap();
            Invalidate();
            ColourChanged?.Invoke(this, colour);
        }
        else
        {
            _hex.Text = Hex(Current);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_svRect.Contains(e.Location))
        {
            _draggingSv = true;
            UpdateFromSv(e.Location);
        }
        else if (_hueRect.Contains(e.Location))
        {
            _draggingHue = true;
            UpdateFromHue(e.Location);
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_draggingSv)
        {
            UpdateFromSv(e.Location);
        }
        else if (_draggingHue)
        {
            UpdateFromHue(e.Location);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_draggingSv || _draggingHue)
        {
            _draggingSv = false;
            _draggingHue = false;
            ColourChanged?.Invoke(this, Current);
        }

        base.OnMouseUp(e);
    }

    private void UpdateFromSv(Point at)
    {
        _saturation = Math.Clamp((at.X - _svRect.X) / (double)(_svRect.Width - 1), 0, 1);
        _value = 1.0 - Math.Clamp((at.Y - _svRect.Y) / (double)(_svRect.Height - 1), 0, 1);
        _hex.Text = Hex(Current);
        Invalidate();
    }

    private void UpdateFromHue(Point at)
    {
        _hue = Math.Clamp((at.X - _hueRect.X) / (double)(_hueRect.Width - 1), 0, 1) * 360.0;
        RebuildSvBitmap();
        _hex.Text = Hex(Current);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _svBitmap?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        g.Clear(Theme.Surface);

        if (_svBitmap != null)
        {
            g.DrawImage(_svBitmap, _svRect);
        }

        var svMarker = new Point(
            _svRect.X + (int)(_saturation * (_svRect.Width - 1)),
            _svRect.Y + (int)((1 - _value) * (_svRect.Height - 1)));
        PaintRing(g, svMarker, 6, Current);

        g.DrawImage(HueStripBitmap, _hueRect);
        int hueX = _hueRect.X + (int)(_hue / 360.0 * (_hueRect.Width - 1));
        using (var huePen = new Pen(Color.White, 2f))
        {
            g.DrawLine(huePen, hueX, _hueRect.Y - 2, hueX, _hueRect.Bottom + 2);
        }
        using (var hueOutline = new Pen(Color.FromArgb(90, 0, 0, 0), 1f))
        {
            g.DrawLine(hueOutline, hueX - 1, _hueRect.Y - 2, hueX - 1, _hueRect.Bottom + 2);
            g.DrawLine(hueOutline, hueX + 1, _hueRect.Y - 2, hueX + 1, _hueRect.Bottom + 2);
        }

        var swatch = new Rectangle(Pad, _hueRect.Bottom + Gap, SwatchSize, SwatchSize);
        using (GraphicsPath swatchPath = Theme.RoundedRectangle(swatch, 6))
        using (var swatchBrush = new SolidBrush(Current))
        {
            g.FillPath(swatchBrush, swatchPath);
            using var outline = new Pen(Theme.Border);
            g.DrawPath(outline, swatchPath);
        }
    }

    private static void PaintRing(Graphics g, Point at, int radius, Color fill)
    {
        var box = new Rectangle(at.X - radius, at.Y - radius, radius * 2, radius * 2);
        using (var brush = new SolidBrush(fill))
        {
            g.FillEllipse(brush, box);
        }

        using var white = new Pen(Color.White, 2f);
        g.DrawEllipse(white, box);
        using var black = new Pen(Color.FromArgb(140, 0, 0, 0), 1f);
        g.DrawEllipse(black, Rectangle.Inflate(box, 1, 1));
    }
}

/// <summary>A plain flat button with a text label, for actions that are not the primary switch.</summary>
internal sealed class PillButton : FlatControl
{
    public PillButton()
    {
        Radius = 8;
        ForeColor = Theme.Accent;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; } = Theme.AccentSoft;

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        g.Clear(BackColor);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, Radius);

        Color fill = !Enabled ? Theme.NeutralSoft : Hovered ? Theme.Blend(Fill, Color.White, 0.10) : Fill;
        using (var brush = new SolidBrush(Pressed ? Theme.Scale(fill, 0.94) : fill))
        {
            g.FillPath(brush, path);
        }

        DrawFocusRing(g, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Small round × button, used to delete a custom preset from its list row.</summary>
internal sealed class DeleteButton : FlatControl
{
    public DeleteButton()
    {
        Radius = 6;
        Size = new Size(22, 22);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        g.Clear(BackColor);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, Radius);

        if (Hovered)
        {
            using var brush = new SolidBrush(Theme.NeutralSoft);
            g.FillPath(brush, path);
        }

        DrawFocusRing(g, path);

        using var pen = new Pen(Hovered ? Theme.Text : Theme.TextMuted, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float m = Width * 0.28f;
        g.DrawLine(pen, m, m, Width - m, Height - m);
        g.DrawLine(pen, Width - m, m, m, Height - m);
    }
}
