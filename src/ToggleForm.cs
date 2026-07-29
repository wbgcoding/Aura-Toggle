using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// The window: the effect list and the settings gear on top, one animated button that both
/// shows and switches the state, and colour chips for the effects that use a colour.
/// Everything that talks to the controller runs off the UI thread.
/// </summary>
internal sealed class ToggleForm : Form
{
    private readonly EffectButton _toggle = new();
    private readonly Select _effects = new();
    private readonly ColourStrip _colours = new();
    private readonly GlyphButton _gear = new();
    private readonly Layout _layout = new();
    private readonly NotifyIcon _tray = new();
    private readonly ToolStripMenuItem _trayLighting = new();

    private AuraState _state;
    private AuraSettings _settings;
    private SettingsPopup? _settingsPopup;
    private bool _busy;
    private bool _exiting;
    private int _channels;

    public ToggleForm()
    {
        _state = AuraState.Load();
        _settings = AuraSettings.Load();

        Text = Strings.WindowTitle;
        Icon = LoadIcon();
        ClientSize = new Size(344, 214);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9F);
        Padding = new Padding(16, 14, 16, 14);
        DoubleBuffered = true;

        _effects.AccessibleName = Strings.PresetAccessibleName;
        _effects.Dock = DockStyle.Fill;
        _effects.Margin = new Padding(0, 0, 10, 0);
        _effects.SetItems(AuraPresets.All.Select(preset =>
            new SelectItem(preset.Key, preset.DisplayName, preset.Mode)));
        _effects.SelectionChanged += OnEffectChosen;

        _gear.AccessibleName = Strings.SettingsAccessibleName;
        _gear.Margin = new Padding(0, 2, 0, 0);
        _gear.Click += OnSettingsClick;

        _toggle.Dock = DockStyle.Fill;
        _toggle.Font = new Font(Font.FontFamily, 16F, FontStyle.Bold);
        _toggle.AccessibleName = Strings.ButtonAccessibleName;
        _toggle.Margin = new Padding(0, 14, 0, 0);
        _toggle.Click += OnToggleClick;

        _colours.Anchor = AnchorStyles.None; // centred under the button
        _colours.Margin = new Padding(0, 14, 0, 0);
        _colours.ColourPicked += OnColourPicked;

        var top = new Layout
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(_effects, 0, 0);
        top.Controls.Add(_gear, 1, 0);

        _layout.Dock = DockStyle.Fill;
        _layout.ColumnCount = 1;
        _layout.RowCount = 3;
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // effect list and gear
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // toggle
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // colours
        _layout.Controls.Add(top, 0, 0);
        _layout.Controls.Add(_toggle, 0, 1);
        _layout.Controls.Add(_colours, 0, 2);
        Controls.Add(_layout);

        SetUpTray();
        Render();

        // The big button owns the focus, so the window does not open with a ringed drop down.
        ActiveControl = _toggle;

        Shown += OnShown;
        FormClosing += OnFormClosing;
        Resize += OnResize;
    }

    /// <summary>The application icon, embedded so the window matches the executable.</summary>
    private static Icon? LoadIcon()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AuraToggle.aura.ico");
        return stream == null ? null : new Icon(stream, SystemInformation.IconSize);
    }

    private void SetUpTray()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new TrayMenuRenderer(),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.5F),
        };

        _trayLighting.Click += OnToggleClick;
        menu.Items.Add(_trayLighting);
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

        _tray.Icon = Icon;
        _tray.Text = Strings.WindowTitle;
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    /// <summary>Talking to the controller happens after the window is up, never before.</summary>
    private async void OnShown(object? sender, EventArgs e)
    {
        if (_settings.StartMinimised)
        {
            HideToTray();
        }

        (string Firmware, int Channels)? controller = await Task.Run(AuraDevice.TryDescribe);

        if (controller == null)
        {
            Text = $"{Strings.WindowTitle} - {Strings.StatusControllerMissing}";
            _toggle.Enabled = false;
            _effects.Enabled = false;
            _colours.Enabled = false;
            return;
        }

        _channels = controller.Value.Channels;
        Text = $"{Strings.WindowTitle} - " + string.Format(CultureInfo.CurrentCulture,
            Strings.StatusChannels, _channels);

        await ApplyStartAction();
    }

    /// <summary>Puts the lighting into the state chosen in the settings, once per start.</summary>
    private async Task ApplyStartAction()
    {
        string action = _settings.StartAction;

        if (action == AuraSettings.StartActionOff)
        {
            await Run(() => Program.Switch(on: false));
        }
        else if (action != AuraSettings.StartActionNone && AuraPresets.Find(action) is AuraPreset preset)
        {
            await Run(() => Program.ApplyPreset(preset, CurrentColour));
        }
    }

    private Color CurrentColour => Color.FromArgb(_state.Red, _state.Green, _state.Blue);

    private void OnToggleClick(object? sender, EventArgs e)
    {
        bool target = !_state.On;
        _ = Run(() => Program.Switch(target));
    }

    private void OnEffectChosen(object? sender, EventArgs e)
    {
        if (_effects.Selected != null && AuraPresets.Find(_effects.Selected.Key) is AuraPreset preset)
        {
            _ = Run(() => Program.ApplyPreset(preset, CurrentColour));
        }
    }

    private void OnColourPicked(object? sender, EventArgs e)
    {
        if (AuraPresets.ByMode(_state.Mode) is AuraPreset preset && preset.UsesColour)
        {
            Color colour = _colours.Colour;
            _ = Run(() => Program.ApplyPreset(preset, colour));
        }
    }

    /// <summary>
    /// Opens the settings panel. It is not modal, so clicking anywhere else dismisses it.
    /// </summary>
    private void OnSettingsClick(object? sender, EventArgs e)
    {
        if (_settingsPopup != null)
        {
            _settingsPopup.Close();
            return;
        }

        var popup = new SettingsPopup(_settings);
        _settingsPopup = popup;

        popup.Changed += (_, settings) =>
        {
            bool languageChanged = settings.Language != _settings.Language;
            _settings = settings;
            _toggle.Animate = settings.Animate;

            if (languageChanged)
            {
                Strings.Override = settings.Language;
                RefreshTexts();
                popup.Close();
            }
        };
        popup.FormClosed += (_, _) =>
        {
            _settingsPopup = null;
            popup.Dispose();
        };

        popup.Open(_gear.PointToScreen(new Point(_gear.Width, _gear.Height + 6)), this);
    }

    private void OnResize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing && _settings.MinimiseOnClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _tray.Visible = false;
        _tray.Dispose();
    }

    protected override void WndProc(ref Message m)
    {
        // A second start of the tool posts this, instead of opening another window.
        if (m.Msg == Program.ShowWindowMessage && !IsDisposed)
        {
            RestoreFromTray();
        }

        base.WndProc(ref m);
    }

    /// <summary>Sends the window to the notification area, where the tray menu takes over.</summary>
    private void HideToTray()
    {
        _tray.Visible = true;
        _toggle.Paused = true;
        Hide();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        _tray.Visible = false;
        _toggle.Paused = false;
    }

    /// <summary>
    /// Runs one switching action on a worker thread. Talking to the controller takes a moment,
    /// and doing it here instead of on the UI thread keeps the window painting and responsive.
    /// </summary>
    private async Task Run(Func<AuraState> action)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            _state = await Task.Run(action);
        }
        catch (Exception ex) when (ex is AuraNotFoundException or IOException)
        {
            if (Visible)
            {
                MessageBox.Show(this, ex.Message, Strings.WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                _tray.ShowBalloonTip(4000, Strings.WindowTitle, ex.Message, ToolTipIcon.Warning);
            }
        }
        finally
        {
            SetBusy(false);
            Render();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _toggle.Busy = busy;
        _effects.Enabled = !busy;
        _colours.Enabled = !busy;
        UseWaitCursor = busy;
    }

    /// <summary>Re-reads every piece of text, so a language change shows up straight away.</summary>
    private void RefreshTexts()
    {
        _effects.AccessibleName = Strings.PresetAccessibleName;
        _gear.AccessibleName = Strings.SettingsAccessibleName;
        _toggle.AccessibleName = Strings.ButtonAccessibleName;

        _effects.SetItems(AuraPresets.All.Select(preset =>
            new SelectItem(preset.Key, preset.DisplayName, preset.Mode)));

        if (_tray.ContextMenuStrip is ContextMenuStrip menu)
        {
            foreach (ToolStripItem item in menu.Items)
            {
                if (item.Tag is string tag)
                {
                    item.Text = tag == "open" ? Strings.TrayOpen : Strings.TrayExit;
                }
            }
        }

        Text = _channels > 0
            ? $"{Strings.WindowTitle} - " + string.Format(CultureInfo.CurrentCulture, Strings.StatusChannels, _channels)
            : Strings.WindowTitle;

        Render();
    }

    private void Render()
    {
        AuraPreset preset = AuraPresets.ByMode(_state.Mode) ?? AuraPresets.All[0];
        Color colour = CurrentColour;

        _toggle.Text = _state.On ? Strings.ButtonStateOn : Strings.ButtonStateOff;
        _toggle.Animate = _settings.Animate;
        _toggle.Show(_state.On, _state.Mode, colour);

        _effects.Colour = colour;
        _effects.ShowSelection(preset.Key);

        _colours.Colour = colour;
        _colours.Visible = preset.UsesColour;
        _colours.Invalidate();

        _trayLighting.Text = _state.On ? Strings.ButtonStateOn : Strings.ButtonStateOff;
        _trayLighting.Checked = _state.On;
        _tray.Text = $"{Strings.WindowTitle} — {(_state.On ? preset.DisplayName : Strings.ButtonStateOff)}";

        // The window only reserves room for the chips when an effect actually uses them.
        int wanted = preset.UsesColour ? 218 : 176;
        if (ClientSize.Height != wanted)
        {
            ClientSize = new Size(ClientSize.Width, wanted);
        }
    }
}

/// <summary>Paints the tray menu in the same flat, themed style as the window.</summary>
internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    public TrayMenuRenderer() : base(new TrayColours())
    {
        RoundedEdges = true;
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
        e.Graphics.FillEllipse(brush, e.ImageRectangle.X + 5, e.ImageRectangle.Y + 5, 9, 9);
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
