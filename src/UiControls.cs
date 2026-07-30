using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    bool Editable = false, bool IsAction = false, bool Renamable = false, bool IsHint = false);

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
            _surface.Dispose();
        }

        base.Dispose(disposing);
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
                using var wash = new SolidBrush(Color.FromArgb(Hovered ? 52 : 74, 0, 0, 0));
                surface.FillRectangle(wash, area);

                if (_busy)
                {
                    using var dim = new SolidBrush(Color.FromArgb(110, BackColor));
                    surface.FillRectangle(dim, area);
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
            using var brush = new SolidBrush(_busy ? Theme.Blend(fill, BackColor, 110 / 255.0) : fill);
            g.FillPath(brush, path);
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

    /// <summary>An entry marked as a command was picked; the selection is left alone.</summary>
    public event EventHandler<SelectItem>? ActionPicked;

    /// <summary>The edit button of an editable entry was pressed.</summary>
    public event EventHandler<SelectItem>? EditRequested;

    /// <summary>The delete button of an editable entry was pressed and then confirmed.</summary>
    public event EventHandler<SelectItem>? DeleteRequested;

    /// <summary>
    /// Least width the drop down opens at. The control itself can be narrow, while its list
    /// still has room for a name plus the edit and delete buttons.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int PopupWidth { get; set; }

    /// <summary>
    /// The width this control needs for its longest entry, icon, chevron and padding included.
    /// Measured rather than assumed: the entries are translated, and a channel can be renamed to
    /// anything, so any fixed number ends up clipping somebody's text.
    /// </summary>
    /// <param name="withIcon">
    /// False for a list whose entries carry no icon - the channel selector - so it is not padded
    /// out by room nothing draws in. Every pixel it keeps is one the effect list loses.
    /// </param>
    public int PreferredWidthForItems(bool withIcon = true)
    {
        using Graphics g = CreateGraphics();

        var widest = 0;
        foreach (SelectItem item in _items)
        {
            widest = Math.Max(widest, TextRenderer.MeasureText(g, item.Text, Font).Width);
        }

        // 11 left inset + 22 icon + 10 gap + text + 26 for the chevron.
        return widest + 11 + (withIcon ? 32 : 0) + 26;
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

        var popup = new SelectPopup(_items, Selected, Colour, Math.Max(Width, PopupWidth), Font);

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
    private const int Button = 22;
    private const int ButtonGap = 2;

    /// <summary>Separator plus breathing space above the first command row.</summary>
    private const int ActionGap = 11;

    private readonly List<SelectItem> _items;
    private readonly Color _colour;
    private readonly int _firstAction;

    /// <summary>Shared by every icon in the list, instead of one buffer per icon per paint.</summary>
    private readonly EffectSurface _icons = new();

    private int _highlighted;
    private int _hovered = -1;

    /// <summary>How far the list is scrolled, in pixels, when it does not all fit on screen.</summary>
    private int _scroll;

    private int _maxScroll;

    /// <summary>The row whose delete button is waiting to be confirmed, or -1.</summary>
    private int _confirming = -1;

    public SelectPopup(List<SelectItem> items, SelectItem? selected, Color colour, int width, Font font)
    {
        _items = items;
        _colour = colour;
        _highlighted = selected == null ? 0 : Math.Max(0, items.IndexOf(selected));
        _firstAction = items.FindIndex(item => item.IsAction);

        Font = font;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        KeyPreview = true;

        int content = (items.Count * RowHeight) + (Inset * 2) + (_firstAction >= 0 ? ActionGap : 0);

        // Enough custom presets and the list would be taller than the screen, putting its last
        // rows out of reach. It stops at the work area and scrolls instead.
        int room = Screen.PrimaryScreen?.WorkingArea.Height ?? content;
        int height = Math.Min(content, Math.Max(RowHeight * 3, room - 80));
        _maxScroll = Math.Max(0, content - height);

        ClientSize = new Size(width, height);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icons.Dispose();
        }

        base.Dispose(disposing);
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

    /// <summary>The edit button of an editable row was pressed.</summary>
    public event EventHandler<SelectItem>? EditRequested;

    /// <summary>A delete was asked for and then confirmed on the row itself.</summary>
    public event EventHandler<SelectItem>? DeleteRequested;

    private Rectangle RowRect(int index) => new(
        Inset,
        Inset + (index * RowHeight) + (_firstAction >= 0 && index >= _firstAction ? ActionGap : 0) - _scroll,
        Width - (Inset * 2),
        RowHeight);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_maxScroll > 0)
        {
            _scroll = Math.Clamp(_scroll - (e.Delta / 120 * RowHeight), 0, _maxScroll);
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
        if (row.Top < Inset)
        {
            _scroll = Math.Max(0, _scroll - (Inset - row.Top));
        }
        else if (row.Bottom > ClientSize.Height - Inset)
        {
            _scroll = Math.Min(_maxScroll, _scroll + (row.Bottom - (ClientSize.Height - Inset)));
        }
    }

    private static Rectangle ButtonRect(Rectangle row, int fromRight) => new(
        row.Right - 6 - ((fromRight + 1) * Button) - (fromRight * ButtonGap),
        row.Y + ((RowHeight - Button) / 2),
        Button,
        Button);

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
                if (_items[_highlighted].Editable)
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
            ScrollTo(_highlighted);
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int hovered = IndexAt(e.Location);
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
                DeleteRequested?.Invoke(this, _items[index]);
                Close();
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
            bool active = !item.IsHint &&
                (confirming || (_hovered >= 0 ? i == _hovered : i == _highlighted && _confirming < 0));

            if (i == _firstAction)
            {
                using var separator = new Pen(Theme.Border);
                int y = row.Y - (ActionGap / 2);
                g.DrawLine(separator, row.X + 4, y, row.Right - 4, y);
            }

            if (active)
            {
                using GraphicsPath highlight = Theme.RoundedRectangle(row, 7);
                using var brush = new SolidBrush(confirming
                    ? Color.FromArgb(Theme.Dark ? 60 : 26, Theme.Danger)
                    : Theme.AccentSoft);
                g.FillPath(brush, highlight);
            }

            int left = row.X + 8;
            var icon = new Rectangle(left, row.Y + ((RowHeight - 14) / 2), 22, 14);

            if (item.IsAction)
            {
                PaintPlus(g, new Rectangle(left, row.Y + ((RowHeight - 14) / 2), 14, 14), Theme.Accent);
                left += 14 + 8;
            }
            else if (item.Mode is byte mode)
            {
                EffectPainter.PaintIcon(g, icon, mode, _colour, _icons);
                left = icon.Right + 10;
            }
            else if (item.CustomColours is { Length: > 0 } colours)
            {
                EffectPainter.PaintUserIcon(g, icon, colours, _icons);
                left = icon.Right + 10;
            }

            int buttons = confirming || item.Editable ? (Button * 2) + ButtonGap + 6
                : item.Renamable ? Button + 6
                : 6;
            var text = new Rectangle(left, row.Y, Math.Max(0, row.Right - left - buttons), RowHeight);
            TextRenderer.DrawText(g, confirming ? Strings.CustomPresetConfirmDelete : item.Text, Font, text,
                confirming ? Theme.Danger : item.IsAction ? Theme.Accent : Theme.Text,
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
            }
            else if (item.Renamable && (i == _hovered || i == _highlighted))
            {
                // Only shown on hover/keyboard focus - a channel row is mostly just a choice,
                // and a pencil on every single one would be noise the rest of the time.
                PaintGlyphButton(g, ButtonRect(row, 0), Theme.TextMuted, PaintPencil);
            }
        }
    }

    private static void PaintGlyphButton(Graphics g, Rectangle box, Color colour, Action<Graphics, Rectangle, Color> glyph)
    {
        glyph(g, Rectangle.Inflate(box, -6, -6), colour);
    }

    private static void PaintCross(Graphics g, Rectangle box, Color colour)
    {
        using var pen = new Pen(colour, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, box.Left, box.Top, box.Right, box.Bottom);
        g.DrawLine(pen, box.Right, box.Top, box.Left, box.Bottom);
    }

    private static void PaintCheck(Graphics g, Rectangle box, Color colour)
    {
        using var pen = new Pen(colour, 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, new[]
        {
            new PointF(box.Left, box.Top + (box.Height * 0.55f)),
            new PointF(box.Left + (box.Width * 0.36f), box.Bottom),
            new PointF(box.Right, box.Top),
        });
    }

    private static void PaintPlus(Graphics g, Rectangle box, Color colour)
    {
        using var pen = new Pen(colour, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float cx = box.X + (box.Width / 2f);
        float cy = box.Y + (box.Height / 2f);
        g.DrawLine(pen, box.Left + 1, cy, box.Right - 1, cy);
        g.DrawLine(pen, cx, box.Top + 1, cx, box.Bottom - 1);
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

    private const int Chip = 24;
    private const int Gap = 9;
    private const int Inset = 4;

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

            bool keyboard = Focused && ShowFocusCues && i == _focusedChip;

            if (i == _hoveredChip || active || keyboard)
            {
                using var ring = new Pen(
                    active || keyboard ? Theme.Accent : Color.FromArgb(120, Theme.Accent),
                    keyboard && !active ? 1.6f : 2f);
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
        _box.LostFocus += (_, _) => Invalidate();

        // Typing changes the inner box, not this control, so without passing the event on a host
        // watching TextChanged never hears a keystroke - which is what left the preset editor's
        // Create button greyed out however much was typed into it.
        _box.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
        _box.KeyDown += (_, e) => Accepted?.Invoke(this, e);
        Controls.Add(_box);

        Height = 34;
    }

    /// <summary>Key presses inside the field, so a host can act on Enter.</summary>
    public event KeyEventHandler? Accepted;

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
        int height = Math.Min(_box.PreferredHeight, Math.Max(1, Height - 6));
        _box.SetBounds(SidePadding, Math.Max(0, (Height - height) / 2),
            Math.Max(1, Width - (SidePadding * 2)), height);
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

    /// <summary>Set once the popup is going away, so a last-moment Leave cannot apply anything.</summary>
    private bool _closing;
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
        Font = Theme.Ui;
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
        if (_closing)
        {
            return;
        }

        string text = _hex.Text.Trim().TrimStart('#');
        if (text.Length == 6 && text.All(Uri.IsHexDigit) &&
            int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
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
        Radius = 9;
        ForeColor = Theme.Accent;
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
    public void FitToText(int padding = 22)
    {
        using Graphics g = CreateGraphics();
        Width = Math.Max(Width, TextRenderer.MeasureText(g, Text, Font).Width + (padding * 2));
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
/// A slim slider. Used for the brightness the effect colour is scaled to before it is sent -
/// the controller has no brightness of its own.
/// </summary>
internal sealed class Slider : FlatControl
{
    private const int TrackHeight = 6;
    private const int Knob = 16;

    private readonly EffectSurface _surface = new();
    private readonly System.Windows.Forms.Timer _commit = new() { Interval = 250 };

    private int _value = 100;
    private bool _dragging;

    public Slider()
    {
        Height = 24;

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
        _dragging = true;
        SetFromPoint(e.X);
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
            ValueCommitted?.Invoke(this, EventArgs.Empty);
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

        using (var outline = new Pen(Hovered || _dragging ? Theme.Accent : Theme.Border, 1.4f))
        {
            g.DrawEllipse(outline, knob);
        }

        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(Color.FromArgb(130, Theme.Accent), 2f);
            g.DrawEllipse(ring, RectangleF.Inflate(knob, 2, 2));
        }
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

/// <summary>
/// Small popup to give one channel a name of its own. Not modal - a click anywhere else
/// dismisses it without saving, same as every popup here except the preset editor.
/// </summary>
internal sealed class RenamePopup : Form
{
    private const int Pad = 14;
    private const int FieldWidth = 200;

    private readonly TextField _name = new();
    private readonly PillButton _save = new();
    private readonly PillButton _reset = new();

    public RenamePopup(string currentName)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        DoubleBuffered = true;
        KeyPreview = true;

        _name.Location = new Point(Pad, Pad);
        _name.Width = FieldWidth;
        _name.Text = currentName;
        _name.MaxLength = 30;
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
        _save.Height = 30;
        _save.Width = 90;
        _save.Location = new Point(Pad, _name.Bottom + 10);
        _save.Click += (_, _) => Commit();
        Controls.Add(_save);
        _save.FitToText(16);

        _reset.Text = Strings.ChannelRenameReset;
        _reset.Height = 30;
        _reset.Width = 96;
        _reset.Fill = Theme.NeutralSoft;
        _reset.ForeColor = Theme.TextMuted;
        _reset.Click += (_, _) =>
        {
            Renamed?.Invoke(this, "");
            Close();
        };
        Controls.Add(_reset);
        _reset.FitToText(14);

        // Placed and sized after both buttons know their own width, so "Zurücksetzen" is neither
        // clipped nor overlapping Save.
        _reset.Location = new Point(_save.Right + 8, _name.Bottom + 10);
        ClientSize = new Size(
            Math.Max(Pad + FieldWidth + Pad, _reset.Right + Pad),
            _reset.Bottom + Pad);
        _name.Width = ClientSize.Width - (Pad * 2);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>Raised with the new name, or an empty one when Reset was chosen.</summary>
    public event EventHandler<string>? Renamed;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return parameters;
        }
    }

    private void Commit()
    {
        Renamed?.Invoke(this, _name.Text.Trim());
        Close();
    }

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
        _name.FocusInput();
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

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Prepare(e.Graphics);
        e.Graphics.Clear(Theme.Surface);
    }
}
