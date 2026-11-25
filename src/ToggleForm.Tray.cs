using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>The notification area: its icon, its menu and the way the window goes there and back.</summary>
internal sealed partial class ToggleForm
{
    /// <summary>The application icon, embedded so the window matches the executable.</summary>
    private static Icon? LoadIcon() => LoadIcon("AuraToggle.aura.ico", SystemInformation.IconSize);

    /// <summary>The "on" icon at tray size: the large titlebar <see cref="Icon"/> reused here
    /// was only ever downscaled by Windows, which read softer than loading it small directly.</summary>
    private static Icon? LoadIconOnTray() => LoadIcon("AuraToggle.aura.ico", SystemInformation.SmallIconSize);

    /// <summary>The dimmed variant shown in the tray while the lighting is off.</summary>
    private static Icon? LoadIconOff() => LoadIcon("AuraToggle.aura-off.ico", SystemInformation.SmallIconSize);

    private static Icon? LoadIcon(string resourceName, Size size)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        return stream == null ? null : new Icon(stream, size);
    }

    /// <summary>
    /// Room above the first and below the last entry, and a little more height per row, so the
    /// rounded highlight does not sit against the menu's own rounded edge. Applied as the menu
    /// opens, which also catches the effect entries rebuilt since.
    /// </summary>
    private void SpaceTrayMenu(ToolStripDropDown menu)
    {
        // The menu's own dpi: it opens on the screen with the notification area, which need not
        // be the one the window is on.
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

    private void SetUpTray()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new TrayMenuRenderer(),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Theme.Menu,
        };

        // The whole board, never the selector's target: this entry sits next to a label that
        // reads the board state, and there is no channel selector in the notification area to
        // make a per-channel switch mean anything. Same reasoning as the hotkey.
        _trayLighting.Click += (_, _) => _ = Run(() => Program.Switch(!BoardIsOn()));
        menu.Items.Add(_trayLighting);

        _trayEffects.Text = Strings.TrayEffects;
        _trayEffects.DropDown.Renderer = menu.Renderer;
        _trayEffects.DropDown.BackColor = Theme.Surface;
        _trayEffects.DropDown.ForeColor = Theme.Text;
        _trayEffects.DropDown.Font = Theme.Menu;

        // The same compositor-drawn rounded corners and border as every popup of the window, for
        // the menu and its effect list alike; HandleCreated covers a handle WinForms recreates.
        menu.HandleCreated += (_, _) => Theme.RoundWindowCorners(menu.Handle);
        _trayEffects.DropDown.HandleCreated += (_, _) => Theme.RoundWindowCorners(_trayEffects.DropDown.Handle);
        menu.Opening += (_, _) => SpaceTrayMenu(menu);
        _trayEffects.DropDown.Opening += (_, _) => SpaceTrayMenu(_trayEffects.DropDown);
        menu.Items.Add(_trayEffects);
        RefreshTrayEffects();

        menu.Items.Add(new ToolStripSeparator());

        var open = new ToolStripMenuItem(Strings.TrayOpen) { Tag = "open" };
        open.Click += (_, _) => RestoreFromTray();
        menu.Items.Add(open);

        var exit = new ToolStripMenuItem(Strings.TrayExit) { Tag = "exit" };
        exit.Click += (_, _) =>
        {
            _exiting = true;
            Close();
        };
        menu.Items.Add(exit);

        _tray.Icon = _iconOnTray ?? Icon;
        _tray.Text = Strings.WindowTitle;
        _tray.ContextMenuStrip = menu;

        // Shown for as long as the tool runs, window open or not: one click switches the
        // lighting and the menu reaches the effects without bringing the window forward.
        _tray.Visible = true;

        // A shortcut or script may have switched the board since the menu last showed it.
        menu.Opening += (_, _) => RefreshFromDisk();

        _trayClickTimer.Tick += (_, _) =>
        {
            _trayClickTimer.Stop();

            // With no controller a switch could only raise the same balloon again; the window
            // shows why and has the retry button.
            if (_noController)
            {
                RestoreFromTray();
                return;
            }

            _ = Run(() => Program.Switch(!BoardIsOn()));
        };

        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _trayClickTimer.Interval = SystemInformation.DoubleClickTime;
                _trayClickTimer.Start();
            }
        };

        _tray.DoubleClick += (_, _) =>
        {
            _trayClickTimer.Stop();
            RestoreFromTray();
        };
    }

    /// <summary>
    /// The tray's "Effects" submenu: the same built-in effects and custom presets, in the same
    /// order, as the window's list (without its hint and "new preset" rows). Rebuilt with that
    /// list, so a new, renamed or deleted preset and a language change reach it too.
    /// </summary>
    private void RefreshTrayEffects()
    {
        _trayEffects.Text = Strings.TrayEffects;

        foreach (ToolStripItem old in _trayEffects.DropDownItems.Cast<ToolStripItem>().ToList())
        {
            old.Dispose();
        }

        _trayEffects.DropDownItems.Clear();

        void Add(string key, string text)
        {
            var item = new ToolStripMenuItem(text) { Tag = key };
            item.Click += (_, _) => ApplyTrayEffect(key, text);
            _trayEffects.DropDownItems.Add(item);
        }

        foreach (AuraPreset preset in AuraPresets.All)
        {
            Add(preset.Key, preset.DisplayName);
        }

        foreach (CustomPreset custom in AuraCustomPresets.Load())
        {
            Add(CustomKey(custom.Name), custom.Name);
        }

        UpdateTrayEffectChecks();
    }

    /// <summary>Ticks the effect the board is running, the same one the window's list shows.</summary>
    private void UpdateTrayEffectChecks()
    {
        string active = _state.CustomPreset.Length > 0
            ? CustomKey(_state.CustomPreset)
            : (AuraPresets.ByMode(_state.Mode) ?? AuraPresets.All[0]).Key;

        foreach (ToolStripMenuItem item in _trayEffects.DropDownItems.OfType<ToolStripMenuItem>())
        {
            item.Checked = item.Tag is string key && key == active;
        }
    }

    /// <summary>
    /// Like choosing the entry in the window, but for the whole board in the board's own colour:
    /// nothing in the notification area names a channel (same reasoning as the lighting entry).
    /// </summary>
    private void ApplyTrayEffect(string key, string text) =>
        ApplyEffect(key, text, deviceKey: null, channel: -1, CurrentColour);

    /// <summary>Sends the window to the notification area, where the tray menu takes over.</summary>
    private void HideToTray()
    {
        SaveWindowPosition();
        _tray.Visible = true;
        _toggle.Paused = true;
        Hide();
    }

    private async void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        RefreshFromDisk();
        _toggle.Paused = false;

        if (_devices.Count == 0)
        {
            // The tray icon was the only thing left to click while no controller was found: a
            // reasonable moment to check again, since the user just came back to look.
            await DiscoverDevices();
        }

        // The selector and the effect list were rebuilt while the window was hidden (a rename, a
        // discovery in the tray), and nothing repainted the rest of the window from them.
        Render();
    }
}

/// <summary>Paints the tray menu in the same flat, themed style as the window.</summary>
internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    public TrayMenuRenderer() : base(new TrayColours())
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

    private sealed class TrayColours : ProfessionalColorTable
    {
        public TrayColours() => UseSystemColors = false;

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
