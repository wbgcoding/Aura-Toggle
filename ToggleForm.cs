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
/// The window: one animated button that both shows and switches the state, the effect list
/// below it, and colour chips for the effects that use a colour. Everything that talks to the
/// controller runs off the UI thread.
/// </summary>
internal sealed class ToggleForm : Form
{
    private readonly EffectButton _toggle = new();
    private readonly Select _effects = new();
    private readonly ColourStrip _colours = new();
    private readonly GlyphButton _gear = new();
    private readonly Layout _layout = new();

    private AuraState _state;
    private AuraSettings _settings;
    private bool _busy;

    public ToggleForm()
    {
        _state = AuraState.Load();
        _settings = AuraSettings.Load();

        Text = Strings.WindowTitle;
        Icon = LoadIcon();
        ClientSize = new Size(324, 232);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9F);
        Padding = new Padding(16, 10, 16, 14);
        DoubleBuffered = true;

        _gear.AccessibleName = Strings.SettingsAccessibleName;
        _gear.Anchor = AnchorStyles.Right;
        _gear.Margin = new Padding(0, 0, 0, 6);
        _gear.Click += OnSettingsClick;

        _toggle.Dock = DockStyle.Fill;
        _toggle.Font = new Font(Font.FontFamily, 16F, FontStyle.Bold);
        _toggle.AccessibleName = Strings.ButtonAccessibleName;
        _toggle.Margin = new Padding(0, 0, 0, 14);
        _toggle.Click += OnToggleClick;

        _effects.Dock = DockStyle.Fill;
        _effects.AccessibleName = Strings.PresetAccessibleName;
        _effects.Margin = new Padding(0);
        _effects.SetItems(AuraPresets.All.Select(preset =>
            new SelectItem(preset.Key, preset.DisplayName, preset.Mode)));
        _effects.SelectionChanged += OnEffectChosen;

        _colours.Anchor = AnchorStyles.Left;
        _colours.Margin = new Padding(1, 12, 0, 0);
        _colours.ColourPicked += OnColourPicked;

        _layout.Dock = DockStyle.Fill;
        _layout.ColumnCount = 1;
        _layout.RowCount = 4;
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // gear
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // toggle
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // effect list
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // colours
        _layout.Controls.Add(_gear, 0, 0);
        _layout.Controls.Add(_toggle, 0, 1);
        _layout.Controls.Add(_effects, 0, 2);
        _layout.Controls.Add(_colours, 0, 3);
        Controls.Add(_layout);

        Render();
        Shown += OnShown;
        FormClosing += OnFormClosing;
        Resize += (_, _) => _toggle.Paused = WindowState == FormWindowState.Minimized;
    }

    /// <summary>The application icon, embedded so the window matches the executable.</summary>
    private static Icon? LoadIcon()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AuraToggle.aura.ico");
        return stream == null ? null : new Icon(stream, SystemInformation.IconSize);
    }

    /// <summary>Talking to the controller happens after the window is up, never before.</summary>
    private async void OnShown(object? sender, EventArgs e)
    {
        if (_settings.StartMinimised)
        {
            WindowState = FormWindowState.Minimized;
        }

        (string Firmware, int Channels)? controller = await Task.Run(AuraDevice.TryDescribe);

        if (controller == null)
        {
            Text = $"{Strings.WindowTitle}  —  {Strings.StatusControllerMissing}";
            _toggle.Enabled = false;
            _effects.Enabled = false;
            _colours.Enabled = false;
            return;
        }

        // The controller belongs in the title bar: it is reference information, not a control.
        Text = $"{Strings.WindowTitle}  —  " + string.Format(CultureInfo.CurrentCulture,
            Strings.StatusController, controller.Value.Firmware, controller.Value.Channels);

        await ApplyStartAction();
    }

    /// <summary>Puts the lighting into the state chosen in the settings, once per start.</summary>
    private async Task ApplyStartAction()
    {
        string action = _settings.StartAction;
        if (action == AuraSettings.StartActionNone)
        {
            return;
        }

        if (action == AuraSettings.StartActionOff)
        {
            await Run(() => Program.Switch(on: false));
            return;
        }

        if (AuraPresets.Find(action) is AuraPreset preset)
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

    private void OnSettingsClick(object? sender, EventArgs e)
    {
        using var popup = new SettingsPopup(_settings);
        _settings = popup.Open(_gear.PointToScreen(new Point(_gear.Width, _gear.Height + 4)));
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && _settings.MinimiseOnClose)
        {
            e.Cancel = true;
            WindowState = FormWindowState.Minimized;
        }
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
            MessageBox.Show(this, ex.Message, Strings.WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    private void Render()
    {
        AuraPreset preset = AuraPresets.ByMode(_state.Mode) ?? AuraPresets.All[0];
        Color colour = CurrentColour;

        _toggle.Text = _state.On ? Strings.ButtonStateOn : Strings.ButtonStateOff;
        _toggle.Show(_state.On, _state.Mode, colour);

        _effects.Colour = colour;
        _effects.ShowSelection(preset.Key);

        _colours.Colour = colour;
        _colours.Visible = preset.UsesColour;
        _colours.Invalidate();

        // The window only reserves room for the chips when an effect actually uses them.
        int wanted = preset.UsesColour ? 234 : 200;
        if (ClientSize.Height != wanted)
        {
            ClientSize = new Size(ClientSize.Width, wanted);
        }
    }
}
