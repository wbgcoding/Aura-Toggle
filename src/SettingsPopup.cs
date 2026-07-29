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
    private readonly ToggleSwitch _animate = new();
    private readonly Select _startAction = new();
    private readonly Select _language = new();

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
        ClientSize = new Size(276, 302);

        var layout = new Layout
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            BackColor = Theme.Surface,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int row = 0; row < 8; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddSwitch(layout, 0, Strings.SettingAutoStart, _autoStart, AuraSettings.AutoStart);
        AddSwitch(layout, 1, Strings.SettingStartMinimised, _startMinimised, settings.StartMinimised);
        AddSwitch(layout, 2, Strings.SettingMinimiseOnClose, _minimiseOnClose, settings.MinimiseOnClose);
        AddSwitch(layout, 3, Strings.SettingAnimate, _animate, settings.Animate);

        AddLabel(layout, 4, Strings.SettingStartAction);
        AddSelect(layout, 5, _startAction, StartActions(), settings.StartAction);

        AddLabel(layout, 6, Strings.SettingLanguage);
        AddSelect(layout, 7, _language, Languages(), settings.Language);

        Controls.Add(layout);

        _autoStart.CheckedChanged += (_, _) => AuraSettings.AutoStart = _autoStart.Checked;
        _startMinimised.CheckedChanged += (_, _) => Apply();
        _minimiseOnClose.CheckedChanged += (_, _) => Apply();
        _animate.CheckedChanged += (_, _) => Apply();
    }

    public AuraSettings Settings { get; private set; }

    /// <summary>Raised whenever a switch is flipped, because there is no OK button.</summary>
    public event EventHandler<AuraSettings>? Changed;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return parameters;
        }
    }

    private static IEnumerable<SelectItem> Languages()
    {
        yield return new SelectItem(AuraSettings.LanguageAuto, Strings.LanguageAuto, null);
        yield return new SelectItem("en", Strings.LanguageEnglish, null);
        yield return new SelectItem("de", Strings.LanguageGerman, null);
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

    private void AddLabel(Layout layout, int row, string text)
    {
        var label = new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Margin = new Padding(2, 12, 0, 5),
        };

        layout.Controls.Add(label, 0, row);
        layout.SetColumnSpan(label, 2);
    }

    private void AddSelect(Layout layout, int row, Select select, IEnumerable<SelectItem> items, string selected)
    {
        select.Dock = DockStyle.Top;
        select.Height = 32;
        select.Margin = new Padding(0);
        select.BackColor = Theme.Surface;
        select.SetItems(items);
        select.ShowSelection(selected);
        select.SelectionChanged += (_, _) => Apply();
        select.PopupOpening += (_, _) => _childOpen = true;
        select.PopupClosed += (_, _) =>
        {
            _childOpen = false;
            Activate();
        };

        layout.Controls.Add(select, 0, row);
        layout.SetColumnSpan(select, 2);
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
            Animate = _animate.Checked,
            StartAction = _startAction.Selected?.Key ?? AuraSettings.StartActionNone,
            Language = _language.Selected?.Key ?? AuraSettings.LanguageAuto,
        };

        Settings.Save();
        Changed?.Invoke(this, Settings);
    }

    /// <summary>
    /// Opens the panel below the gear. It is deliberately not modal: clicking anywhere else,
    /// including the window behind it, dismisses it.
    /// </summary>
    public void Open(Point at, IWin32Window owner)
    {
        Rectangle screen = Screen.FromPoint(at).WorkingArea;
        Location = new Point(
            Math.Clamp(at.X - Width, screen.Left + 4, Math.Max(screen.Left + 4, screen.Right - Width - 4)),
            Math.Clamp(at.Y, screen.Top + 4, Math.Max(screen.Top + 4, screen.Bottom - Height - 4)));

        Show(owner);
        Activate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.RoundWindowCorners(Handle);
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

        Theme.Prepare(e.Graphics);
        using var border = new Pen(Theme.Border);
        using GraphicsPath frame = Theme.RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 12);
        e.Graphics.DrawPath(border, frame);
    }
}
