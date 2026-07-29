using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// The small panel behind the gear. Every switch applies straight away - there is no OK
/// button, because there is nothing here worth confirming.
/// </summary>
internal sealed class SettingsPopup : Form
{
    private readonly ToggleSwitch _autoStart = new();
    private readonly ToggleSwitch _startMinimised = new();
    private readonly ToggleSwitch _minimiseOnClose = new();
    private readonly Select _startAction = new();

    private bool _childOpen;

    public SettingsPopup(AuraSettings settings)
    {
        Settings = settings;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9F);
        DoubleBuffered = true;
        Padding = new Padding(14, 12, 14, 14);
        ClientSize = new Size(268, 206);

        var layout = new Layout
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            BackColor = Theme.Surface,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int row = 0; row < 5; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddSwitch(layout, 0, Strings.SettingAutoStart, _autoStart, AuraSettings.AutoStart);
        AddSwitch(layout, 1, Strings.SettingStartMinimised, _startMinimised, settings.StartMinimised);
        AddSwitch(layout, 2, Strings.SettingMinimiseOnClose, _minimiseOnClose, settings.MinimiseOnClose);

        var startLabel = new Label
        {
            AutoSize = true,
            Text = Strings.SettingStartAction,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Margin = new Padding(2, 14, 0, 6),
        };
        layout.Controls.Add(startLabel, 0, 3);
        layout.SetColumnSpan(startLabel, 2);

        _startAction.Dock = DockStyle.Fill;
        _startAction.Margin = new Padding(0);
        _startAction.BackColor = Theme.Surface;
        _startAction.SetItems(StartActions());
        _startAction.ShowSelection(settings.StartAction);
        _startAction.SelectionChanged += (_, _) => Apply();
        _startAction.PopupOpening += (_, _) => _childOpen = true;
        _startAction.PopupClosed += (_, _) =>
        {
            _childOpen = false;
            Activate();
        };
        layout.Controls.Add(_startAction, 0, 4);
        layout.SetColumnSpan(_startAction, 2);

        Controls.Add(layout);

        _autoStart.CheckedChanged += (_, _) => AuraSettings.AutoStart = _autoStart.Checked;
        _startMinimised.CheckedChanged += (_, _) => Apply();
        _minimiseOnClose.CheckedChanged += (_, _) => Apply();
    }

    public AuraSettings Settings { get; private set; }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return parameters;
        }
    }

    private static IEnumerable<SelectItem> StartActions()
    {
        yield return new SelectItem(AuraSettings.StartActionNone, Strings.StartActionNone, null);
        yield return new SelectItem(AuraSettings.StartActionOff, Strings.StartActionOff, 0);

        foreach (AuraPreset preset in AuraPresets.All)
        {
            yield return new SelectItem(preset.Key, preset.DisplayName, preset.Mode);
        }
    }

    private void AddSwitch(Layout layout, int row, string text, ToggleSwitch toggle, bool value)
    {
        var label = new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            Margin = new Padding(2, 6, 0, 6),
        };

        toggle.Checked = value;
        toggle.BackColor = Theme.Surface;
        toggle.Margin = new Padding(10, 3, 0, 3);

        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(toggle, 1, row);
    }

    private void Apply()
    {
        Settings = Settings with
        {
            StartMinimised = _startMinimised.Checked,
            MinimiseOnClose = _minimiseOnClose.Checked,
            StartAction = _startAction.Selected?.Key ?? AuraSettings.StartActionNone,
        };

        Settings.Save();
    }

    /// <summary>Opens the panel below the gear and returns once it is dismissed.</summary>
    public AuraSettings Open(Point at)
    {
        Rectangle screen = Screen.FromPoint(at).WorkingArea;
        Location = new Point(
            Math.Clamp(at.X - Width, screen.Left + 4, Math.Max(screen.Left + 4, screen.Right - Width - 4)),
            Math.Clamp(at.Y, screen.Top + 4, Math.Max(screen.Top + 4, screen.Bottom - Height - 4)));

        ShowDialog();
        return Settings;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        using GraphicsPath frame = Theme.RoundedRectangle(new RectangleF(0, 0, Width, Height), 12);
        Region = new Region(frame);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);

        // The effect list is a window of its own; opening it must not dismiss this panel.
        if (!_childOpen)
        {
            Close();
        }
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
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(Theme.Border);
        using GraphicsPath frame = Theme.RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 12);
        e.Graphics.DrawPath(border, frame);
    }
}
