using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// One entry of a <see cref="Select"/>. A mode of null draws no effect icon, unless
/// <paramref name="CustomColours"/> is given - used for custom presets, which mix effects
/// and so have no single mode of their own.
/// </summary>
/// <param name="Editable">
/// Gives the row an edit and a delete button, for the entries the user made themselves.
/// </param>
/// <param name="IsAction">
/// Marks the row as a command rather than a choice: it sits at the bottom, behind a separator,
/// and picking it never changes the selection.
/// </param>
/// <param name="Renamable">
/// Gives the row a single pencil button that raises the same edit event as
/// <paramref name="Editable"/>, without the delete button - for entries that always exist and
/// can only be given a different name, such as a channel.
/// </param>
/// <param name="IsHint">
/// A line of explanation rather than a choice: muted, not selectable, skipped by the keyboard.
/// Used to say why some effects are missing from the list.
/// </param>
internal sealed record SelectItem(string Key, string Text, byte? Mode, Color[]? CustomColours = null,
    bool Editable = false, bool IsAction = false, bool Renamable = false, bool IsHint = false,
    string? Hint = null);

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
        AccessibleRole = AccessibleRole.PushButton;
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
    /// the same three characters on every frame, and each one used to cost a
    /// <see cref="TextRenderer.MeasureText"/> - a GDI call - plus a string per character per pass,
    /// five passes deep for the shadow. Measured on this machine: 61 us and 275 bytes per frame
    /// that this cache turns into nothing.
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

/// <summary>A rounded drop down that opens a themed popup instead of the system list.</summary>
internal sealed class Select : FlatControl
{
    /// <summary>The closed control's own metrics, at 96 dpi - see <see cref="OnPaint"/>.</summary>
    private const int TextInset = 11;
    private const int IconWidth = 22;
    private const int IconHeight = 14;
    private const int IconGap = 10;
    private const int ChevronRoom = 26;

    private readonly List<SelectItem> _items = new();

    /// <summary>Shared across repaints instead of one throwaway buffer per paint (hover, focus,
    /// theme change all repaint the closed control, same as <see cref="SelectPopup"/>'s own).</summary>
    private readonly EffectSurface _icons = new();

    public Select()
    {
        Radius = 9;
        DesignHeight = 34;
        AccessibleRole = AccessibleRole.ComboBox;
    }

    public event EventHandler? SelectionChanged;

    /// <summary>Raised around the drop down, so a hosting popup can stay open meanwhile.</summary>
    public event EventHandler? PopupOpening;

    public event EventHandler? PopupClosed;

    /// <summary>An entry marked as a command was picked; the selection is left alone.</summary>
    public event EventHandler<SelectItem>? ActionPicked;

    /// <summary>The edit button of an editable entry was pressed.</summary>
    public event EventHandler<SelectItem>? EditRequested;

    /// <summary>The delete button of an editable entry was pressed and then confirmed.</summary>
    public event EventHandler<SelectItem>? DeleteRequested;

    /// <summary>The duplicate button of an editable entry was pressed.</summary>
    public event EventHandler<SelectItem>? DuplicateRequested;

    /// <summary>
    /// Least width the drop down opens at. The control itself can be narrow, while its list
    /// still has room for a name plus the edit and delete buttons.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int PopupWidth { get; set; }

    /// <summary>
    /// True for a list that takes whatever its row has left rather than claiming a width of its
    /// own. It then reports no preferred width at all, so a layout cannot treat the width it
    /// happened to have on the display it came from as a floor the row must not shrink below -
    /// which is what pushed the gear off the right-hand edge after a display-scale change. A list
    /// this narrow shortens its own text, which is the intended result; a list that claims a width
    /// (the channel selector, in a column sized to fit) leaves this off.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool TakesWhatIsLeft { get; set; }

    public override Size GetPreferredSize(Size proposedSize) =>
        TakesWhatIsLeft ? new Size(0, Height) : base.GetPreferredSize(proposedSize);

    /// <summary>
    /// The width this control needs for its longest entry, icon, chevron and padding included.
    /// Measured rather than assumed: the entries are translated, and a channel can be renamed to
    /// anything, so any fixed number ends up clipping somebody's text.
    /// </summary>
    /// <param name="withIcon">
    /// False for a list whose entries carry no icon - the channel selector - so it is not padded
    /// out by room nothing draws in. Every pixel it keeps is one the effect list loses.
    /// </param>
    /// <param name="includeHints">
    /// False for sizing the closed control itself: a hint row can never be picked (see
    /// <see cref="SelectItem.IsHint"/>), so the closed control never has to show its text and
    /// measuring it there only made the control - and a window sized from it - grow and shrink
    /// as the hint row came and went. The popup this opens still needs the true width, since the
    /// hint really is drawn as a row there; that call keeps the default.
    /// </param>
    public int PreferredWidthForItems(bool withIcon = true, bool includeHints = true)
    {
        var widest = 0;
        foreach (SelectItem item in _items)
        {
            if (!includeHints && item.IsHint)
            {
                continue;
            }

            widest = Math.Max(widest, this.MeasuredWidth(item.Text, Font));
        }

        // The measured text already grew with the display, so the fixed parts around it have to as
        // well or the control comes out too narrow for its own longest entry.
        return widest + this.Scaled(TextInset + ChevronRoom +
            (withIcon ? IconWidth + IconGap : 0));
    }

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

        // A list open right now follows along instead of showing what was there a moment ago -
        // deleting a preset from one of its rows is exactly that case.
        _popup?.Resync(_items);
        Invalidate();
    }

    /// <summary>Shows an entry without raising <see cref="SelectionChanged"/>.</summary>
    public void ShowSelection(string key)
    {
        Selected = _items.FirstOrDefault(item => item.Key == key) ?? _items.FirstOrDefault();
        Invalidate();
    }

    /// <summary>
    /// Shared by every <see cref="Select"/> in the process. A sibling drop down still open when
    /// this one is clicked used to take an extra click or two to get out of the way: the click
    /// that landed on this control first only deactivated the other popup, and that popup's own
    /// close raced this one's open. Closing it here, synchronously, before this one is even
    /// created removes the race - by the time the new popup opens, the old one is fully gone.
    /// </summary>
    private static SelectPopup? _openPopup;

    /// <summary>This control's own open list, for pushing item changes into while it is up.</summary>
    private SelectPopup? _popup;

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);

        if (_items.Count == 0 || !Enabled)
        {
            return;
        }

        _openPopup?.Close();

        var popup = new SelectPopup(_items, Selected, Colour, Math.Max(Width, PopupWidth), Font, DeviceDpi);
        _openPopup = popup;
        _popup = popup;

        popup.Picked += (_, item) =>
        {
            if (item.IsAction)
            {
                ActionPicked?.Invoke(this, item);
                return;
            }

            if (item == Selected)
            {
                return;
            }

            Selected = item;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };

        popup.EditRequested += (_, item) => EditRequested?.Invoke(this, item);
        popup.DeleteRequested += (_, item) => DeleteRequested?.Invoke(this, item);
        popup.DuplicateRequested += (_, item) => DuplicateRequested?.Invoke(this, item);

        popup.FormClosed += (_, _) =>
        {
            if (_openPopup == popup)
            {
                _openPopup = null;
            }

            if (_popup == popup)
            {
                _popup = null;
            }

            PopupClosed?.Invoke(this, EventArgs.Empty);
            popup.Dispose();
        };

        PopupOpening?.Invoke(this, EventArgs.Empty);

        // Height + 4 below this control, and the same distance above it when there is no room
        // below - measured from this control rather than assumed, since the drop downs inside the
        // popups are shorter than the one in the window.
        popup.Open(PointToScreen(new Point(0, Height + this.Scaled(4))), FindForm(), Height + this.Scaled(8));
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

        // Everything here is custom painted, so nothing grows with the display on its own - the
        // icon stayed 22x14 next to text that had doubled.
        int left = this.Scaled(TextInset);
        var icon = new Rectangle(left, (Height - this.Scaled(IconHeight)) / 2,
            this.Scaled(IconWidth), this.Scaled(IconHeight));

        if (Selected?.Mode is byte mode)
        {
            EffectPainter.PaintIcon(g, icon, mode, Colour, _icons);
            left = icon.Right + this.Scaled(IconGap);
        }
        else if (Selected?.CustomColours is { Length: > 0 } colours)
        {
            EffectPainter.PaintUserIcon(g, icon, colours, _icons);
            left = icon.Right + this.Scaled(IconGap);
        }

        var text = new Rectangle(left, 0, Math.Max(0, Width - left - this.Scaled(ChevronRoom)), Height);
        TextRenderer.DrawText(g, Selected?.Text ?? "", Font, text, Enabled ? Theme.Text : Theme.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        PaintChevron(g, new Point(Width - this.Scaled(16), Height / 2), Theme.TextMuted);
    }

    private void PaintChevron(Graphics g, Point centre, Color colour)
    {
        int arm = this.Scaled(4);
        int rise = this.Scaled(2);

        using var pen = new Pen(colour, this.ScaledF(1.6f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        g.DrawLines(pen, new[]
        {
            new Point(centre.X - arm, centre.Y - rise),
            new Point(centre.X, centre.Y + rise),
            new Point(centre.X + arm, centre.Y - rise),
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icons.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>The themed list that <see cref="Select"/> drops down.</summary>
internal sealed class SelectPopup : PopupForm
{
    /// <summary>
    /// The list's own metrics, at 96 dpi. Everything drawn here is custom painted, so nothing
    /// scales on its own: the numbers are taken through <see cref="Scale"/> against the dpi of the
    /// control that opened the list, which is by definition the screen it appears on.
    /// </summary>
    private const int RowHeightAt96 = 30;
    private const int InsetAt96 = 6;
    private const int ButtonAt96 = 22;
    private const int ButtonGapAt96 = 2;

    /// <summary>Separator plus breathing space above the first command row.</summary>
    private const int ActionGapAt96 = 11;

    private readonly List<SelectItem> _items;
    private readonly Color _colour;

    // Not readonly: refreshed by OnDpiChanged when this is dragged to a display at another
    // scale, which is the only time any of these need to change after construction.
    private int _dpi;
    private int _rowHeight;
    private int _inset;
    private int _button;
    private int _buttonGap;
    private int _actionGap;
    private int _firstAction;

    /// <summary>Shared by every icon in the list, instead of one buffer per icon per paint.</summary>
    private readonly EffectSurface _icons = new();

    private int _highlighted;
    private int _hovered = -1;

    /// <summary>
    /// Set once an arrow key has moved the selection, cleared by the next mouse move. The list
    /// opens with nothing marked at all: marking the current entry made the highlight jump back to
    /// it whenever the pointer left the rows, which reads as flicker rather than as an answer to
    /// "which one is running" - the closed control says that already.
    /// </summary>
    private bool _keyboard;

    /// <summary>How far the list is scrolled, in pixels, when it does not all fit on screen.</summary>
    private int _scroll;

    /// <summary>The full, unscrolled height of every row, recomputed whenever the item list
    /// changes - unlike <see cref="_maxScroll"/>, which also depends on the screen.</summary>
    private int _content;

    private int _maxScroll;

    /// <summary>The row whose delete button is waiting to be confirmed, or -1.</summary>
    private int _confirming = -1;

    /// <summary>F2/Delete work on a custom preset row without any control of its own to hang a
    /// tooltip off - shown and hidden by hand as the hovered row changes instead.</summary>
    private readonly ToolTip _shortcutTip = new();
    private int _tipRow = -1;

    /// <param name="dpi">
    /// The opening control's <see cref="Control.DeviceDpi"/>. Taken from there rather than read
    /// here: this window has no handle yet, so it has no dpi of its own to ask.
    /// </param>
    public SelectPopup(List<SelectItem> items, SelectItem? selected, Color colour, int width, Font font, int dpi)
    {
        // Copied rather than aliased: the caller's own list (Select._items) is cleared and
        // refilled in place by SetItems, which would otherwise mutate this popup's list out from
        // under its cached _highlighted/_firstAction indices while it is still open.
        _items = new List<SelectItem>(items);
        _colour = colour;
        _highlighted = selected == null ? 0 : Math.Max(0, _items.IndexOf(selected));

        _dpi = dpi;
        ComputeMetrics();

        Font = font;
        Measure();

        // Left at its default, the tooltip is the system's own plain white box - jarring next to
        // this list's own dark, rounded surface.
        Theme.StyleToolTip(_shortcutTip);

        // A placeholder height, good enough until Open() knows which screen this actually opens
        // on and sizes it for real - the constructor cannot know that yet, and guessing the
        // primary screen was wrong on a second monitor with a shorter work area.
        ClientSize = new Size(width, Math.Min(_content, _rowHeight * 3));
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    private int Scale(int length) => length * _dpi / 96;

    private void ComputeMetrics()
    {
        _rowHeight = Scale(RowHeightAt96);
        _inset = Scale(InsetAt96);
        _button = Scale(ButtonAt96);
        _buttonGap = Scale(ButtonGapAt96);
        _actionGap = Scale(ActionGapAt96);
    }

    /// <summary>
    /// Dragged onto a display with a different scale: the row metrics were scaled for the dpi
    /// passed in when this opened, and the list's own height was measured against it.
    /// </summary>
    /// <remarks>
    /// <see cref="_dpi"/> is refreshed here rather than read live from <see cref="Control.DeviceDpi"/>
    /// in <see cref="Scale"/>: the constructor runs before this window has a handle, and
    /// <see cref="Control.DeviceDpi"/> is not the screen it is about to open on until then - see
    /// the constructor's own <c>dpi</c> parameter. This is the one place after construction where
    /// the handle already exists and the value is trustworthy.
    /// </remarks>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(() =>
        {
            if (!IsDisposed)
            {
                _dpi = DeviceDpi;
                ComputeMetrics();
                Measure();
                Fit(Screen.FromControl(this).WorkingArea);
                _scroll = Math.Clamp(_scroll, 0, _maxScroll);
                Invalidate();
            }
        });
    }

    /// <summary>Where the command rows start and how tall the whole list wants to be.</summary>
    private void Measure()
    {
        _firstAction = _items.FindIndex(item => item.IsAction);
        _content = (_items.Count * _rowHeight) + (_inset * 2) + (_firstAction >= 0 ? _actionGap : 0);
    }

    /// <summary>
    /// Takes the item list again while the window stays open. Deleting a preset from a row used to
    /// close the whole list, so removing three of them meant opening it three times - the list is
    /// where presets are managed, so it has to survive managing them.
    /// </summary>
    public void Resync(IEnumerable<SelectItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        Measure();

        _confirming = -1;
        _hovered = -1;
        _highlighted = Math.Clamp(_highlighted, 0, Math.Max(0, _items.Count - 1));

        Fit(Screen.FromControl(this).WorkingArea);
        _scroll = Math.Clamp(_scroll, 0, _maxScroll);
        Invalidate();
    }

    /// <summary>
    /// Height for the screen this is on, and how far it can scroll there. The constructor cannot
    /// know either - a work area shorter than the primary monitor's otherwise let the list claim
    /// rows past the bottom of the screen instead of scrolling to reach them.
    /// </summary>
    private void Fit(Rectangle screen)
    {
        int height = Math.Min(_content, Math.Max(_rowHeight * 3, screen.Height - Scale(80)));
        _maxScroll = Math.Max(0, _content - height);

        if (height != ClientSize.Height)
        {
            ClientSize = new Size(ClientSize.Width, height);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icons.Dispose();
            _shortcutTip.Dispose();
        }

        base.Dispose(disposing);
    }

    public event EventHandler<SelectItem>? Picked;

    /// <summary>The edit button of an editable row was pressed.</summary>
    public event EventHandler<SelectItem>? EditRequested;

    /// <summary>A delete was asked for and then confirmed on the row itself.</summary>
    public event EventHandler<SelectItem>? DeleteRequested;

    /// <summary>The duplicate button of an editable row was pressed.</summary>
    public event EventHandler<SelectItem>? DuplicateRequested;

    private Rectangle RowRect(int index) => new(
        _inset,
        _inset + (index * _rowHeight) + (_firstAction >= 0 && index >= _firstAction ? _actionGap : 0) - _scroll,
        Width - (_inset * 2),
        _rowHeight);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_maxScroll > 0)
        {
            _scroll = Math.Clamp(_scroll - (e.Delta / 120 * _rowHeight), 0, _maxScroll);
            _hovered = IndexAt(e.Location);
            Invalidate();
        }

        base.OnMouseWheel(e);
    }

    /// <summary>Brings a row fully into view, for keyboard navigation in a scrolled list.</summary>
    private void ScrollTo(int index)
    {
        if (_maxScroll <= 0)
        {
            return;
        }

        Rectangle row = RowRect(index);
        if (row.Top < _inset)
        {
            _scroll = Math.Max(0, _scroll - (_inset - row.Top));
        }
        else if (row.Bottom > ClientSize.Height - _inset)
        {
            _scroll = Math.Min(_maxScroll, _scroll + (row.Bottom - (ClientSize.Height - _inset)));
        }
    }

    private Rectangle ButtonRect(Rectangle row, int fromRight) => new(
        row.Right - _inset - ((fromRight + 1) * _button) - (fromRight * _buttonGap),
        row.Y + ((_rowHeight - _button) / 2),
        _button,
        _button);

    private int IndexAt(Point point)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (RowRect(i).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Opens the list at a screen position. It is not modal, so a click on the window behind
    /// it, or anywhere else, closes it without picking anything.
    /// </summary>
    /// <param name="flipAbove">
    /// The height of the control this drops from, plus the gap below it, so a list that has to
    /// open upward clears that control instead of covering it.
    /// </param>
    public void Open(Point at, IWin32Window? owner, int flipAbove)
    {
        Fit(Screen.FromPoint(at).WorkingArea);
        Place(at, flipAbove);

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

    /// <summary>
    /// Review mode only: shows the tooltip on the first row that has one, the same way hovering it
    /// would - so the dark-tooltip fix can be screenshotted directly instead of trusted by reading
    /// the code, and the effect list itself does not need a mouse hover the review harness cannot
    /// send reliably (see <c>WORK.md</c> notes on UI Automation and this app's controls).
    /// </summary>
    internal void ShowTipForReview()
    {
        int row = _items.FindIndex(item => item.Hint != null || item.Editable || item.Renamable);
        if (row < 0)
        {
            return;
        }

        string tip = _items[row].Hint ?? Strings.PresetShortcutHint;
        Rectangle rect = RowRect(row);
        _tipRow = row;
        _shortcutTip.Show(tip, this, rect.Left + 12, rect.Top + 20, 4000);
    }

    /// <summary>
    /// Review mode only: renders exactly what <see cref="Theme.PaintToolTip"/> paints for a real
    /// tooltip, onto a bitmap instead of a live system tooltip window - a manually shown
    /// <see cref="ToolTip"/> only ever paints for the foreground window, which a headless/automated
    /// session does not reliably hand a newly opened one, and <see cref="ShowTipForReview"/> alone
    /// could not be proven on that surface. This calls the same drawing code either way.
    /// </summary>
    internal void RenderTipForReview(Graphics g, Rectangle bounds, string text)
    {
        Theme.PaintToolTip(new DrawToolTipEventArgs(g, this, this, bounds, text, Theme.Surface, Theme.Text, Font));
    }

    /// <summary>
    /// Set only by <see cref="Program"/>'s review mode: with nothing else on screen to hold focus
    /// first, anything stealing it (another window, another process) would otherwise close this
    /// popup before <see cref="ShowTipForReview"/>'s tooltip is ever seen - same reason
    /// <see cref="SettingsPopup"/> has one of its own.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool KeepOpenOnDeactivate { private get; set; }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);

        if (!KeepOpenOnDeactivate)
        {
            Close();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                // A pending delete is what Escape cancels first; the list stays open.
                if (_confirming >= 0)
                {
                    _confirming = -1;
                    Invalidate();
                }
                else
                {
                    Close();
                }

                break;

            case Keys.Down:
            case Keys.Up:
            case Keys.Home:
            case Keys.End:
                MoveHighlight(e.KeyCode);
                break;

            case Keys.Enter:
            case Keys.Space:
                Choose(_highlighted);
                break;

            case Keys.F2:
                // Same as the pencil, for anyone not using the mouse.
                if (Editable(_highlighted))
                {
                    EditRequested?.Invoke(this, _items[_highlighted]);
                    Close();
                }

                break;

            case Keys.Delete:
                if (_highlighted < _items.Count && _items[_highlighted].Editable)
                {
                    _confirming = _confirming == _highlighted ? -1 : _highlighted;
                    Invalidate();
                }

                break;

            default:
                // Everything else, Tab above all, is left alone - marking every key handled
                // swallowed it.
                base.OnKeyDown(e);
                return;
        }

        e.Handled = true;
        base.OnKeyDown(e);
    }

    private bool Editable(int index) =>
        index >= 0 && index < _items.Count && (_items[index].Editable || _items[index].Renamable);

    /// <summary>
    /// Moves the highlight, stepping over the command row: it is an action, not one of the
    /// choices, so arrowing onto it would be a dead stop.
    /// </summary>
    private void MoveHighlight(Keys key)
    {
        int step = key == Keys.Up ? -1 : 1;
        int next = key switch
        {
            Keys.Home => 0,
            Keys.End => _items.Count - 1,
            _ => _highlighted + step,
        };

        while (next >= 0 && next < _items.Count && (_items[next].IsAction || _items[next].IsHint))
        {
            next += key is Keys.Home ? 1 : key is Keys.End ? -1 : step;
        }

        if (next >= 0 && next < _items.Count)
        {
            _highlighted = next;
            _hovered = -1;
            _confirming = -1;
            _keyboard = true;
            ScrollTo(_highlighted);
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        // Without this the row the pointer left over stays lit while the mouse is somewhere else
        // entirely - OnMouseMove only ever fires inside the list.
        if (_hovered >= 0)
        {
            _hovered = -1;
            Invalidate();
        }

        if (_tipRow >= 0)
        {
            _tipRow = -1;
            _shortcutTip.Hide(this);
        }

        base.OnMouseLeave(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int hovered = IndexAt(e.Location);
        _keyboard = false;

        if (hovered != _hovered)
        {
            // Only the two rows that changed are repainted. Invalidating the whole list rebuilt
            // every effect icon on every mouse move.
            int left = _hovered;
            _hovered = hovered;

            foreach (int row in new[] { left, hovered })
            {
                if (row >= 0)
                {
                    Invalidate(Rectangle.Inflate(RowRect(row), 2, 2));
                }
            }

            if (left < 0 || hovered < 0)
            {
                // The highlight also follows the keyboard selection, which is elsewhere.
                Invalidate(Rectangle.Inflate(RowRect(_highlighted), 2, 2));
            }
        }

        Cursor = hovered >= 0 && !_items[hovered].IsHint ? Cursors.Hand : Cursors.Default;

        // A custom preset row has F2/Delete as their only hint besides the two small icons
        // themselves, and a built-in effect row explains what it does - either way shown for the
        // whole row, not just the icon, so it is easy to find.
        string? tip = hovered < 0
            ? null
            : _items[hovered].Hint ??
              (_items[hovered].Editable || _items[hovered].Renamable ? Strings.PresetShortcutHint : null);

        if (tip != null && hovered != _tipRow)
        {
            _tipRow = hovered;
            _shortcutTip.Show(tip, this, e.Location.X + 12, e.Location.Y + 20, 4000);
        }
        else if (tip == null && _tipRow >= 0)
        {
            _tipRow = -1;
            _shortcutTip.Hide(this);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        int index = IndexAt(e.Location);
        if (index < 0 || _items[index].IsHint)
        {
            return;
        }

        Rectangle row = RowRect(index);

        if (_confirming >= 0)
        {
            // Deleting takes two clicks, and the tick deliberately sits left of where the X
            // was: clicking the same spot twice cancels instead of deleting.
            if (index == _confirming && ButtonRect(row, 1).Contains(e.Location))
            {
                // The list stays open: whoever handles this refills it through Resync, so the row
                // disappears under the pointer and the next one can be deleted straight away.
                _confirming = -1;
                DeleteRequested?.Invoke(this, _items[index]);
                return;
            }

            _confirming = -1;
            Invalidate();
            return;
        }

        if (_items[index].Editable)
        {
            if (ButtonRect(row, 0).Contains(e.Location))
            {
                _confirming = index;
                Invalidate();
                return;
            }

            if (ButtonRect(row, 1).Contains(e.Location))
            {
                EditRequested?.Invoke(this, _items[index]);
                Close();
                return;
            }

            if (ButtonRect(row, 2).Contains(e.Location))
            {
                // Closes the list like edit does, straight into the new copy's editor - staying
                // open just left the copy sitting unnoticed in a list the user had to dismiss
                // by hand to see what they got.
                DuplicateRequested?.Invoke(this, _items[index]);
                Close();
                return;
            }
        }
        else if (_items[index].Renamable && ButtonRect(row, 0).Contains(e.Location))
        {
            EditRequested?.Invoke(this, _items[index]);
            Close();
            return;
        }

        Choose(index);
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
            SelectItem item = _items[i];
            Rectangle row = RowRect(i);
            bool confirming = i == _confirming;

            // Hover, or the keyboard's own position once an arrow key has been used - never the
            // running entry just for being the running entry.
            bool active = !item.IsHint &&
                (confirming || i == _hovered || (_keyboard && _hovered < 0 && i == _highlighted && _confirming < 0));

            if (i == _firstAction)
            {
                using var separator = new Pen(Theme.Border);
                int y = row.Y - (_actionGap / 2);
                g.DrawLine(separator, row.X + Scale(4), y, row.Right - Scale(4), y);
            }

            if (active)
            {
                using GraphicsPath highlight = Theme.RoundedRectangle(row, Scale(7));
                using var brush = new SolidBrush(confirming
                    ? Color.FromArgb(Theme.Dark ? 60 : 26, Theme.Danger)
                    : Theme.AccentSoft);
                g.FillPath(brush, highlight);
            }

            int left = row.X + Scale(8);
            int glyph = Scale(14);
            var icon = new Rectangle(left, row.Y + ((_rowHeight - glyph) / 2), Scale(22), glyph);

            if (item.IsAction)
            {
                PaintPlus(g, new Rectangle(left, icon.Y, glyph, glyph), Theme.Accent);
                left += glyph + Scale(8);
            }
            else if (item.Mode is byte mode)
            {
                EffectPainter.PaintIcon(g, icon, mode, _colour, _icons);
                left = icon.Right + Scale(10);
            }
            else if (item.CustomColours is { Length: > 0 } colours)
            {
                EffectPainter.PaintUserIcon(g, icon, colours, _icons);
                left = icon.Right + Scale(10);
            }

            int buttons = confirming ? (_button * 2) + _buttonGap + _inset
                : item.Editable ? (_button * 3) + (_buttonGap * 2) + _inset
                : item.Renamable ? _button + _inset
                : _inset;
            var text = new Rectangle(left, row.Y, Math.Max(0, row.Right - left - buttons), _rowHeight);
            TextRenderer.DrawText(g, confirming ? Strings.CustomPresetConfirmDelete : item.Text, Font, text,
                confirming ? Theme.Danger : item.IsAction ? Theme.Accent : item.IsHint ? Theme.TextMuted : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (confirming)
            {
                PaintGlyphButton(g, ButtonRect(row, 0), Theme.TextMuted, PaintCross);
                PaintGlyphButton(g, ButtonRect(row, 1), Theme.Danger, PaintCheck);
            }
            else if (item.Editable)
            {
                PaintGlyphButton(g, ButtonRect(row, 0), Theme.TextMuted, PaintCross);
                PaintGlyphButton(g, ButtonRect(row, 1), Theme.TextMuted, PaintPencil);
                PaintGlyphButton(g, ButtonRect(row, 2), Theme.TextMuted, PaintCopy);
            }
            else if (item.Renamable && (i == _hovered || (_keyboard && i == _highlighted)))
            {
                // Only shown on hover/keyboard focus - a channel row is mostly just a choice,
                // and a pencil on every single one would be noise the rest of the time.
                PaintGlyphButton(g, ButtonRect(row, 0), Theme.TextMuted, PaintPencil);
            }
        }

        if (_maxScroll > 0)
        {
            PaintScrollIndicator(g);
        }
    }

    /// <summary>
    /// How far down a list too long for the screen currently sits. Drawn only when there is
    /// something to scroll, in the margin the rows already keep clear on the right, so a list
    /// that fits looks exactly as it did. Nothing to grab: the wheel and the arrow keys already
    /// move the list, and a target this narrow would be a poor one to drag.
    /// </summary>
    private void PaintScrollIndicator(Graphics g)
    {
        int thickness = Scale(3);
        int edgeGap = Scale(3);

        float track = ClientSize.Height - (_inset * 2);
        float thumb = Math.Max(_rowHeight, track * ClientSize.Height / _content);
        float top = _inset + ((track - thumb) * _scroll / _maxScroll);
        float x = Width - thickness - edgeGap;

        Paint(new RectangleF(x, _inset, thickness, track), Theme.Dark ? 38 : 26);
        Paint(new RectangleF(x, top, thickness, thumb), Theme.Dark ? 120 : 96);

        void Paint(RectangleF bounds, int alpha)
        {
            using GraphicsPath shape = Theme.RoundedRectangle(bounds, thickness / 2f);
            using var brush = new SolidBrush(Color.FromArgb(alpha, Theme.Text));
            g.FillPath(brush, shape);
        }
    }

    /// <summary>
    /// The glyphs below take their line weight from the box they are handed rather than from a
    /// flat number of pixels. The box has already grown with the display, so deriving from it is
    /// what keeps a pencil or a cross the same weight relative to its button at every scale -
    /// a fixed 1.6f drew a hairline inside a doubled button.
    /// </summary>
    private static Pen GlyphPen(Rectangle box, Color colour, float weight) =>
        new(colour, Math.Max(1f, box.Width * weight)) { StartCap = LineCap.Round, EndCap = LineCap.Round };

    private static void PaintGlyphButton(Graphics g, Rectangle box, Color colour, Action<Graphics, Rectangle, Color> glyph)
    {
        // Proportional, for the same reason: a flat -6 left the glyph filling a button that had
        // doubled around it.
        int inset = Math.Max(1, box.Width * 6 / 22);
        glyph(g, Rectangle.Inflate(box, -inset, -inset), colour);
    }

    private static void PaintCross(Graphics g, Rectangle box, Color colour)
    {
        using Pen pen = GlyphPen(box, colour, 0.16f);
        g.DrawLine(pen, box.Left, box.Top, box.Right, box.Bottom);
        g.DrawLine(pen, box.Right, box.Top, box.Left, box.Bottom);
    }

    private static void PaintCheck(Graphics g, Rectangle box, Color colour)
    {
        using Pen pen = GlyphPen(box, colour, 0.19f);
        g.DrawLines(pen, new[]
        {
            new PointF(box.Left, box.Top + (box.Height * 0.55f)),
            new PointF(box.Left + (box.Width * 0.36f), box.Bottom),
            new PointF(box.Right, box.Top),
        });
    }

    private static void PaintPlus(Graphics g, Rectangle box, Color colour)
    {
        using Pen pen = GlyphPen(box, colour, 0.18f);
        float cx = box.X + (box.Width / 2f);
        float cy = box.Y + (box.Height / 2f);
        float end = box.Width * 0.1f;
        g.DrawLine(pen, box.Left + end, cy, box.Right - end, cy);
        g.DrawLine(pen, cx, box.Top + end, cx, box.Bottom - end);
    }

    /// <summary>
    /// Two overlapping squares - the front one drawn whole, the back one only where it still
    /// peeks out, rather than erasing the overlap with a fill in a colour that would have to
    /// match whatever is behind this row (plain, hovered or highlighted all differ).
    /// </summary>
    private static void PaintCopy(Graphics g, Rectangle box, Color colour)
    {
        using Pen pen = GlyphPen(box, colour, 0.14f);
        float size = box.Width * 0.62f;
        float offset = box.Width * 0.30f;

        var front = new RectangleF(box.X + offset, box.Y + offset, size, size);
        g.DrawRectangle(pen, front.X, front.Y, front.Width, front.Height);

        g.DrawLine(pen, box.X, box.Y, box.X + size, box.Y);
        g.DrawLine(pen, box.X, box.Y, box.X, box.Y + size);
        g.DrawLine(pen, box.X + size, box.Y, box.X + size, box.Y + offset);
        g.DrawLine(pen, box.X, box.Y + size, box.X + offset, box.Y + size);
    }

    /// <summary>A pencil pointing at the lower left, drawn as a body, a tip and a nib.</summary>
    private static void PaintPencil(Graphics g, Rectangle box, Color colour)
    {
        float x = box.X;
        float y = box.Y;
        float w = box.Width;
        float h = box.Height;

        using var brush = new SolidBrush(colour);
        using var body = new GraphicsPath();
        body.AddPolygon(new[]
        {
            new PointF(x + (w * 0.32f), y + (h * 0.90f)),
            new PointF(x + (w * 0.10f), y + (h * 0.68f)),
            new PointF(x + (w * 0.68f), y + (h * 0.10f)),
            new PointF(x + (w * 0.90f), y + (h * 0.32f)),
        });
        g.FillPath(brush, body);

        using var tip = new GraphicsPath();
        tip.AddPolygon(new[]
        {
            new PointF(x + (w * 0.06f), y + h),
            new PointF(x + (w * 0.26f), y + (h * 0.96f)),
            new PointF(x + (w * 0.04f), y + (h * 0.74f)),
        });
        g.FillPath(brush, tip);
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

    // WinForms scales the control's own bounds on a display change, but nothing inside a custom
    // paint - so the chips are laid out from the current dpi rather than from flat numbers, or
    // they would sit in the left half of a strip that has grown to twice the width.
    private int Chip => this.Scaled(ChipAt96);

    private int Gap => this.Scaled(GapAt96);

    private int Inset => this.Scaled(InsetAt96);

    private int _hoveredChip = -1;

    /// <summary>Which chip the keyboard is on, so the strip is not a dead tab stop.</summary>
    private int _focusedChip;

    public ColourStrip()
    {
        Height = Chip + (Inset * 2);
        Width = ((Palette.Length + 1) * (Chip + Gap)) - Gap + (Inset * 2);
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
        // watching TextChanged never hears a keystroke - which is what left the preset editor's
        // Create button greyed out however much was typed into it.
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
        _box.SetBounds(inset, Math.Max(0, (Height - height) / 2),
            Math.Max(1, Width - (inset * 2)), height);
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
/// Opens where <see cref="ColourStrip"/>'s custom chip is clicked, replacing the plain
/// Windows colour dialog that used to sit there.
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
    // and these are painted inside it - held as fixed 96 dpi rectangles they left the picker in
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
        // which used to push the colour the user had just cancelled with Escape.
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
        using (var huePen = new Pen(Color.White, this.ScaledF(2f)))
        {
            g.DrawLine(huePen, hueX, HueRect.Y - hueOverhang, hueX, HueRect.Bottom + hueOverhang);
        }
        using (var hueOutline = new Pen(Color.FromArgb(90, 0, 0, 0), this.ScaledF(1f)))
        {
            g.DrawLine(hueOutline, hueX - 1, HueRect.Y - hueOverhang, hueX - 1, HueRect.Bottom + hueOverhang);
            g.DrawLine(hueOutline, hueX + 1, HueRect.Y - hueOverhang, hueX + 1, HueRect.Bottom + hueOverhang);
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

    /// <summary>
    /// Wide enough for its own label plus padding, so a longer translation is never clipped -
    /// the button has no ellipsis and no auto-size of its own.
    /// </summary>
    private int? _fitPadding;

    public void FitToText(int padding = 22)
    {
        // Measured and assigned outright rather than only grown: a button whose label is later
        // swapped for a shorter one (the preset editor's Create/Replace/Save button as its name
        // field is typed into) has to be able to shrink back, not just stay at its widest ever.
        // The padding is scaled because the measured text already is.
        _fitPadding = padding;
        Width = this.MeasuredWidth(Text, Font) + (this.Scaled(padding) * 2);
    }

    /// <summary>
    /// A button sized to its label keeps that size until something asks again, so a move to a
    /// display at another scale left it as wide as the text used to be.
    /// </summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);

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
/// Settings' Reset and the preset editor's Delete each used to hand-roll this same timer and
/// text swap; this is the one copy.
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
            ValueChanged?.Invoke(this, EventArgs.Empty);
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
        keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown
            || base.IsInputKey(keyData);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int before = _value;
        Value = _value + (e.Delta / 120 * 5);

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
            Keys.Left => _value - 5,
            Keys.Right => _value + 5,
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
