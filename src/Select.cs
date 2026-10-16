using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
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
/// Used for a short explanation among the choices.
/// </param>
internal sealed record SelectItem(string Key, string Text, byte? Mode, Color[]? CustomColours = null,
    bool Editable = false, bool IsAction = false, bool Renamable = false, bool IsHint = false,
    string? Hint = null);

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

    /// <summary>The closed control paints its choice by hand, so it has to hand the text over as its value.</summary>
    protected override AccessibleObject CreateAccessibilityInstance() => new SelectAccessibleObject(this);

    private sealed class SelectAccessibleObject : ControlAccessibleObject
    {
        public SelectAccessibleObject(Select owner) : base(owner)
        {
        }

        public override string? Value
        {
            get => ((Select)Owner!).Selected?.Text;
            set { }
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.F4 || base.IsInputKey(keyData);

    /// <summary>
    /// Alt+Down and F4 open the list, as on a system combo box; Up and Down step the choice
    /// without opening it. Enter and Space already open it (<see cref="FlatControl"/>).
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool open = e.KeyCode == Keys.F4 || (e.KeyCode == Keys.Down && e.Alt);
        if (open)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.KeyCode is Keys.Up or Keys.Down && !e.Alt)
        {
            StepSelection(e.KeyCode == Keys.Down ? 1 : -1);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Moves the choice to the next entry that can be picked, as a click on it would.</summary>
    private void StepSelection(int direction)
    {
        if (!Enabled)
        {
            return;
        }

        int current = Selected == null ? -1 : _items.IndexOf(Selected);
        for (int i = current + direction; i >= 0 && i < _items.Count; i += direction)
        {
            SelectItem candidate = _items[i];
            if (candidate.IsHint || candidate.IsAction)
            {
                continue;
            }

            Selected = candidate;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
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

            widest = Math.Max(widest, Theme.MeasuredWidth(item.Text, Font));
        }

        // The measured text already grew with the display, so the fixed parts around it have to as
        // well or the control comes out too narrow for its own longest entry.
        return widest + this.Scaled(TextInset + ChevronRoom +
            (withIcon ? IconWidth + IconGap : 0));
    }

    /// <summary>How wide the list this opens needs to be for its longest entry, see
    /// <see cref="SelectPopup.WidthFor"/>.</summary>
    public int PreferredPopupWidth() => SelectPopup.WidthFor(_items, Font, DeviceDpi);

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
        AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
    }

    /// <summary>
    /// Shared by every <see cref="Select"/> in the process. A sibling drop down still open when
    /// this one is clicked would take an extra click or two to get out of the way: the click
    /// that lands on this control first only deactivates the other popup, and that popup's own
    /// close races this one's open. Closing it here, synchronously, before this one is even
    /// created removes the race - by the time the new popup opens, the old one is fully gone.
    /// </summary>
    private static SelectPopup? _openPopup;

    /// <summary>This control's own open list, for pushing item changes into while it is up.</summary>
    private SelectPopup? _popup;

#if REVIEW
    /// <summary>Surface tool only: opens the list exactly as a click does and keeps it open
    /// while nothing on an automated desktop holds the focus.</summary>
    internal void OpenForReview()
    {
        OnClick(EventArgs.Empty);
        if (_openPopup != null)
        {
            _openPopup.KeepOpenOnDeactivate = true;
        }
    }
#endif

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
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
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

        // Everything here is custom painted, so nothing grows with the display on its own - an
        // unscaled icon would stay 22x14 next to text that had doubled.
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
    /// opens with nothing marked at all: marking the current entry would make the highlight jump
    /// back to it whenever the pointer left the rows, which reads as flicker rather than as an answer to
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
    private readonly ThemedTip _shortcutTip = new();
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

        // A placeholder height, good enough until Open() knows which screen this actually opens
        // on and sizes it for real - the constructor cannot know that yet, and guessing the
        // primary screen was wrong on a second monitor with a shorter work area.
        ClientSize = new Size(width, Math.Min(_content, _rowHeight * 3));
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    private int Scale(int length) => length * _dpi / 96;

    /// <summary>
    /// The narrowest this list can open at without cutting an entry short: each row's text plus
    /// what the row paint puts beside it - the icon or plus on the left, the pencil or the edit
    /// buttons on the right. Mirrors the row layout in OnPaint, so a change to one belongs in
    /// the other.
    /// </summary>
    public static int WidthFor(IEnumerable<SelectItem> items, Font font, int dpi)
    {
        int Scale(int length) => length * dpi / 96;

        int widest = 0;
        foreach (SelectItem item in items)
        {
            int left = Scale(8) + (item.IsAction ? Scale(14) + Scale(8)
                : item.Mode is not null || item.CustomColours is { Length: > 0 } ? Scale(22) + Scale(10)
                : 0);
            int right = item.Editable ? (Scale(ButtonAt96) * 3) + (Scale(ButtonGapAt96) * 2) + Scale(InsetAt96)
                : item.Renamable ? Scale(ButtonAt96) + Scale(InsetAt96)
                : Scale(InsetAt96);
            widest = Math.Max(widest, left + TextRenderer.MeasureText(item.Text, font).Width + right);
        }

        return widest + (Scale(InsetAt96) * 2);
    }

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
    /// Takes the item list again while the window stays open. Deleting a preset from a row must
    /// not close the whole list, or removing three of them would mean opening it three times - the
    /// list is where presets are managed, so it has to survive managing them.
    /// </summary>
    public void Resync(IEnumerable<SelectItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        Measure();

        _confirming = -1;
        _hovered = -1;
        HideRowTip();
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

    /// <summary>Wheel movement not yet worth a whole notch - precision touchpads report small deltas.</summary>
    private int _wheelRemainder;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_maxScroll > 0)
        {
            _wheelRemainder += e.Delta;
            int notches = _wheelRemainder / 120;
            _wheelRemainder -= notches * 120;
            _scroll = Math.Clamp(_scroll - (notches * _rowHeight), 0, _maxScroll);
            _hovered = IndexAt(e.Location);
            HideRowTip();
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

    /// <summary>A preset row has edit, duplicate and delete on the keyboard; a channel row only renames.</summary>
    private static string ShortcutHintFor(SelectItem item) =>
        item.Editable ? Strings.PresetShortcutHint : Strings.ChannelShortcutHint;

#if REVIEW
    /// <summary>
    /// Review mode only: shows the tooltip on the first row that has one, the same way hovering it
    /// would - so the tooltip's dark theme can be checked in a screenshot instead of trusted by reading
    /// the code, and the effect list itself does not need a mouse hover the review harness cannot
    /// send reliably against these controls.
    /// </summary>
    internal void ShowTipForReview()
    {
        int row = _items.FindIndex(item => item.Hint != null || item.Editable || item.Renamable);
        if (row < 0)
        {
            return;
        }

        string tip = _items[row].Hint ?? ShortcutHintFor(_items[row]);
        Rectangle rect = RowRect(row);
        _tipRow = row;
        _shortcutTip.Show(tip, this, rect.Left + this.Scaled(12), rect.Top + this.Scaled(20), 4000);
    }

#if REVIEW
    /// <summary>
    /// Surface tool only: paints the list with <paramref name="hovered"/> lit onto
    /// <paramref name="onto"/> (a fresh bitmap when null), limited to <paramref name="clip"/>
    /// the way a partial invalidation limits a real repaint.
    /// </summary>
    internal Bitmap PaintForReview(int hovered, Bitmap? onto, Rectangle clip)
    {
        Bitmap bitmap = onto ?? new Bitmap(ClientSize.Width, ClientSize.Height);
        _hovered = hovered;
        using Graphics g = Graphics.FromImage(bitmap);
        g.SetClip(clip);
        OnPaint(new PaintEventArgs(g, clip));
        return bitmap;
    }

    /// <summary>Surface tool only: what a hover change on <paramref name="row"/> invalidates.</summary>
    internal Rectangle HoverAreaForReview(int row) => Rectangle.Inflate(RowRect(row), this.Scaled(2), this.Scaled(2));
#endif

    /// <summary>Drops the hovered row's tip when the row under it changes without the pointer
    /// moving: the list resynced, scrolled, or the keyboard took over.</summary>
    private void HideRowTip()
    {
        if (_tipRow >= 0)
        {
            _tipRow = -1;
            _shortcutTip.Hide(this);
        }
    }

    /// <summary>
    /// Review mode only: renders exactly what <see cref="Theme.PaintToolTip"/> paints for a real
    /// tooltip onto a bitmap, so the tip's face can be checked without capturing its window.
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
#endif

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);

#if REVIEW
        if (KeepOpenOnDeactivate)
        {
            return;
        }
#endif

        Close();
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
                if (_confirming >= 0 && _confirming == _highlighted && _highlighted < _items.Count)
                {
                    // The armed row's tick, from the keyboard: applying the preset here would do
                    // the opposite of what the red row is asking.
                    SelectItem armed = _items[_highlighted];
                    _confirming = -1;
                    DeleteRequested?.Invoke(this, armed);
                    Invalidate();
                }
                else
                {
                    Choose(_highlighted);
                }

                break;

            case Keys.D when e.Control:
                // Same as the copy button, closing the list the same way.
                if (_highlighted >= 0 && _highlighted < _items.Count && _items[_highlighted].Editable)
                {
                    DuplicateRequested?.Invoke(this, _items[_highlighted]);
                    Close();
                }

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
    /// Moves the highlight. Hint rows are stepped over - they cannot be picked - but the command
    /// row can be reached, so "New preset" is not a mouse-only entry.
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

        while (next >= 0 && next < _items.Count && _items[next].IsHint)
        {
            next += key is Keys.Home ? 1 : key is Keys.End ? -1 : step;
        }

        if (next >= 0 && next < _items.Count)
        {
            _highlighted = next;
            _hovered = -1;
            _confirming = -1;
            _keyboard = true;
            HideRowTip();
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

        HideRowTip();

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
                    Invalidate(Rectangle.Inflate(RowRect(row), this.Scaled(2), this.Scaled(2)));
                }
            }

            if (left < 0 || hovered < 0)
            {
                // The highlight also follows the keyboard selection, which is elsewhere.
                Invalidate(Rectangle.Inflate(RowRect(_highlighted), this.Scaled(2), this.Scaled(2)));
            }
        }

        Cursor = hovered >= 0 && !_items[hovered].IsHint ? Cursors.Hand : Cursors.Default;

        // A custom preset row has F2/Delete as their only hint besides the two small icons
        // themselves, and a built-in effect row explains what it does - either way shown for the
        // whole row, not just the icon, so it is easy to find.
        string? tip = hovered < 0
            ? null
            : _items[hovered].Hint ??
              (_items[hovered].Editable || _items[hovered].Renamable ? ShortcutHintFor(_items[hovered]) : null);

        if (tip != null && hovered != _tipRow)
        {
            _tipRow = hovered;
            _shortcutTip.Show(tip, this, e.Location.X + this.Scaled(12), e.Location.Y + this.Scaled(20), 4000);
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

            // A hover change invalidates two rows; the rest keep what is on screen, icons included.
            if (!e.ClipRectangle.IntersectsWith(Rectangle.Inflate(row, Scale(2), Scale(2))))
            {
                continue;
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
    /// a fixed 1.6f would draw a hairline inside a doubled button.
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
