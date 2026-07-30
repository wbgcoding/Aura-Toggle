using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// The window: the effect list, an optional per-device selector and the settings gear on top,
/// one animated button that both shows and switches the state, and colour chips for the
/// effects that use a colour. Everything that talks to the controller runs off the UI thread.
/// </summary>
internal sealed class ToggleForm : Form
{
    /// <summary>The drop down entry that opens the editor for a new custom preset.</summary>
    private const string NewPresetKey = "new-custom-preset";

    private const string CustomPrefix = "custom:";
    private const string DevicePrefix = "dev:";
    private const string ChannelPrefix = "ch:";

    /// <summary>How much room the big switch keeps for itself, whatever else the window shows.</summary>
    private const int ToggleHeight = 96;

    /// <summary>WM_SETTINGCHANGE, which is how a light or dark theme switch arrives.</summary>
    private const int SettingChange = 0x001A;

    private readonly EffectButton _toggle = new();
    private readonly Select _effects = new();
    private readonly Select _channel = new();
    private readonly ColourStrip _colours = new();
    private readonly Slider _brightness = new();
    private readonly Label _brightnessValue = new();
    private readonly Label _brightnessLabel = new();
    private readonly Layout _brightnessRow;
    private readonly Layout _topRow;
    private readonly GlyphButton _gear = new();
    private readonly Layout _layout = new();
    private readonly NotifyIcon _tray = new();
    private readonly ToolStripMenuItem _trayLighting = new();

    private AuraState _state;
    private AuraSettings _settings;
    private SettingsPopup? _settingsPopup;
    private List<AuraDeviceSummary> _devices = new();
    private bool _busy;
    private bool _exiting;
    private bool _dark = Theme.Dark;

    /// <summary>When the settings panel last closed, so the gear does not immediately reopen it.</summary>
    private long _settingsClosedAt;

    public ToggleForm()
    {
        _state = AuraState.Load();
        _settings = AuraSettings.Load();

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = Strings.WindowTitle;
        Icon = LoadIcon();
        ClientSize = new Size(380, 214);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        Padding = new Padding(16, 14, 16, 14);
        DoubleBuffered = true;

        _effects.AccessibleName = Strings.PresetAccessibleName;
        _effects.Dock = DockStyle.Fill;
        _effects.Margin = new Padding(0, 0, 8, 0);
        _effects.PopupWidth = 252; // room for a preset name next to its edit and delete buttons
        _effects.SelectionChanged += OnEffectChosen;
        _effects.ActionPicked += (_, _) => OpenPresetEditor(null);
        _effects.EditRequested += (_, item) => OpenPresetEditor(FindCustomPreset(item.Text));
        _effects.DeleteRequested += (_, item) => DeleteCustomPreset(item.Text);

        _channel.AccessibleName = Strings.ChannelAccessibleName;
        _channel.Width = 112; // "Alle Kanäle" has to fit without being cut off
        _channel.PopupWidth = 210;
        _channel.Margin = new Padding(0, 0, 8, 0);
        _channel.Visible = false; // shown once the board has more than one switchable channel
        _channel.SelectionChanged += (_, _) =>
        {
            // Which effects can be offered depends on what is selected, so the list is rebuilt.
            RefreshEffectItems();
            Render();
        };
        _channel.EditRequested += (_, item) => OpenChannelRename(item);

        _gear.AccessibleName = Strings.SettingsAccessibleName;
        _gear.Margin = new Padding(0, 2, 0, 0);
        _gear.Click += OnSettingsClick;

        _toggle.Dock = DockStyle.Fill;
        _toggle.Font = Theme.Display;
        _toggle.AccessibleName = Strings.ButtonAccessibleName;
        _toggle.Margin = new Padding(0, 14, 0, 0);
        _toggle.Click += OnToggleClick;

        _colours.Anchor = AnchorStyles.None; // centred under the button
        _colours.Margin = new Padding(0, 14, 0, 0);
        _colours.ColourPicked += OnColourPicked;

        _brightnessLabel.AutoSize = true;
        _brightnessLabel.Text = Strings.SettingBrightness;
        _brightnessLabel.ForeColor = Theme.TextMuted;
        _brightnessLabel.Margin = new Padding(2, 0, 0, 2);

        _brightnessValue.AutoSize = true;
        _brightnessValue.ForeColor = Theme.TextMuted;
        _brightnessValue.Margin = new Padding(0, 0, 2, 2);

        _brightness.Dock = DockStyle.Top;
        _brightness.Minimum = AuraState.MinBrightness;
        _brightness.Maximum = AuraState.MaxBrightness;
        _brightness.AccessibleName = Strings.SettingBrightness;
        _brightness.Margin = new Padding(0);
        _brightness.ValueChanged += (_, _) => ShowBrightnessValue();
        _brightness.ValueCommitted += OnBrightnessCommitted;

        _brightnessRow = new Layout
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 12, 0, 0),
        };
        _brightnessRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _brightnessRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _brightnessRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _brightnessRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _brightnessRow.Controls.Add(_brightnessLabel, 0, 0);
        _brightnessRow.Controls.Add(_brightnessValue, 1, 0);
        _brightnessRow.Controls.Add(_brightness, 0, 1);
        _brightnessRow.SetColumnSpan(_brightness, 2);

        _topRow = new Layout
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
        };
        _topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topRow.Controls.Add(_effects, 0, 0);
        _topRow.Controls.Add(_channel, 1, 0);
        _topRow.Controls.Add(_gear, 2, 0);

        _layout.Dock = DockStyle.Fill;
        _layout.ColumnCount = 1;
        _layout.RowCount = 4;
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // effect list, channel, gear
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // toggle
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // colours
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // brightness
        _layout.Controls.Add(_topRow, 0, 0);
        _layout.Controls.Add(_toggle, 0, 1);
        _layout.Controls.Add(_colours, 0, 2);
        _layout.Controls.Add(_brightnessRow, 0, 3);
        Controls.Add(_layout);

        RefreshEffectItems();
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
            Font = Theme.Menu,
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

    /// <summary>
    /// Every built-in effect plus every saved custom preset, in one list. Called again after
    /// the custom preset editor reports a change, so a new or deleted preset shows up at once.
    /// </summary>
    private void RefreshEffectItems()
    {
        string? selected = _effects.Selected?.Key;

        // With a single channel selected, the effects the controller generates itself are left
        // out: it runs them across all of its channels at once, so offering them here would
        // silently change the other headers too. The one already running stays listed, otherwise
        // the closed control could not show what the channel is doing.
        bool oneChannel = Target.Channel >= 0;
        byte running = Displayed.Mode;

        var effects = AuraPresets.All
            .Where(p => !oneChannel || p.PerChannel || p.Mode == running)
            .Select(p => new SelectItem(p.Key, p.DisplayName, p.Mode))
            .ToList();

        if (oneChannel && effects.Count < AuraPresets.All.Count)
        {
            effects.Add(new SelectItem("channel-effect-hint", Strings.ChannelEffectHint, null, IsHint: true));
        }

        IEnumerable<SelectItem> custom = AuraCustomPresets.Load().Select(p => new SelectItem(
            CustomKey(p.Name), p.Name, null,
            p.Entries.Select(e => Color.FromArgb(e.Red, e.Green, e.Blue)).ToArray(),
            Editable: true));

        // The last row creates a preset, so the feature lives where the presets themselves are
        // rather than behind the gear.
        var create = new SelectItem(NewPresetKey, Strings.ButtonNewCustomPreset, null, IsAction: true);

        _effects.SetItems(effects.Concat(custom).Append(create));

        if (selected != null)
        {
            _effects.ShowSelection(selected);
        }

        FitEffectList();
    }

    private static string CustomKey(string name) => CustomPrefix + name;

    private static CustomPreset? FindCustomPreset(string name) =>
        AuraCustomPresets.Load().Find(p => p.Name == name);

    /// <summary>Opens the editor for a new preset, or for the one the pencil was clicked on.</summary>
    private void OpenPresetEditor(CustomPreset? preset)
    {
        var editor = new CustomPresetEditor(preset, _devices, _state);
        editor.PresetsChanged += (_, _) =>
        {
            RefreshEffectItems();
            Render();
        };
        editor.FormClosed += (_, _) => editor.Dispose();

        editor.Open(new Point(Right + 8, Top), this);
    }

    /// <summary>
    /// Deletes a preset the drop down asked to remove. The list there has already made the
    /// user confirm it.
    /// </summary>
    private void DeleteCustomPreset(string name)
    {
        List<CustomPreset> presets = AuraCustomPresets.Load();
        if (presets.RemoveAll(p => p.Name == name) == 0)
        {
            return;
        }

        AuraCustomPresets.Save(presets);

        // The lighting keeps running, but it is no longer a named preset.
        if (_state.CustomPreset == name)
        {
            _state = _state with { CustomPreset = "" };
            _state.Save();
        }

        RefreshEffectItems();
        Render();
    }

    /// <summary>Talking to the controller happens after the window is up, never before.</summary>
    private async void OnShown(object? sender, EventArgs e)
    {
        // Starting the tool by hand always shows the window; only the Run key entry may
        // open straight into the notification area.
        if (_settings.StartMinimised && Program.LaunchedAtStartup)
        {
            HideToTray();
        }

        // Locked while the first discovery runs. Two discoveries at once fight over the
        // controller's answers - each one's handshake times out waiting for the other's reply -
        // and the loser concludes there is no controller at all.
        SetBusy(true);
        _devices = await Task.Run(AuraDevice.ListDevices);
        SetBusy(false);

        if (_devices.Count == 0)
        {
            Text = $"{Strings.WindowTitle} - {Strings.StatusControllerMissing}";
            _toggle.Enabled = false;
            _effects.Enabled = false;
            _channel.Enabled = false;
            _colours.Enabled = false;
            _brightness.Enabled = false;

            // The tray entry has to go grey too, or it stays clickable and every click just
            // raises the same balloon about there being no controller.
            _trayLighting.Enabled = false;
            return;
        }

        SetUpChannelSelector();
        UpdateTitle();

        await ApplyStartAction();
    }

    /// <summary>
    /// Every switchable target: all channels together, then - on machines with more than one
    /// controller - each controller as a whole, then every single channel. One controller with
    /// four channels is exactly the case the old per-controller list had nothing to offer.
    /// </summary>
    private void SetUpChannelSelector()
    {
        string? selected = _channel.Selected?.Key;
        bool several = _devices.Count > 1;

        var items = new List<SelectItem> { new(AuraSettings.ChannelAll, Strings.ChannelAll, null) };

        if (several)
        {
            items.AddRange(_devices.Select(d => new SelectItem(DevicePrefix + d.Key, d.Name, null)));
        }

        // Read once for the whole list rather than per channel.
        Dictionary<string, string> chosen = AuraChannelNames.All();

        foreach (AuraDeviceSummary device in _devices)
        {
            items.AddRange(device.Channels.Select(channel => new SelectItem(
                $"{ChannelPrefix}{device.Key}|{channel.Index}",
                ChannelLabels.For(device, channel, several, chosen),
                null, Renamable: true)));
        }

        _channel.SetItems(items);
        _channel.ShowSelection(selected ?? AuraSettings.ChannelAll);
        _channel.Visible = items.Count > 2;

        // Measured from the actual labels: channels can be renamed to anything, and on a
        // multi-controller board the name is prefixed too, so a fixed width would clip them.
        if (_channel.Visible)
        {
            // Kept as narrow as its own longest label: the effect list takes the rest of the row,
            // and that is where a long preset name would otherwise be cut off. The list it opens
            // stays wider than the button, since it has room to spare.
            _channel.Width = Math.Clamp(_channel.PreferredWidthForItems(withIcon: false) + 6, 92, 190);
            _channel.PopupWidth = Math.Max(_channel.Width, 200);
        }

        FitEffectList();
    }

    /// <summary>
    /// The effect list takes whatever the row has left. Its own entries are translated, so the
    /// drop down is opened at least as wide as the longest of them even when the closed control
    /// has to be narrower.
    /// </summary>
    private void FitEffectList() =>
        _effects.PopupWidth = Math.Clamp(_effects.PreferredWidthForItems() + 52, 252, 340);

    /// <summary>Splits a "ch:&lt;deviceKey&gt;|&lt;index&gt;" key back into its two parts.</summary>
    private static (string DeviceKey, int Channel)? ParseChannelKey(string key)
    {
        if (!key.StartsWith(ChannelPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string rest = key[ChannelPrefix.Length..];
        int separator = rest.LastIndexOf('|');
        if (separator > 0 && int.TryParse(rest[(separator + 1)..],
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            return (rest[..separator], index);
        }

        return null;
    }

    /// <summary>
    /// What the next switch applies to: no key means every controller, a channel of -1 means
    /// every channel of the one named.
    /// </summary>
    private (string? DeviceKey, int Channel) Target
    {
        get
        {
            if (!_channel.Visible || _channel.Selected?.Key is not string key || key == AuraSettings.ChannelAll)
            {
                return (null, -1);
            }

            if (key.StartsWith(DevicePrefix, StringComparison.Ordinal))
            {
                return (key[DevicePrefix.Length..], -1);
            }

            return ParseChannelKey(key) is (string deviceKey, int channel) ? (deviceKey, channel) : (null, -1);
        }
    }

    /// <summary>Opens the small popup that gives one channel a name of its own.</summary>
    private void OpenChannelRename(SelectItem item)
    {
        if (ParseChannelKey(item.Key) is not (string deviceKey, int channelIndex))
        {
            return;
        }

        var popup = new RenamePopup(item.Text);
        popup.Renamed += (_, name) =>
        {
            AuraChannelNames.Set(deviceKey, channelIndex, name);
            SetUpChannelSelector();
        };
        popup.FormClosed += (_, _) => popup.Dispose();
        popup.Open(_channel.PointToScreen(new Point(0, _channel.Height + 4)), this);
    }

    private void UpdateTitle()
    {
        int channels = _devices.Sum(d => d.Channels.Count);
        Text = $"{Strings.WindowTitle} - " + string.Format(CultureInfo.CurrentCulture, Strings.StatusChannels, channels);
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

    /// <summary>
    /// The effect, colour, power and brightness the window is showing. With a single channel or a
    /// single controller selected that is what was last set for it, so switching the selector
    /// brings its own look back; with "all channels" selected it is the global one the toggle and
    /// the command line share.
    /// </summary>
    private (byte Mode, Color Colour, bool On, byte Brightness) Displayed
    {
        get
        {
            (string? deviceKey, int channel) = Target;
            if (deviceKey == null)
            {
                return (_state.Mode, CurrentColour, _state.On, _state.Brightness);
            }

            Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();

            if (channel >= 0)
            {
                return AuraChannelStates.Get(remembered, deviceKey, channel) is ChannelLighting one
                    ? Look(one)
                    : (_state.Mode, CurrentColour, _state.On, _state.Brightness);
            }

            // A whole controller counts as on while any of its channels is, and shows the look of
            // the first channel that has one of its own.
            ChannelLighting? first = null;
            var anyOn = false;

            foreach (AuraChannel own in _devices.Find(d => d.Key == deviceKey)?.Channels ?? new List<AuraChannel>())
            {
                if (AuraChannelStates.Get(remembered, deviceKey, own.Index) is not ChannelLighting look)
                {
                    // Never set on its own, so it is following the board - which is lit.
                    anyOn = true;
                    continue;
                }

                first ??= look;
                anyOn |= look.On;
            }

            return first == null
                ? (_state.Mode, CurrentColour, _state.On, _state.Brightness)
                : Look(first with { On = anyOn });
        }
    }

    /// <summary>One channel's record as the window shows it, brightness resolved.</summary>
    private (byte Mode, Color Colour, bool On, byte Brightness) Look(ChannelLighting look) => (
        look.Mode,
        Color.FromArgb(look.Red, look.Green, look.Blue),
        look.On,
        look.Brightness == 0 ? _state.Brightness : look.Brightness);

    private void OnToggleClick(object? sender, EventArgs e)
    {
        // Switches whatever the button is showing, which for a single channel is that channel
        // rather than the whole board.
        bool target = !Displayed.On;
        (string? deviceKey, int channel) = Target;
        _ = Run(() => Program.Switch(target, deviceKey, channel));
    }

    private void OnEffectChosen(object? sender, EventArgs e)
    {
        if (_effects.Selected == null)
        {
            return;
        }

        if (_effects.Selected.Key.StartsWith(CustomPrefix, StringComparison.Ordinal))
        {
            // A custom preset names its own channels, so the selector does not apply to it.
            if (FindCustomPreset(_effects.Selected.Text) is CustomPreset preset)
            {
                _ = Run(() => Program.ApplyCustomPreset(preset));
            }

            return;
        }

        if (AuraPresets.Find(_effects.Selected.Key) is AuraPreset built)
        {
            // The colour that goes with it is the one on show, which for a single channel is
            // that channel's own rather than whatever was last set board-wide.
            Color colour = Displayed.Colour;
            (string? deviceKey, int channel) = Target;
            _ = Run(() => Program.ApplyPreset(built, colour, deviceKey, channel));
        }
    }

    private void OnColourPicked(object? sender, EventArgs e)
    {
        if (AuraPresets.ByMode(Displayed.Mode) is AuraPreset preset && preset.UsesColour)
        {
            Color colour = _colours.Colour;
            (string? deviceKey, int channel) = Target;
            _ = Run(() => Program.ApplyPreset(preset, colour, deviceKey, channel));
        }
    }

    private void ShowBrightnessValue() => _brightnessValue.Text =
        string.Format(CultureInfo.CurrentCulture, Strings.BrightnessValue, _brightness.Value);

    /// <summary>
    /// Sent once the knob is released, for whatever the selector points at - one channel, one
    /// controller or the whole board, which is also what hands single channels back to the
    /// board-wide brightness.
    /// </summary>
    private void OnBrightnessCommitted(object? sender, EventArgs e)
    {
        var percent = (byte)_brightness.Value;
        (string? deviceKey, int channel) = Target;
        _ = Run(() => Program.SetBrightness(percent, deviceKey, channel));
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

        // Pressing the gear while the panel is open deactivates it, so it has already closed and
        // cleared itself by the time this click arrives - and the guard above would reopen it.
        // A click that lands right after that close is the second half of the same gesture.
        if (Environment.TickCount64 - _settingsClosedAt < 250)
        {
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
            }
        };
        popup.FormClosed += (_, _) =>
        {
            _settingsPopup = null;
            _settingsClosedAt = Environment.TickCount64;
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

        // Windows announces a light or dark switch by broadcasting this to every window. The work
        // is deferred to the message loop rather than done here, so it happens after WinForms has
        // finished reacting to the same broadcast.
        if (m.Msg == SettingChange && !IsDisposed &&
            Marshal.PtrToStringUni(m.LParam) == "ImmersiveColorSet")
        {
            BeginInvoke(FollowSystemTheme);
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// Re-colours the window, its panels and the notification area menu after Windows switched
    /// theme, instead of leaving it half dark until the next start.
    /// </summary>
    private void FollowSystemTheme()
    {
        Theme.Forget();
        if (_dark == Theme.Dark)
        {
            // Something else about the colour scheme changed - the accent colour, say.
            return;
        }

        _dark = Theme.Dark;
        Theme.Retint(this);

        if (_tray.ContextMenuStrip is ContextMenuStrip menu)
        {
            // Not part of the window's own control tree, and it paints from a colour table that is
            // read per item, so it only needs its own two colours moved over.
            menu.BackColor = Theme.Surface;
            menu.ForeColor = Theme.Text;
        }

        Render();
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
        _channel.Enabled = !busy;
        _colours.Enabled = !busy;
        _brightness.Enabled = !busy;

        // Requests that arrive while a switch is in flight are dropped, so the tray entry says
        // so instead of looking like it did nothing.
        _trayLighting.Enabled = !busy && _devices.Count > 0;
        UseWaitCursor = busy;
    }

    /// <summary>Re-reads every piece of text, so a language change shows up straight away.</summary>
    private void RefreshTexts()
    {
        _effects.AccessibleName = Strings.PresetAccessibleName;
        _channel.AccessibleName = Strings.ChannelAccessibleName;
        _gear.AccessibleName = Strings.SettingsAccessibleName;
        _toggle.AccessibleName = Strings.ButtonAccessibleName;
        _brightnessLabel.Text = Strings.SettingBrightness;
        _brightness.AccessibleName = Strings.SettingBrightness;

        RefreshEffectItems();
        if (_devices.Count > 0)
        {
            SetUpChannelSelector();
            UpdateTitle();
        }

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

        Render();
    }

    private void Render()
    {
        (byte mode, Color colour, bool on, byte brightness) = Displayed;
        bool wholeBoard = Target.DeviceKey == null;

        string effectKey;
        string displayName;
        bool usesColour;

        // A custom preset is a bundle across channels, so it only names itself while the
        // selector is on "all channels"; with one channel picked, that channel's effect is
        // the honest thing to show.
        if (wholeBoard && _state.CustomPreset.Length > 0)
        {
            effectKey = CustomKey(_state.CustomPreset);
            displayName = _state.CustomPreset;
            usesColour = false;
        }
        else
        {
            AuraPreset preset = AuraPresets.ByMode(mode) ?? AuraPresets.All[0];
            effectKey = preset.Key;
            displayName = preset.DisplayName;
            usesColour = preset.UsesColour;
        }

        (byte red, byte green, byte blue) = AuraState.Dim(colour.R, colour.G, colour.B, brightness);

        _toggle.Text = on ? Strings.ButtonStateOn : Strings.ButtonStateOff;
        _toggle.Animate = _settings.Animate;

        // The button previews what the lighting actually looks like, brightness included; the
        // chips and the list icons keep showing the pure colour, because that is the choice.
        _toggle.Show(on, mode, Color.FromArgb(red, green, blue));

        _effects.Colour = colour;
        _effects.ShowSelection(effectKey);

        _colours.Colour = colour;
        _colours.Visible = usesColour;
        _colours.Invalidate();

        _brightness.Value = brightness;
        ShowBrightnessValue();
        _brightnessRow.Visible = usesColour;

        _trayLighting.Text = _state.On ? Strings.ButtonStateOn : Strings.ButtonStateOff;
        _trayLighting.Checked = _state.On;
        _tray.Text = $"{Strings.WindowTitle} - {(_state.On ? displayName : Strings.ButtonStateOff)}";

        ResizeToContent(usesColour);
    }

    /// <summary>
    /// Room for the chips and the brightness slider is only reserved for the effects that carry
    /// a colour - the firmware colours the rest itself, so neither control would do anything
    /// there. The height is measured rather than hardcoded: at a larger display scale, or in
    /// German, the rows grow and a fixed number would eat into the button until it collapsed.
    /// </summary>
    private void ResizeToContent(bool usesColour)
    {
        int wanted = Padding.Vertical
            + _topRow.PreferredSize.Height
            + _toggle.Margin.Vertical + ToggleHeight;

        if (usesColour)
        {
            wanted += _colours.Height + _colours.Margin.Vertical;
            wanted += _brightnessRow.PreferredSize.Height + _brightnessRow.Margin.Vertical;
        }

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
