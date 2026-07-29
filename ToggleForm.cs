using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// The whole user interface: a large button that switches the lighting, and below it a
/// drop down with the available effects plus the button that applies the selected one.
/// </summary>
internal sealed class ToggleForm : Form
{
    private readonly Button _toggle;
    private readonly ComboBox _presets;
    private readonly Button _apply;
    private bool _on;

    public ToggleForm()
    {
        Text = Strings.WindowTitle;
        ClientSize = new Size(320, 190);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(16);

        _toggle = new Button
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            AccessibleName = Strings.ButtonAccessibleName,
        };
        _toggle.FlatAppearance.BorderSize = 0;
        _toggle.Click += OnToggleClick;

        _presets = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = Strings.PresetAccessibleName,
        };
        _presets.Items.AddRange(AuraPresets.All.ToArray());

        _apply = new Button
        {
            Dock = DockStyle.Fill,
            Text = Strings.ButtonSet,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 0, 10, 0),
        };
        _apply.Click += OnApplyClick;

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 12, 0, 0),
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_presets, 0, 0);
        bottom.Controls.Add(_apply, 1, 0);
        _presets.Margin = new Padding(0, 1, 8, 1);
        _apply.Margin = new Padding(0);
        _apply.MinimumSize = new Size(0, _presets.PreferredHeight + 2);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_toggle, 0, 0);
        layout.Controls.Add(bottom, 0, 1);
        Controls.Add(layout);

        AuraState state = AuraState.Load();
        _on = state.On;
        _presets.SelectedItem = AuraPresets.ByMode(state.Mode) ?? AuraPresets.All[0];
        Render();
    }

    private void OnToggleClick(object? sender, EventArgs e) => Guarded(() =>
    {
        _on = Program.Switch(!_on).On;
    });

    private void OnApplyClick(object? sender, EventArgs e) => Guarded(() =>
    {
        if (_presets.SelectedItem is AuraPreset preset)
        {
            _on = Program.ApplyPreset(preset).On;
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
        _toggle.Text = _on ? Strings.ButtonStateOn : Strings.ButtonStateOff;
        _toggle.BackColor = _on ? Color.FromArgb(0, 132, 80) : Color.FromArgb(70, 70, 74);
    }
}
