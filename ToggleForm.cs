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
/// The whole user interface: a status line, a large button that switches the lighting, and a
/// drop down with the available effects plus the button that applies the selected one.
/// Switching runs off the UI thread, so the window stays responsive while it happens.
/// </summary>
internal sealed class ToggleForm : Form
{
    private readonly StatusPill _status;
    private readonly RoundedButton _toggle;
    private readonly RoundedButton _apply;
    private readonly ComboBox _presets;
    private readonly Label _effectLabel;
    private readonly Label _controllerText;

    private AuraState _state;
    private bool _busy;

    public ToggleForm()
    {
        _state = AuraState.Load();

        Text = Strings.WindowTitle;
        Icon = LoadIcon();
        ClientSize = new Size(360, 268);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9F);
        Padding = new Padding(20, 18, 20, 16);
        DoubleBuffered = true;

        _status = new StatusPill
        {
            Dock = DockStyle.Fill,
            Height = 34,
            Font = new Font(Font.FontFamily, 9.75F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 18),
        };

        _toggle = new RoundedButton
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
            Radius = 16,
            AccessibleName = Strings.ButtonAccessibleName,
            Margin = new Padding(0, 0, 0, 20),
        };
        _toggle.Click += OnToggleClick;

        _effectLabel = new Label
        {
            AutoSize = true,
            Text = Strings.LabelEffect,
            ForeColor = Theme.TextMuted,
            Font = new Font(Font.FontFamily, 8.25F, FontStyle.Bold),
            Margin = new Padding(2, 0, 0, 6),
        };

        _presets = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            DrawMode = DrawMode.OwnerDrawFixed,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            ItemHeight = 26,
            AccessibleName = Strings.PresetAccessibleName,
            Margin = new Padding(0, 0, 10, 0),
        };
        _presets.Items.AddRange(AuraPresets.All.ToArray());
        _presets.DrawItem += OnDrawPresetItem;

        _apply = new RoundedButton
        {
            Dock = DockStyle.Fill,
            Text = Strings.ButtonSet,
            Font = new Font(Font.FontFamily, 9.25F, FontStyle.Bold),
            Radius = 9,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 0, 16, 0),
            Fill = Theme.AccentSoft,
            FillHover = Theme.AccentSoftHover,
            FillPressed = Theme.AccentSoftPressed,
            Label = Theme.Accent,
            Margin = new Padding(0),
            MinimumSize = new Size(0, 32),
        };
        _apply.Click += OnApplyClick;

        _controllerText = new Label
        {
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            Font = new Font(Font.FontFamily, 8.25F),
            Margin = new Padding(2, 16, 0, 0),
        };

        var effectRow = new Layout
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        effectRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        effectRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        effectRow.Controls.Add(_presets, 0, 0);
        effectRow.Controls.Add(_apply, 1, 0);

        var layout = new Layout
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));  // status plus its margin
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // toggle
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // effect label
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // effect row
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // controller footer
        layout.Controls.Add(_status, 0, 0);
        layout.Controls.Add(_toggle, 0, 1);
        layout.Controls.Add(_effectLabel, 0, 2);
        layout.Controls.Add(effectRow, 0, 3);
        layout.Controls.Add(_controllerText, 0, 4);
        Controls.Add(layout);

        _presets.SelectedItem = AuraPresets.ByMode(_state.Mode) ?? AuraPresets.All[0];
        Render();
        Shown += OnShown;
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
        (string Firmware, int Channels)? controller = await Task.Run(AuraDevice.TryDescribe);

        if (controller == null)
        {
            _controllerText.Text = Strings.StatusControllerMissing;
            _toggle.Enabled = false;
            _apply.Enabled = false;
            _presets.Enabled = false;
            return;
        }

        _controllerText.Text = string.Format(CultureInfo.CurrentCulture, Strings.StatusController,
            controller.Value.Firmware, controller.Value.Channels);
    }

    private void OnDrawPresetItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _presets.Items[e.Index] is not AuraPreset preset)
        {
            return;
        }

        bool selected = e.State.HasFlag(DrawItemState.Selected);
        using (var background = new SolidBrush(selected ? Theme.AccentSoft : Theme.Surface))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }

        var swatch = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + ((e.Bounds.Height - 11) / 2), 15, 11);
        Theme.PaintSwatch(e.Graphics, swatch,
            Color.FromArgb(_state.Red, _state.Green, _state.Blue), !preset.UsesColour);

        var text = new Rectangle(swatch.Right + 9, e.Bounds.Y, e.Bounds.Width - swatch.Right - 9, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, preset.DisplayName, e.Font ?? Font, text, Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void OnToggleClick(object? sender, EventArgs e)
    {
        bool target = !_state.On;
        Run(() => Program.Switch(target));
    }

    private void OnApplyClick(object? sender, EventArgs e)
    {
        if (_presets.SelectedItem is AuraPreset preset)
        {
            Run(() => Program.ApplyPreset(preset));
        }
    }

    /// <summary>
    /// Runs one switching action on a worker thread. Talking to the controller takes a moment,
    /// and doing it here instead of on the UI thread keeps the window painting and responsive.
    /// </summary>
    private async void Run(Func<AuraState> action)
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
        _apply.Busy = busy;
        _presets.Enabled = !busy;
        UseWaitCursor = busy;

        if (busy)
        {
            _status.Show(Strings.StatusBusy, Theme.TextMuted, Theme.NeutralSoft, Theme.TextMuted);
        }
    }

    private void Render()
    {
        AuraPreset preset = AuraPresets.ByMode(_state.Mode) ?? AuraPresets.All[0];

        _toggle.Text = _state.On ? Strings.ButtonStateOn : Strings.ButtonStateOff;
        _toggle.Fill = _state.On ? Theme.Accent : Theme.Neutral;
        _toggle.FillHover = _state.On ? Theme.AccentHover : Theme.NeutralHover;
        _toggle.FillPressed = _state.On ? Theme.AccentPressed : Theme.NeutralPressed;
        _toggle.Invalidate();

        _status.Show(
            _state.On
                ? $"{Strings.StatusOn}  ·  {preset.DisplayName}"
                : Strings.StatusOff,
            _state.On ? Theme.Accent : Theme.TextMuted,
            _state.On ? Theme.AccentSoft : Theme.NeutralSoft,
            _state.On ? Theme.Text : Theme.TextMuted);

        _presets.Invalidate();
    }
}
