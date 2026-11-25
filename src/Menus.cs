using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>The app's own look for every right-click menu: the tray menu and the text boxes.</summary>
internal static class ThemedMenu
{
    /// <summary>Renderer, colours, font, rounded corners and row spacing for <paramref name="menu"/>.
    /// The colours are set again each time it opens, so a switch between light and dark mode
    /// reaches a menu built before it.</summary>
    public static void Style(ToolStripDropDown menu)
    {
        menu.Renderer = new MenuRenderer();
        menu.Font = Theme.Menu;
        Recolour(menu);
        menu.HandleCreated += (_, _) => Theme.RoundWindowCorners(menu.Handle);
        menu.Opening += (_, _) =>
        {
            Recolour(menu);
            Space(menu);
        };
    }

    /// <summary>Gives <paramref name="box"/> a themed menu with the usual editing commands in
    /// place of the grey one Windows draws for a text box.</summary>
    public static void AttachTo(TextBoxBase box)
    {
        var menu = new ContextMenuStrip();
        Style(menu);

        void Add(Func<string> text, char? key, Func<TextBoxBase, bool> canRun, Action run)
        {
            var item = new ToolStripMenuItem { Tag = new TextCommand(text, key, canRun) };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }

        bool Editable(TextBoxBase b) => !b.ReadOnly && b.Enabled;

        Add(() => Strings.TextUndo, 'Z', b => Editable(b) && b.CanUndo, box.Undo);
        menu.Items.Add(new ToolStripSeparator());
        Add(() => Strings.TextCut, 'X', b => Editable(b) && b.SelectionLength > 0, box.Cut);
        Add(() => Strings.TextCopy, 'C', b => b.SelectionLength > 0, box.Copy);
        Add(() => Strings.TextPaste, 'V', b => Editable(b) && ClipboardHasText(), box.Paste);
        Add(() => Strings.TextDelete, null, b => Editable(b) && b.SelectionLength > 0, () => box.SelectedText = "");
        menu.Items.Add(new ToolStripSeparator());
        Add(() => Strings.TextSelectAll, 'A', b => b.TextLength > 0 && b.SelectionLength < b.TextLength, box.SelectAll);

        menu.Opening += (_, _) => Prepare(box);
        box.ContextMenuStrip = menu;
        box.Disposed += (_, _) => menu.Dispose();
    }

    /// <summary>Labels and enables the text box menu for this moment: the language can change
    /// while the box lives, and what can be done depends on the selection and the clipboard.</summary>
    internal static void Prepare(TextBoxBase box)
    {
        if (box.ContextMenuStrip == null)
        {
            return;
        }

        foreach (ToolStripMenuItem item in box.ContextMenuStrip.Items.OfType<ToolStripMenuItem>())
        {
            if (item.Tag is TextCommand command)
            {
                item.Text = command.Text();
                item.ShortcutKeyDisplayString = command.Key is char letter
                    ? $"{Strings.HotkeyModifierControl}+{letter}"
                    : null;
                item.Enabled = command.CanRun(box);
            }
        }
    }

    private sealed record TextCommand(Func<string> Text, char? Key, Func<TextBoxBase, bool> CanRun);

    private static bool ClipboardHasText()
    {
        try
        {
            return Clipboard.ContainsText();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or System.Threading.ThreadStateException)
        {
            // Another program holds the clipboard open; offering Paste costs nothing then.
            return true;
        }
    }

    private static void Recolour(ToolStripDropDown menu)
    {
        menu.BackColor = Theme.Surface;
        menu.ForeColor = Theme.Text;
    }

    /// <summary>
    /// Room above the first and below the last entry, and a little more height per row, so the
    /// rounded highlight does not sit against the menu's own rounded edge. Applied as the menu
    /// opens, which also catches entries rebuilt since.
    /// </summary>
    private static void Space(ToolStripDropDown menu)
    {
        // The menu's own dpi: the tray menu opens on the screen with the notification area,
        // which need not be the one the window is on.
        int Scale(int length) => length * menu.DeviceDpi / 96;
        int edge = Scale(4);
        menu.Padding = new Padding(menu.Padding.Left, edge, menu.Padding.Right, edge);
        foreach (ToolStripItem item in menu.Items)
        {
            if (item is ToolStripMenuItem)
            {
                item.Padding = new Padding(0, Scale(3), 0, Scale(3));
            }
        }
    }
}

/// <summary>Paints a menu in the same flat, themed style as the window.</summary>
internal sealed class MenuRenderer : ToolStripProfessionalRenderer
{
    public MenuRenderer() : base(new MenuColours())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // A drop down gets its border from the compositor along with the rounded corners; a
        // square one drawn here would show its corners poking out of the rounded window.
        // Windows 10 rounds nothing and draws no border of its own, so the menu keeps its drawn one.
        if (e.ToolStrip is not ToolStripDropDown || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            base.OnRenderToolStripBorder(e);
        }
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is not { Selected: true, Enabled: true })
        {
            return;
        }

        // A rounded highlight set in from the menu's edges, matching the rows of the window's own
        // lists, instead of a square bar that runs into the rounded corners.
        Rectangle row = new(Point.Empty, e.Item.Size);
        int inset = Math.Max(2, row.Height / 8);
        var area = new RectangleF(inset, 1, row.Width - (inset * 2) - 1, row.Height - 3);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRectangle(area, row.Height / 5f);
        using var brush = new SolidBrush(Theme.AccentSoft);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item?.Enabled == true ? Theme.Text : Theme.TextMuted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // The lighting entry shows its state through a dot rather than a Windows tick box.
        if (e.Item is not ToolStripMenuItem { Checked: true })
        {
            return;
        }

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Theme.Accent);

        // Sized from the box the menu gave it rather than in fixed pixels: a renderer is not a
        // control, so it has no dpi of its own to scale against, but that box already carries the
        // menu's, which is the same thing one step earlier.
        Rectangle box = e.ImageRectangle;
        int dot = Math.Max(4, Math.Min(box.Width, box.Height) / 2);
        e.Graphics.FillEllipse(brush, box.X + ((box.Width - dot) / 2), box.Y + ((box.Height - dot) / 2), dot, dot);
    }

    private sealed class MenuColours : ProfessionalColorTable
    {
        public MenuColours() => UseSystemColors = false;

        public override Color ToolStripDropDownBackground => Theme.Surface;

        public override Color MenuItemSelected => Theme.AccentSoft;

        public override Color MenuItemSelectedGradientBegin => Theme.AccentSoft;

        public override Color MenuItemSelectedGradientEnd => Theme.AccentSoft;

        public override Color MenuItemBorder => Theme.AccentSoft;

        public override Color MenuBorder => Theme.Border;

        public override Color ImageMarginGradientBegin => Theme.Surface;

        public override Color ImageMarginGradientMiddle => Theme.Surface;

        public override Color ImageMarginGradientEnd => Theme.Surface;

        public override Color SeparatorDark => Theme.Border;

        public override Color SeparatorLight => Theme.Border;
    }
}
