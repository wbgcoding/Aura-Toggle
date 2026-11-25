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

    private void SetUpTray()
    {
        var menu = new ContextMenuStrip();
        ThemedMenu.Style(menu);

        // The whole board, never the selector's target: this entry sits next to a label that
        // reads the board state, and there is no channel selector in the notification area to
        // make a per-channel switch mean anything. Same reasoning as the hotkey.
        _trayLighting.Click += (_, _) => _ = Run(() => Program.Switch(!BoardIsOn()));
        menu.Items.Add(_trayLighting);

        _trayEffects.Text = Strings.TrayEffects;
        ThemedMenu.Style(_trayEffects.DropDown);
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
