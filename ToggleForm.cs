using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// The whole user interface: a status card, a large button that switches the lighting, and a
/// drop down with the available effects plus the button that applies the selected one.
/// </summary>
internal sealed class ToggleForm : Form
{
    private readonly RoundedButton _toggle;
    private readonly RoundedButton _apply;
    private readonly ComboBox _presets;
    private readonly Label _statusDot;
    private readonly Label _statusText;
    private readonly Label _effectText;
    private readonly Label _controllerText;
    private readonly Swatch _swatch;

    private AuraState _state;

    public ToggleForm()
    {
        _state = AuraState.Load();

        Text = Strings.WindowTitle;
        Icon = LoadIcon();
        ClientSize = new Size(340, 262);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Padding = new Padding(16);
        Font = new Font("Segoe UI", 9F);

        _statusDot = new Label
        {
            AutoSize = true,
            Text = "●",
            Font = new Font(Font.FontFamily, 12F),
            Margin = new Padding(0, 0, 8, 0),
        };

        _statusText = new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            ForeColor = Theme.Text,
            Margin = new Padding(0, 2, 0, 0),
        };

        _swatch = new Swatch
        {
            Size = new Size(14, 14),
            Margin = new Padding(2, 4, 8, 0),
        };

        _effectText = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Text,
            Margin = new Padding(0, 3, 0, 0),
        };

        _controllerText = new Label
        {
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            Margin = new Padding(0, 8, 0, 0),
        };

        var cardContent = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(14, 12, 14, 12),
            BackColor = Color.Transparent,
        };
        cardContent.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cardContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        cardContent.Controls.Add(_statusDot, 0, 0);
        cardContent.Controls.Add(_statusText, 1, 0);
        cardContent.Controls.Add(_swatch, 0, 1);
        cardContent.Controls.Add(_effectText, 1, 1);
        cardContent.Controls.Add(_controllerText, 0, 2);
        cardContent.SetColumnSpan(_controllerText, 2);

        var card = new Card
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 14),
        };
        card.Controls.Add(cardContent);

        _toggle = new RoundedButton
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
            Radius = 14,
            AccessibleName = Strings.ButtonAccessibleName,
        };
        _toggle.Click += OnToggleClick;

        _presets = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            DrawMode = DrawMode.OwnerDrawFixed,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            ItemHeight = 24,
            AccessibleName = Strings.PresetAccessibleName,
            Margin = new Padding(0, 2, 10, 2),
        };
        _presets.Items.AddRange(AuraPresets.All.ToArray());
        _presets.DrawItem += OnDrawPresetItem;

        _apply = new RoundedButton
        {
            Dock = DockStyle.Fill,
            Text = Strings.ButtonSet,
            Radius = 8,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 0, 14, 0),
            Fill = Theme.Secondary,
            FillHover = Theme.SecondaryHover,
            FillPressed = Theme.SecondaryPressed,
            Label = Theme.Accent,
            Margin = new Padding(0),
        };
        _apply.Click += OnApplyClick;
        _apply.MinimumSize = new Size(0, 30);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 14, 0, 0),
            BackColor = Color.Transparent,
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_presets, 0, 0);
        bottom.Controls.Add(_apply, 1, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(card, 0, 0);
        layout.Controls.Add(_toggle, 0, 1);
        layout.Controls.Add(bottom, 0, 2);
        Controls.Add(layout);

        _presets.SelectedItem = AuraPresets.ByMode(_state.Mode) ?? AuraPresets.All[0];
        ShowController();
        Render();
    }

    /// <summary>The application icon, embedded so the window matches the executable.</summary>
    private static Icon? LoadIcon()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AuraToggle.aura.ico");
        return stream == null ? null : new Icon(stream, SystemInformation.IconSize);
    }

    private void ShowController()
    {
        (string Firmware, int Channels)? controller = AuraDevice.TryDescribe();

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
        if (e.Index < 0)
        {
            return;
        }

        bool selected = e.State.HasFlag(DrawItemState.Selected);
        using (var background = new SolidBrush(selected ? Theme.Accent : Theme.Surface))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }

        var text = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, _presets.Items[e.Index]?.ToString() ?? "", e.Font ?? Font, text,
            selected ? Color.White : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void OnToggleClick(object? sender, EventArgs e) => Guarded(() =>
    {
        _state = Program.Switch(!_state.On);
    });

    private void OnApplyClick(object? sender, EventArgs e) => Guarded(() =>
    {
        if (_presets.SelectedItem is AuraPreset preset)
        {
            _state = Program.ApplyPreset(preset);
        }
    });

    /// <summary>
    /// Runs one switching action. The controls stay disabled while it runs so a double click
    /// cannot race on the HID handle, and failures surface instead of being swallowed.
    /// </summary>
    private void Guarded(Action action)
    {
        _toggle.Enabled = false;
        _apply.Enabled = false;
        _presets.Enabled = false;
        try
        {
            action();
            Render();
        }
        catch (Exception ex) when (ex is AuraNotFoundException or IOException)
        {
            MessageBox.Show(this, ex.Message, Strings.WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _toggle.Enabled = true;
            _apply.Enabled = true;
            _presets.Enabled = true;
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

        _statusDot.ForeColor = _state.On ? Theme.Accent : Theme.TextMuted;
        _statusText.Text = _state.On ? Strings.StatusOn : Strings.StatusOff;
        _effectText.Text = string.Format(CultureInfo.CurrentCulture, Strings.StatusEffect, preset.DisplayName);
        _effectText.ForeColor = _state.On ? Theme.Text : Theme.TextMuted;

        _swatch.Show(
            _state.On ? Color.FromArgb(_state.Red, _state.Green, _state.Blue) : Theme.Border,
            spectrum: _state.On && !preset.UsesColour);
    }
}
