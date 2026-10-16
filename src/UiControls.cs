using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// The main switch. While the lighting is on it animates the effect that is running, so the
/// button itself is the status display.
/// </summary>
internal sealed class EffectButton : FlatControl
{
    // 30 fps: smooth for lighting effects that never move faster than about one cycle every
    // two seconds, and roughly halves the GDI+ and GC cost of 60 fps.
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly EffectSurface _surface = new();

    /// <summary>The label's drop-shadow offsets, fixed - hoisted out of OnPaint so a repaint at
    /// 30 fps does not allocate a new array every time.</summary>
    private static readonly Point[] ShadowOffsets =
    {
        new(0, 1), new(1, 0), new(0, -1), new(-1, 0),
    };

    /// <summary>Gap between letters of the ON/OFF label, at 96 dpi - GDI+ has no letter-spacing
    /// of its own, so this is added by hand between characters drawn one at a time.</summary>
    private const int LetterSpacingAt96 = 4;

    private const TextFormatFlags LabelFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

    /// <summary>The label these measurements belong to - <see cref="MeasureLabel"/>.</summary>
    private string _labelSource = "";
    private int _labelSpacing = -1;
    private string[] _labelChars = Array.Empty<string>();
    private int[] _labelWidths = Array.Empty<int>();
    private int _labelWidth;
    private int _labelHeight;

    private bool _on;
    private byte _mode;
    private Color _colour = Color.White;
    private bool _busy;
    private bool _paused;
    private bool _animate = true;

    /// <summary>The bold variant of <see cref="Control.Font"/>, rebuilt only when that changes -
    /// which happens on every repaint's font lookup otherwise, 30 times a second while animating.</summary>
    private Font? _boldFont;

    // Shared rather than allocated per call: painting only ever happens on the UI thread, each
    // call sets the colour and uses the brush immediately, and this paints at up to 30 fps.
    private readonly SolidBrush _washBrush = new(Color.Black);
    private readonly SolidBrush _dimBrush = new(Color.Black);
    private readonly SolidBrush _fillBrush = new(Color.Black);

    public EffectButton()
    {
        Radius = 16;
        AccessibleRole = AccessibleRole.CheckButton;
        _timer.Tick += (_, _) => Invalidate();
    }

    /// <summary>The button is a switch: a screen reader should hear "checked" while the lighting is on.</summary>
    protected override AccessibleObject CreateAccessibilityInstance() => new ButtonAccessibleObject(this);

    private sealed class ButtonAccessibleObject : ControlAccessibleObject
    {
        public ButtonAccessibleObject(EffectButton owner) : base(owner)
        {
        }

        public override AccessibleStates State =>
            base.State | (((EffectButton)Owner!)._on ? AccessibleStates.Checked : AccessibleStates.None);
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

    /// <summary>
    /// What the button is animating right now, for the layout review to report. A custom preset
    /// has no single effect of its own, so this is the one place the choice it makes - the effect
    /// most of its channels run - can be read back instead of judged from a moving picture.
    /// </summary>
    internal (byte Mode, Color Colour) Showing => (_mode, _colour);

    public void Show(bool on, byte mode, Color colour)
    {
        bool changed = _on != on;
        _on = on;
        _mode = mode;
        _colour = colour;
        UpdateAnimation();

        if (changed)
        {
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        }
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

    /// <summary>
    /// Animating an unlit board would be a lie, and it would burn cycles for nothing. A static
    /// colour has no time component either (<see cref="EffectPainter.Render"/> fills it flat), so
    /// it needs no timer; hover, press and focus repaint through their own invalidations.
    /// </summary>
    private void UpdateAnimation()
    {
        _timer.Enabled = _on && _animate && Visible && Enabled && !_busy && !_paused &&
            _mode != AuraState.ModeStatic;
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
            _surface.Dispose();
            _boldFont?.Dispose();
            _washBrush.Dispose();
            _dimBrush.Dispose();
            _fillBrush.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>WinForms hands this a freshly scaled <see cref="Control.Font"/> on a display-scale
    /// change, so the bold variant built from the previous one has to go with it.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        _boldFont?.Dispose();
        _boldFont = null;

        // The cached label widths were measured with the font that just went - a display-scale
        // change would otherwise keep drawing the state word at the old size's spacing.
        _labelSpacing = -1;

        base.OnFontChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        g.Clear(BackColor);

        var bounds = new Rectangle(0, 0, Width, Height);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, Radius);

        if (_on && Enabled)
        {
            // Effect, label wash and busy dim are all drawn into one buffer and masked through
            // the rounded path once. Filling the path per pass smudged the edge every time.
            double seconds = _clock.Elapsed.TotalSeconds;
            _surface.Paint(g, path, bounds, surface =>
            {
                var area = new RectangleF(0, 0, bounds.Width, bounds.Height);
                EffectPainter.Render(surface, area, _mode, _colour, seconds, _animate);

                // A dark wash keeps the label readable over bright and pale effects alike.
                _washBrush.Color = Color.FromArgb(Hovered ? 52 : 74, 0, 0, 0);
                surface.FillRectangle(_washBrush, area);

                if (_busy)
                {
                    _dimBrush.Color = Color.FromArgb(110, BackColor);
                    surface.FillRectangle(_dimBrush, area);
                }
            });
        }
        else
        {
            Color fill = !Enabled
                ? Theme.NeutralSoft
                : Hovered ? Theme.Blend(Theme.Neutral, Color.White, 0.12) : Theme.Neutral;

            fill = Pressed ? Theme.Scale(fill, 0.9) : fill;

            // Blended rather than washed over: a second fill of the same path would blend into
            // its anti-aliased edge again and leave a dark rim.
            _fillBrush.Color = _busy ? Theme.Blend(fill, BackColor, 110 / 255.0) : fill;
            g.FillPath(_fillBrush, path);
        }

        DrawFocusRing(g, path);
        DrawTrackedLabel(g);
    }

    /// <summary>
    /// The state word in wide upper case, drawn one letter at a time with a fixed gap between
    /// them - GDI+ has no letter-spacing to ask for. Upper case is applied here, at paint time
    /// only: <see cref="Control.Text"/> itself stays "On"/"Off" (or "An"/"Aus"), which is what a
    /// screen reader spells out, so it must never become "O N" or a pre-uppercased string.
    /// </summary>
    private void DrawTrackedLabel(Graphics g)
    {
        if (Text.Length == 0)
        {
            return;
        }

        _boldFont ??= new Font(Font, FontStyle.Bold);
        int spacing = this.Scaled(LetterSpacingAt96);

        if (_labelSource != Text || _labelSpacing != spacing)
        {
            MeasureLabel(g, spacing);
        }

        int baseX = (Width - _labelWidth) / 2;
        int baseY = (Height - _labelHeight) / 2;

        void Draw(int dx, int dy, Color colour)
        {
            int x = baseX + dx;
            for (int i = 0; i < _labelChars.Length; i++)
            {
                TextRenderer.DrawText(g, _labelChars[i], _boldFont, new Point(x, baseY + dy), colour, LabelFlags);
                x += _labelWidths[i] + spacing;
            }
        }

        // A soft shadow keeps the label legible even over the brightest frame of an effect.
        foreach (Point offset in ShadowOffsets)
        {
            Draw(offset.X, offset.Y, Color.FromArgb(120, 0, 0, 0));
        }

        Draw(0, 0, Enabled ? Color.White : Theme.TextMuted);
    }

    /// <summary>
    /// Works the label out once for a given text, font and letter gap. At 30 fps the state word is
    /// the same three characters on every frame, and each one would otherwise cost a
    /// <see cref="TextRenderer.MeasureText(Graphics, string, Font, Size, TextFormatFlags)"/> - a
    /// GDI call - plus a string per character per pass, five passes deep for the shadow - work this cache turns into nothing.
    /// </summary>
    private void MeasureLabel(Graphics g, int spacing)
    {
        string label = Text.ToUpperInvariant();

        _labelChars = new string[label.Length];
        _labelWidths = new int[label.Length];
        _labelWidth = 0;
        _labelHeight = 0;

        for (int i = 0; i < label.Length; i++)
        {
            _labelChars[i] = label[i].ToString();
            Size measured = TextRenderer.MeasureText(g, _labelChars[i], _boldFont,
                new Size(int.MaxValue, int.MaxValue), LabelFlags);
            _labelWidths[i] = measured.Width;
            _labelWidth += measured.Width + (i > 0 ? spacing : 0);
            _labelHeight = Math.Max(_labelHeight, measured.Height);
        }

        _labelSource = Text;
        _labelSpacing = spacing;
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
        Color.FromArgb(255, 40, 150),
    };

    private const int ChipAt96 = 24;
    private const int GapAt96 = 9;
    private const int InsetAt96 = 4;

    // The chips are laid out from the current dpi; the strip's own size comes from DesignSize.
    private int Chip => this.Scaled(ChipAt96);

    private int Gap => this.Scaled(GapAt96);

    private int Inset => this.Scaled(InsetAt96);

    private int _hoveredChip = -1;

    /// <summary>Which chip the keyboard is on, so the strip is not a dead tab stop.</summary>
    private int _focusedChip;

    public ColourStrip()
    {
        DesignSize = new Size(
            ((Palette.Length + 1) * (ChipAt96 + GapAt96)) - GapAt96 + (InsetAt96 * 2),
            ChipAt96 + (InsetAt96 * 2));
        AccessibleName = Strings.ColourAccessibleName;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int last = Palette.Length; // the free-choice chip sits after the palette
        int moved = e.KeyCode switch
        {
            Keys.Left => _focusedChip - 1,
            Keys.Right => _focusedChip + 1,
            Keys.Home => 0,
            Keys.End => last,
            _ => _focusedChip,
        };

        if (moved != _focusedChip && moved >= 0 && moved <= last)
        {
            _focusedChip = moved;
            Invalidate();
            e.Handled = true;
            return;
        }

        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Choose(_focusedChip);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        // Start on the chip that is actually selected, not always on the first one.
        int selected = Array.FindIndex(Palette, entry => Same(entry, Colour));
        _focusedChip = selected >= 0 ? selected : Palette.Length;
        base.OnGotFocus(e);
    }

    public event EventHandler? ColourPicked;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Colour { get; set; } = Color.White;

    private Rectangle ChipAt(int index) => new(Inset + (index * (Chip + Gap)), Inset, Chip, Chip);

    private int IndexAt(Point point)
    {
        for (int i = 0; i <= Palette.Length; i++)
        {
            if (Rectangle.Inflate(ChipAt(i), Inset / 2, Inset / 2).Contains(point))
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
        Choose(_hoveredChip);
    }

#if REVIEW
    /// <summary>Surface tool only: the colour picker, as a click on the custom chip opens it.</summary>
    internal void OpenPickerForReview() => Choose(Palette.Length);
#endif

    /// <summary>Picks one chip, whether the mouse or the keyboard got there.</summary>
    private void Choose(int index)
    {
        if (index < 0 || index > Palette.Length)
        {
            return;
        }

        if (index < Palette.Length)
        {
            Colour = Palette[index];
            Invalidate();
            ColourPicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        var box = ChipAt(index);
        var popup = new ColourPickerPopup(Colour);
        popup.ColourChanged += (_, colour) =>
        {
            Colour = colour;
            Invalidate();
            ColourPicked?.Invoke(this, EventArgs.Empty);
        };
        popup.FormClosed += (_, _) => popup.Dispose();
        popup.Open(PointToScreen(new Point(box.X, box.Bottom + this.Scaled(6))), FindForm());
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

            bool keyboard = Focused && ShowFocusCues && i == _focusedChip;

            if (i == _hoveredChip || active || keyboard)
            {
                float width = this.ScaledF(keyboard && !active ? 1.6f : 2f);
                using var ring = new Pen(
                    active || keyboard ? Theme.Accent : Color.FromArgb(120, Theme.Accent), width);
                g.DrawEllipse(ring, Rectangle.Inflate(box, this.Scaled(3), this.Scaled(3)));
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
            double luminance = EffectPainter.Luminance(chip);
            using var outline = new Pen(Color.FromArgb(luminance > 0.75 ? 120 : 52, 0, 0, 0));
            g.DrawEllipse(outline, box);
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

    private static bool Same(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;
}

/// <summary>
/// A themed single line input: the rounded frame is painted here and a borderless
/// <see cref="TextBox"/> sits inside it, so a text field matches the drop downs instead of
/// showing the flat grey system border.
/// </summary>
internal sealed class TextField : FlatControl
{
    private const int SidePadding = 11;

    // Built in the field initialiser, not in the constructor body: setting Height below raises
    // OnSizeChanged, which lays the box out, and that ran before the assignment.
    private readonly TextBox _box = new()
    {
        BorderStyle = BorderStyle.None,
        ForeColor = Theme.Text,
        Font = Theme.Input,
    };

    public TextField()
    {
        Radius = 9;

        _box.GotFocus += (_, _) => Invalidate();
        _box.LostFocus += (_, _) =>
        {
            Invalidate();
            Committed?.Invoke(this, EventArgs.Empty);
        };

        // Typing changes the inner box, not this control, so without passing the event on a host
        // watching TextChanged never hears a keystroke - the preset editor's Create button would stay
        // greyed out however much was typed into it.
        _box.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
        _box.KeyDown += (_, e) => Accepted?.Invoke(this, e);
        Controls.Add(_box);

        DesignHeight = 34;
    }

    /// <summary>Key presses inside the field, so a host can act on Enter.</summary>
    public event KeyEventHandler? Accepted;

    /// <summary>
    /// The caret has left the field, for a host that applies what was typed on the way out rather
    /// than only on Enter.
    /// </summary>
    public event EventHandler? Committed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [AllowNull]
    public override string Text
    {
        get => _box.Text;
        set => _box.Text = value ?? "";
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string PlaceholderText
    {
        get => _box.PlaceholderText;
        set => _box.PlaceholderText = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MaxLength
    {
        get => _box.MaxLength;
        set => _box.MaxLength = value;
    }

    /// <summary>Puts the caret in the field; clicking the frame does the same.</summary>
    public void FocusInput()
    {
        _box.Focus();
        _box.SelectAll();
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        // The inner box has to match the frame it is painted into, in either theme.
        _box.BackColor = Theme.Surface;
        base.OnBackColorChanged(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _box.BackColor = Theme.Surface;
        LayOutBox();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        LayOutBox();
        base.OnSizeChanged(e);
    }

    private void LayOutBox()
    {
        // Both the inset and the vertical breathing room are 96 dpi numbers: the inner box
        // measures itself from the font, which grows, so leaving these flat clipped the text at
        // its own caret height on a scaled display.
        int inset = this.Scaled(SidePadding);
        int height = Math.Min(_box.PreferredHeight, Math.Max(1, Height - this.Scaled(6)));
        int width = Math.Max(1, Width - (inset * 2));
        bool wider = width > _box.Width;
        _box.SetBounds(inset, Math.Max(0, (Height - height) / 2), width, height);

        // A box that just got wider keeps the scroll offset it needed while it was narrow, which
        // hid the start of a name that now fits whole - the rename field selects all its text
        // before a popup opened on a scaled display has grown it to size.
        if (wider && _box.IsHandleCreated)
        {
            int start = _box.SelectionStart;
            int length = _box.SelectionLength;
            _box.Select(0, 0);
            _box.ScrollToCaret();
            _box.Select(start, length);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        FocusInput();
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        g.Clear(BackColor);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, Radius);

        using var fill = new SolidBrush(Theme.Surface);
        g.FillPath(fill, path);

        using var border = new Pen(_box.Focused ? Theme.Accent : Hovered ? Theme.TextMuted : Theme.Border);
        g.DrawPath(border, path);
    }
}

/// <summary>Small square button carrying the settings gear.</summary>
internal sealed class GlyphButton : FlatControl
{
    public GlyphButton()
    {
        Radius = 8;
        DesignSize = new Size(30, 30);
        AccessibleRole = AccessibleRole.PushButton;
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
        DesignSize = new Size(38, 22);
        AccessibleRole = AccessibleRole.CheckButton;
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

    /// <summary>Reports the on/off state, which a bare custom control does not expose.</summary>
    protected override AccessibleObject CreateAccessibilityInstance() => new SwitchAccessibleObject(this);

    private sealed class SwitchAccessibleObject : ControlAccessibleObject
    {
        public SwitchAccessibleObject(ToggleSwitch owner) : base(owner)
        {
        }

        public override AccessibleStates State =>
            base.State | (((ToggleSwitch)Owner!).Checked ? AccessibleStates.Checked : AccessibleStates.None);
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

        float inset = this.Scaled(3);
        float knob = Height - (inset * 2);
        float x = _checked ? Width - knob - inset : inset;
        using var knobBrush = new SolidBrush(Color.White);
        g.FillEllipse(knobBrush, x, inset, knob, knob);
    }
}

/// <summary>
/// A small, themed colour picker: a hue strip, a saturation/value square and a hex field.
/// Opens where <see cref="ColourStrip"/>'s custom chip is clicked.
/// </summary>
internal sealed class ColourPickerPopup : PopupForm
{
    private const int Pad = 14;
    private const int SvSize = 168;
    private const int HueHeight = 16;
    private const int Gap = 10;
    private const int SwatchSize = 30;

    // The hue strip is the same for every instance and every hue, so it is built once.
    private static readonly Bitmap HueStripBitmap = BuildHueStrip();

    private readonly TextField _hex;

    // Computed rather than stored: WinForms scales the window itself for the display it opens on,
    // and these are painted inside it - held as fixed 96 dpi rectangles they would leave the picker in
    // the top left corner of a window twice their size.
    private Rectangle SvRect => new(this.Scaled(Pad), this.Scaled(Pad), this.Scaled(SvSize), this.Scaled(SvSize));

    private Rectangle HueRect => new(
        this.Scaled(Pad), this.Scaled(Pad + SvSize + Gap), this.Scaled(SvSize), this.Scaled(HueHeight));

    private double _hue;
    private double _saturation;
    private double _value;

    /// <summary>Set once the popup is going away, so a last-moment Leave cannot apply anything.</summary>
    private bool _closing;
    private Bitmap? _svBitmap;
    private byte[]? _svBuffer;
    private bool _draggingSv;
    private bool _draggingHue;

    public ColourPickerPopup(Color initial)
    {
        (_hue, _saturation, _value) = Theme.ToHsv(initial);

        ForeColor = Theme.Text;
        Font = Theme.Ui;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        // The project's own field rather than a bare TextBox: the system border and the system
        // font were the one thing in this window that did not look like the rest of it, and in
        // dark mode that border reads as a light box around a dark field.
        _hex = new TextField
        {
            MaxLength = 7,
            Text = Hex(Current),
            AccessibleName = Strings.ColourHexAccessibleName,
        };
        _hex.Accepted += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyHex();
                e.SuppressKeyPress = true;
            }
        };
        _hex.Committed += (_, _) => ApplyHex();
        Controls.Add(_hex);

        Reflow();
        RebuildSvBitmap();
    }

    /// <summary>Sizes the window and places the hex field - both scale with the display, unlike
    /// <see cref="SvRect"/>/<see cref="HueRect"/>'s painted geometry only.</summary>
    private void Reflow()
    {
        ClientSize = new Size(
            this.Scaled(Pad + SvSize + Pad),
            this.Scaled(Pad + SvSize + Gap + HueHeight + Gap + SwatchSize + Pad));

        // Two pixels above the swatch beside it, so the taller field ends up centred on it.
        _hex.Location = new Point(
            this.Scaled(Pad + SwatchSize + 10), this.Scaled(Pad + SvSize + Gap + HueHeight + Gap - 2));
        _hex.Width = this.Scaled(SvSize - SwatchSize - 10);

        KeepOnScreen();
    }

    /// <summary>Dragged onto a display with a different scale: the size and the hex field's
    /// position were both computed for the one it came from.</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(() =>
        {
            if (!IsDisposed)
            {
                Reflow();
                Invalidate();
            }
        });
    }

    protected override void OnOpenedAtOtherScale()
    {
        Reflow();
        Invalidate();
    }

    public event EventHandler<Color>? ColourChanged;

    public Color Current => Theme.FromHsv(_hue, _saturation, _value);

    /// <summary>Opens at a screen position. Not modal - a click anywhere else closes it.</summary>
    public void Open(Point at, IWin32Window? owner)
    {
        Place(at, this.Scaled(6));

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

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The hex field applies its content when it loses focus, and closing does exactly that -
        // which would push the colour the user had just cancelled with Escape.
        _closing = true;
        base.OnFormClosing(e);
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
    /// Repainted whenever the hue changes, which is once per mouse-move for the whole length of a
    /// hue drag - so the bitmap and its row buffer are allocated once for the life of the popup
    /// and rewritten in place. Building a fresh 168x168 bitmap plus an 85 KB array per mouse-move
    /// is what made dragging the strip stutter. Raw pixel writes rather than SetPixel for the
    /// same reason.
    /// </summary>
    private void RebuildSvBitmap()
    {
        Bitmap bitmap = _svBitmap ??= new Bitmap(SvSize, SvSize, PixelFormat.Format24bppRgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, SvSize, SvSize), ImageLockMode.WriteOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            byte[] buffer = _svBuffer ??= new byte[data.Stride * SvSize];
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
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private void ApplyHex()
    {
        if (_closing)
        {
            return;
        }

        if (Theme.TryParseHex(_hex.Text, out Color colour))
        {
            // Leaving the field untouched, or typing the colour already shown, changes nothing:
            // raising it anyway would send the same colour to the controller again.
            Color shown = Current;
            if (colour.R == shown.R && colour.G == shown.G && colour.B == shown.B)
            {
                _hex.Text = Hex(shown);
                return;
            }

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
        if (SvRect.Contains(e.Location))
        {
            _draggingSv = true;
            UpdateFromSv(e.Location);
        }
        else if (HueRect.Contains(e.Location))
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
        _saturation = Math.Clamp((at.X - SvRect.X) / (double)(SvRect.Width - 1), 0, 1);
        _value = 1.0 - Math.Clamp((at.Y - SvRect.Y) / (double)(SvRect.Height - 1), 0, 1);
        _hex.Text = Hex(Current);
        Invalidate();
    }

    private void UpdateFromHue(Point at)
    {
        _hue = Math.Clamp((at.X - HueRect.X) / (double)(HueRect.Width - 1), 0, 1) * 360.0;
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
            g.DrawImage(_svBitmap, SvRect);
        }

        var svMarker = new Point(
            SvRect.X + (int)(_saturation * (SvRect.Width - 1)),
            SvRect.Y + (int)((1 - _value) * (SvRect.Height - 1)));
        PaintRing(g, svMarker, this.Scaled(6), Current, this);

        g.DrawImage(HueStripBitmap, HueRect);
        int hueX = HueRect.X + (int)(_hue / 360.0 * (HueRect.Width - 1));
        int hueOverhang = this.Scaled(2);
        int outlineOffset = this.Scaled(1);
        using (var huePen = new Pen(Color.White, this.ScaledF(2f)))
        {
            g.DrawLine(huePen, hueX, HueRect.Y - hueOverhang, hueX, HueRect.Bottom + hueOverhang);
        }
        using (var hueOutline = new Pen(Color.FromArgb(90, 0, 0, 0), this.ScaledF(1f)))
        {
            g.DrawLine(hueOutline, hueX - outlineOffset, HueRect.Y - hueOverhang, hueX - outlineOffset, HueRect.Bottom + hueOverhang);
            g.DrawLine(hueOutline, hueX + outlineOffset, HueRect.Y - hueOverhang, hueX + outlineOffset, HueRect.Bottom + hueOverhang);
        }

        var swatch = new Rectangle(this.Scaled(Pad), HueRect.Bottom + this.Scaled(Gap),
            this.Scaled(SwatchSize), this.Scaled(SwatchSize));
        using (GraphicsPath swatchPath = Theme.RoundedRectangle(swatch, this.Scaled(6)))
        using (var swatchBrush = new SolidBrush(Current))
        {
            g.FillPath(swatchBrush, swatchPath);
            using var outline = new Pen(Theme.Border);
            g.DrawPath(outline, swatchPath);
        }
    }

    private static void PaintRing(Graphics g, Point at, int radius, Color fill, Control control)
    {
        var box = new Rectangle(at.X - radius, at.Y - radius, radius * 2, radius * 2);
        using (var brush = new SolidBrush(fill))
        {
            g.FillEllipse(brush, box);
        }

        int outlineGrow = Math.Max(1, control.Scaled(1));
        using var white = new Pen(Color.White, control.ScaledF(2f));
        g.DrawEllipse(white, box);
        using var black = new Pen(Color.FromArgb(140, 0, 0, 0), control.ScaledF(1f));
        g.DrawEllipse(black, Rectangle.Inflate(box, outlineGrow, outlineGrow));
    }
}

/// <summary>A plain flat button with a text label, for actions that are not the primary switch.</summary>
internal sealed class PillButton : FlatControl
{
    public PillButton()
    {
        Radius = 9;
        ForeColor = Theme.Accent;
        AccessibleRole = AccessibleRole.PushButton;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; } = Theme.AccentSoft;

    /// <summary>Solid accent fill with a white label: the one action a panel leads with.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary
    {
        get => _primary;
        set
        {
            _primary = value;
            Fill = value ? Theme.Accent : Theme.AccentSoft;
            ForeColor = value ? Color.White : Theme.Accent;
            Invalidate();
        }
    }

    private bool _primary;

    /// <summary>
    /// The fill is derived rather than read on every repaint, so it is the one thing that has to
    /// be moved over to the new theme by hand. The label follows from the palette translation.
    /// </summary>
    public override void ApplyTheme()
    {
        Fill = Theme.Retint(Fill);
        base.ApplyTheme();
    }

    private int? _fitPadding;

    /// <summary>
    /// Wide enough for its own label plus padding, so a longer translation is never clipped -
    /// the button has no ellipsis and no auto-size of its own.
    /// </summary>
    public void FitToText(int padding = 22)
    {
        // Measured and assigned outright rather than only grown: a button whose label is later
        // swapped for a shorter one (the preset editor's Create/Replace/Save button as its name
        // field is typed into) has to be able to shrink back, not just stay at its widest ever.
        // The padding is scaled because the measured text already is.
        _fitPadding = padding;
        Width = Theme.MeasuredWidth(Text, Font) + (this.Scaled(padding) * 2);
    }

    /// <summary>
    /// A button sized to its label keeps that size until something asks again, so a move to a
    /// display at another scale would leave it as wide as the text was.
    /// </summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);

        if (_fitPadding is int padding)
        {
            FitToText(padding);
        }
    }

    /// <summary>The same for a window created straight onto a display at another scale, which
    /// sends no change notification at all.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_fitPadding is int padding)
        {
            FitToText(padding);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        g.Clear(BackColor);

        var bounds = new Rectangle(0, 0, Width, Height);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, Radius);

        Color fill = !Enabled ? Theme.NeutralSoft : Hovered ? Theme.Blend(Fill, Color.White, 0.12) : Fill;
        using (var brush = new SolidBrush(Pressed ? Theme.Scale(fill, 0.92) : fill))
        {
            g.FillPath(brush, path);
        }

        DrawFocusRing(g, path);

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>
/// Arms a <see cref="PillButton"/> on the first click, swapping it to a confirmation label for a
/// few seconds before a second click fires the real action - no separate confirmation dialog.
/// Settings' Reset and the preset editor's Delete share this one copy of the timer and text swap.
/// </summary>
internal sealed class ArmedButton : IDisposable
{
    private readonly PillButton _button;
    private string _idleText;
    private string _armedText;
    private readonly int? _fitPadding;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    private bool _armed;

    /// <param name="button">
    /// Already carrying its idle label and its own styling (fill, foreground, size) - this only
    /// adds the arm/confirm behaviour on top.
    /// </param>
    /// <param name="fitPadding">
    /// Re-measures the button to whichever label is now showing, for a button sized to its own
    /// text rather than docked to fill a row; omit it for a docked button, which needs no refit.
    /// </param>
    public ArmedButton(PillButton button, string idleText, string armedText, int? fitPadding = null)
    {
        _button = button;
        _idleText = idleText;
        _armedText = armedText;
        _fitPadding = fitPadding;

        // Matches the idle-label width a caller that passes fitPadding would otherwise have to
        // fit itself right after construction.
        Refit();

        _button.Click += (_, _) => OnClick();
        _timer.Tick += (_, _) => Disarm();
    }

    /// <summary>Fired on the second click while still armed - the confirmed action itself.</summary>
    public event EventHandler? Confirmed;

    private void OnClick()
    {
        if (!_armed)
        {
            _armed = true;
            _button.Text = _armedText;
            Refit();
            _timer.Start();
            return;
        }

        Disarm();
        Confirmed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to the idle label - the timer expiring, a confirmed click, or a caller
    /// closing out from under it (a language switch, the popup itself closing) all take this
    /// path, so the button is never found already armed the next time it is seen.</summary>
    public void Disarm()
    {
        _armed = false;
        _button.Text = _idleText;
        Refit();
        _timer.Stop();
    }

    /// <summary>
    /// A language switch changes both labels out from under an already-constructed button - the
    /// idle and armed text passed to the constructor stop applying, so a caller that needs new
    /// wording after a switch has to give it here rather than only calling <see cref="Disarm"/>,
    /// which would just repaint the stale text it already holds.
    /// </summary>
    public void Relabel(string idleText, string armedText)
    {
        _idleText = idleText;
        _armedText = armedText;
        Disarm();
    }

    private void Refit()
    {
        if (_fitPadding is int padding)
        {
            _button.FitToText(padding);
        }
    }

    public void Dispose() => _timer.Dispose();
}

/// <summary>
/// A slim slider. Used for the brightness the effect colour is scaled to before it is sent -
/// the controller has no brightness of its own.
/// </summary>
internal sealed class Slider : FlatControl
{
    private const int TrackHeightAt96 = 6;
    private const int KnobAt96 = 16;

    // The control's own height grows with the display; what is painted inside it does not, unless
    // it is taken from the current dpi like this.
    private int TrackHeight => this.Scaled(TrackHeightAt96);

    private int Knob => this.Scaled(KnobAt96);

    private readonly EffectSurface _surface = new();
    private readonly System.Windows.Forms.Timer _commit = new() { Interval = 250 };

    private int _value = 100;
    private bool _dragging;

    /// <summary>The value the drag started from, so a click that lands back on it commits nothing.</summary>
    private int _pressedAt;

    public Slider()
    {
        DesignHeight = 24;
        AccessibleRole = AccessibleRole.Slider;

        // Arrow keys would otherwise fire one switch at the controller per keypress, and
        // ToggleForm drops every request that arrives while the previous one is still running -
        // so the value the user stopped on could be the one that never got sent.
        _commit.Tick += (_, _) =>
        {
            _commit.Stop();
            ValueCommitted?.Invoke(this, EventArgs.Empty);
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _commit.Dispose();
            _surface.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Raised while the knob moves, for a live read-out.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>
    /// Raised once the knob is let go. Applying only here keeps a drag from firing a switch
    /// at the controller for every mouse move.
    /// </summary>
    public event EventHandler? ValueCommitted;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum { get; set; } = 100;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, Minimum, Maximum);
            if (clamped == _value)
            {
                return;
            }

            _value = clamped;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A bare custom control has no value to read out; this one is a percentage.</summary>
    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessibleObject(this);

    private sealed class SliderAccessibleObject : ControlAccessibleObject
    {
        public SliderAccessibleObject(Slider owner) : base(owner)
        {
        }

        public override string? Value
        {
            get => string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.BrightnessValue, ((Slider)Owner!).Value);
            set { }
        }
    }

    private Rectangle TrackRect => new(
        Knob / 2, (Height - TrackHeight) / 2, Math.Max(1, Width - Knob), TrackHeight);

    private float KnobCentre
    {
        get
        {
            Rectangle track = TrackRect;
            double span = Math.Max(1, Maximum - Minimum);
            return track.X + (float)((_value - Minimum) / span * track.Width);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _pressedAt = _value;
            SetFromPoint(e.X);
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            SetFromPoint(e.X);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;

            // A drag that ends where it started - or a plain click on the knob - has nothing to
            // send, and every commit costs a full pass over the controller.
            if (_value != _pressedAt)
            {
                ValueCommitted?.Invoke(this, EventArgs.Empty);
            }
        }

        base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End
            or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);

    /// <summary>Wheel movement not yet worth a whole notch - precision touchpads report small deltas.</summary>
    private int _wheelRemainder;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int before = _value;
        _wheelRemainder += e.Delta;
        int notches = _wheelRemainder / 120;
        _wheelRemainder -= notches * 120;
        Value = _value + (notches * 5);

        if (_value != before)
        {
            Debounce();
        }

        base.OnMouseWheel(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int before = _value;
        Value = e.KeyCode switch
        {
            Keys.Left or Keys.Down => _value - 5,
            Keys.Right or Keys.Up => _value + 5,
            Keys.PageDown => _value - 20,
            Keys.PageUp => _value + 20,
            Keys.Home => Minimum,
            Keys.End => Maximum,
            _ => _value,
        };

        if (_value != before)
        {
            Debounce();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Waits for the keys or the wheel to settle before touching the hardware.</summary>
    private void Debounce()
    {
        _commit.Stop();
        _commit.Start();
    }

    private void SetFromPoint(int x)
    {
        Rectangle track = TrackRect;
        double fraction = Math.Clamp((x - track.X) / (double)track.Width, 0, 1);
        Value = Minimum + (int)Math.Round(fraction * (Maximum - Minimum));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        g.Clear(BackColor);

        Rectangle track = TrackRect;
        float centre = KnobCentre;

        // Track and filled part go into one buffer and are masked through the track shape once.
        // Filling the progress path straight over the track blended into the same rounded left
        // cap a second time and left it smudged.
        _surface.Paint(g, track, TrackHeight / 2f, buffer =>
        {
            using (var rest = new SolidBrush(Theme.NeutralSoft))
            {
                buffer.FillRectangle(rest, 0, 0, track.Width, track.Height);
            }

            using var done = new SolidBrush(Enabled ? Theme.Accent : Theme.Neutral);
            buffer.FillRectangle(done, 0, 0, Math.Max(1, centre - track.X), track.Height);
        });

        var knob = new RectangleF(centre - (Knob / 2f), (Height - Knob) / 2f, Knob, Knob);
        using (var brush = new SolidBrush(Color.White))
        {
            g.FillEllipse(brush, knob);
        }

        using (var outline = new Pen(Hovered || _dragging ? Theme.Accent : Theme.Border, this.ScaledF(1.4f)))
        {
            g.DrawEllipse(outline, knob);
        }

        if (Focused && ShowFocusCues)
        {
            float ringGrow = this.ScaledF(2f);
            using var ring = new Pen(Color.FromArgb(130, Theme.Accent), ringGrow);
            g.DrawEllipse(ring, RectangleF.Inflate(knob, ringGrow, ringGrow));
        }
    }
}

/// <summary>Small round × button, used as a panel's discard/close control.</summary>
internal sealed class DeleteButton : FlatControl
{
    public DeleteButton()
    {
        Radius = 6;
        DesignSize = new Size(22, 22);
        AccessibleRole = AccessibleRole.PushButton;
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

        using var pen = new Pen(Hovered ? Theme.Text : Theme.TextMuted, this.ScaledF(1.5f))
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float m = Width * 0.28f;
        g.DrawLine(pen, m, m, Width - m, Height - m);
        g.DrawLine(pen, Width - m, m, m, Height - m);
    }
}

/// <summary>
/// Icon button for exporting or importing a custom preset as a <c>.json</c> file - a labelled
/// tray arrow rather than a text button, so the pair fits next to the name field instead of a
/// row of its own. The direction alone tells them apart, same as every other icon-only control
/// here, so callers are expected to give each one a tooltip and an <see cref="Control.AccessibleName"/>.
/// </summary>
internal sealed class TransferButton : FlatControl
{
    public TransferButton()
    {
        Radius = 6;
        DesignSize = new Size(26, 26);
        AccessibleRole = AccessibleRole.PushButton;
    }

    /// <summary>True for import: an arrow pointing up, out of the tray - a file coming into the
    /// app. False for export: an arrow pointing down, into the tray - the preset going out.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Import { get; set; }

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

        using var pen = new Pen(Hovered ? Theme.Text : Theme.TextMuted, this.ScaledF(1.5f))
            { StartCap = LineCap.Round, EndCap = LineCap.Round };

        float cx = Width / 2f;
        float shaftTop = Height * 0.22f;
        float shaftBottom = Height * 0.58f;
        float head = Width * 0.15f;
        float trayY = Height * 0.74f;
        float trayHalf = Width * 0.24f;

        (float from, float to) = Import ? (shaftBottom, shaftTop) : (shaftTop, shaftBottom);
        g.DrawLine(pen, cx, from, cx, to);
        g.DrawLine(pen, cx - head, to + (Import ? head : -head), cx, to);
        g.DrawLine(pen, cx + head, to + (Import ? head : -head), cx, to);

        g.DrawLine(pen, cx - trayHalf, trayY, cx + trayHalf, trayY);
    }
}

/// <summary>
/// Small popup to give one channel a name of its own. Not modal - a click anywhere else
/// dismisses it without saving, same as every popup here except the preset editor.
/// </summary>
internal sealed class RenamePopup : PopupForm
{
    private const int Pad = 14;
    private const int FieldWidth = 200;

    private readonly TextField _name = new();
    private readonly PillButton _save = new();
    private readonly PillButton _reset = new();
    private readonly ArmedButton _resetArm;

    public RenamePopup(string currentName)
    {
        ForeColor = Theme.Text;
        Font = Theme.Ui;

        // The popup has no title bar and the field no label beside it, so without this a screen
        // reader announces an unnamed edit box - every other surface here names its controls.
        AccessibleName = Strings.ChannelRenameAccessibleName;
        _name.AccessibleName = Strings.ChannelRenameAccessibleName;
        _name.Text = currentName;
        _name.MaxLength = AuraFiles.MaxChannelName;
        _name.Accepted += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                Commit();
                e.SuppressKeyPress = true;
            }
        };
        Controls.Add(_name);

        _save.Text = Strings.ChannelRenameSave;
        _save.Primary = true;
        _save.DesignHeight = 30;
        _save.Click += (_, _) => Commit();
        Controls.Add(_save);

        _reset.Text = Strings.ChannelRenameReset;
        _reset.DesignHeight = 30;
        _reset.Fill = Theme.NeutralSoft;
        _reset.ForeColor = Theme.Danger;
        // Throws the channel's own name away - arms on the first click like every other
        // destructive button here, instead of acting on a single one.
        _resetArm = new ArmedButton(_reset, Strings.ChannelRenameReset, Strings.ChannelRenameResetConfirm, Pad);
        _resetArm.Confirmed += (_, _) =>
        {
            Renamed?.Invoke(this, "");
            Close();
        };
        // The armed label is wider than the idle one - re-run layout whenever ArmedButton
        // changes it, so the popup keeps fitting instead of clipping or overlapping Save.
        _reset.TextChanged += (_, _) => Reflow();
        Controls.Add(_reset);

        Reflow();
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>Sizes and places every control from the 96 dpi constants - no hand-painted
    /// geometry of its own like <see cref="ColourPickerPopup"/>, just controls to lay out.</summary>
    private void Reflow()
    {
        int pad = this.Scaled(Pad);
        int gap = this.Scaled(10);

        _name.Location = new Point(pad, pad);
        _name.Width = this.Scaled(FieldWidth);

        _save.Location = new Point(pad, _name.Bottom + gap);
        _save.FitToText(16);

        _reset.FitToText(14);

        // Placed and sized after both buttons know their own width, so "Zurücksetzen" is neither
        // clipped nor overlapping Save.
        _reset.Location = new Point(_save.Right + this.Scaled(8), _name.Bottom + gap);
        ClientSize = new Size(
            Math.Max(pad + this.Scaled(FieldWidth) + pad, _reset.Right + pad),
            _reset.Bottom + pad);
        _name.Width = ClientSize.Width - (pad * 2);

        KeepOnScreen();
    }

    /// <summary>Dragged onto a display with a different scale: every position and size above was
    /// computed for the one it came from.</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(() =>
        {
            if (!IsDisposed)
            {
                Reflow();
                Invalidate();
            }
        });
    }

    protected override void OnOpenedAtOtherScale()
    {
        Reflow();
        Invalidate();
    }

    /// <summary>Raised with the new name, or an empty one when Reset was chosen.</summary>
    public event EventHandler<string>? Renamed;

    private void Commit()
    {
        Renamed?.Invoke(this, _name.Text.Trim());
        Close();
    }

    public void Open(Point at, IWin32Window? owner)
    {
        Place(at, this.Scaled(6));

        if (owner == null)
        {
            Show();
        }
        else
        {
            Show(owner);
        }

        Activate();
        _name.FocusInput();
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

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Prepare(e.Graphics);
        e.Graphics.Clear(Theme.Surface);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _resetArm.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// The dark tooltip. The system one shows its window before its owner draw runs, so every dark
/// tooltip opened with a short white flash; this one is painted while the compositor still keeps
/// it hidden and only then uncovered. <see cref="SetToolTip"/> gives a control a hover tip,
/// <see cref="Show"/> and <see cref="Hide"/> place one by hand.
/// </summary>
internal sealed class ThemedTip : IDisposable
{
    private readonly Dictionary<Control, string> _texts = new();
    private readonly System.Windows.Forms.Timer _delay = new() { Interval = SystemInformation.MouseHoverTime };
    private readonly System.Windows.Forms.Timer _expire = new();
    private TipWindow? _window;
    private Control? _hovered;
    private bool _disposed;

    public ThemedTip()
    {
        _delay.Tick += (_, _) =>
        {
            _delay.Stop();
            if (_hovered is { IsDisposed: false, Visible: true } control && _texts.TryGetValue(control, out string? text))
            {
                Point at = control.PointToClient(Cursor.Position);
                Show(text, control, at.X + control.Scaled(12), at.Y + control.Scaled(20), 5000);
            }
        };
        _expire.Tick += (_, _) =>
        {
            _expire.Stop();
            if (_window is { IsDisposed: false })
            {
                _window.HideTip();
            }
        };
    }

    public void SetToolTip(Control control, string text)
    {
        if (!_texts.ContainsKey(control))
        {
            control.MouseEnter += OnEnter;
            control.MouseLeave += OnLeave;
            control.MouseDown += OnLeave;
        }

        _texts[control] = text;
    }

    public void Show(string text, Control owner, int x, int y, int durationMs)
    {
        if (_disposed || owner.IsDisposed)
        {
            return;
        }

        // A list that closes on a click disposes its tips, yet a mouse move already queued for it
        // still arrives; that one is dropped above. A tip window closed any other way is disposed
        // with it, so a fresh one replaces it rather than taking the app down.
        if (_window is null || _window.IsDisposed)
        {
            _window = new TipWindow();
        }

        _window.ShowAt(text, owner, owner.PointToScreen(new Point(x, y)));
        _expire.Stop();
        _expire.Interval = durationMs;
        _expire.Start();
    }

    public void Hide(Control owner)
    {
        _expire.Stop();
        if (_window is { IsDisposed: false })
        {
            _window.HideTip();
        }
    }

    private void OnEnter(object? sender, EventArgs e)
    {
        _hovered = sender as Control;
        _delay.Stop();
        _delay.Start();
    }

    private void OnLeave(object? sender, EventArgs e)
    {
        _delay.Stop();
        _hovered = null;
        Hide((Control)sender!);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Control control in _texts.Keys)
        {
            control.MouseEnter -= OnEnter;
            control.MouseLeave -= OnLeave;
            control.MouseDown -= OnLeave;
        }

        _delay.Dispose();
        _expire.Dispose();
        _window?.Dispose();
    }

    private sealed class TipWindow : Form
    {
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int CsDropShadow = 0x00020000;
        private const int WmNcHitTest = 0x0084;
        private const int HtTransparent = -1;
        private static readonly IntPtr TopMostZ = new(-1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private const uint SwpHideWindow = 0x0080;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
            int width, int height, uint flags);

        private string _text = "";
        private Control? _owner;
        private bool _shown;

        public TipWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
                parameters.ClassStyle |= CsDropShadow;
                return parameters;
            }
        }

        public void ShowAt(string text, Control owner, Point screen)
        {
            _text = text;
            _owner = owner;
            Font = owner.Font;
            BackColor = Theme.Surface;

            Size measured = TextRenderer.MeasureText(text, Font);
            var size = new Size(measured.Width + owner.Scaled(12), measured.Height + owner.Scaled(8));
            Rectangle area = Screen.FromPoint(screen).WorkingArea;
            var bounds = new Rectangle(
                Math.Max(area.Left, Math.Min(screen.X, area.Right - size.Width)),
                Math.Max(area.Top, Math.Min(screen.Y, area.Bottom - size.Height)),
                size.Width, size.Height);

            IntPtr handle = Handle;
            // Cloaked only on the way in: one already showing is dark already, and cloaking it again
            // for every row the pointer crosses made it drop out for a frame each time.
            bool appearing = !_shown;
            if (appearing)
            {
                Theme.Cloak(handle, true);
            }

            // Shown, placed and raised natively, never through Show or TopMost: WinForms raises a
            // topmost form without SWP_NOACTIVATE, the tip took the activation, the list behind
            // it closed on losing it and disposed this window halfway through showing it.
            SetWindowPos(handle, TopMostZ, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                SwpNoActivate | SwpShowWindow);
            _shown = true;
            if (IsDisposed)
            {
                return;
            }

            // Moving onto a display at another scale resizes the window to its own guess.
            if (Bounds != bounds)
            {
                Bounds = bounds;
            }

            Invalidate();
            Update();
            if (appearing)
            {
                Theme.Cloak(handle, false);
            }
        }

        public void HideTip()
        {
            _shown = false;
            if (IsHandleCreated)
            {
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpHideWindow);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var fill = new SolidBrush(Theme.Surface);
            e.Graphics.FillRectangle(fill, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.PaintToolTip(new DrawToolTipEventArgs(e.Graphics, this, _owner ?? this, ClientRectangle,
                _text, Theme.Surface, Theme.Text, Font));
        }

        protected override void WndProc(ref Message m)
        {
            // The pointer passes through to the list underneath, as with the system tooltip, so
            // the tip never takes the hover away from the row it describes.
            if (m.Msg == WmNcHitTest)
            {
                m.Result = HtTransparent;
                return;
            }

            base.WndProc(ref m);
        }
    }
}
