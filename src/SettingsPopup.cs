using System;
using System.Collections.Generic;
using System.Drawing;
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
    private readonly Label _startActionLabel;
    private readonly Label _languageLabel;
    private readonly PillButton _newPreset = new();
    private readonly Layout _layout;

    private bool _childOpen;

    public SettingsPopup(AuraSettings settings)
    {
        Settings = settings;

        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9F);
        DoubleBuffered = true;
        Padding = new Padding(14, 12, 14, 12);

        _layout = new Layout
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 9,
            BackColor = Theme.Surface,
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int row = 0; row < 9; row++)
        {
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddSwitch(0, Strings.SettingAutoStart, _autoStart, AuraSettings.AutoStart);
        AddSwitch(1, Strings.SettingStartMinimised, _startMinimised, settings.StartMinimised);
        AddSwitch(2, Strings.SettingMinimiseOnClose, _minimiseOnClose, settings.MinimiseOnClose);
        AddSwitch(3, Strings.SettingAnimate, _animate, settings.Animate);

        _startActionLabel = AddLabel(4, Strings.SettingStartAction);
        AddSelect(5, _startAction, StartActions(), settings.StartAction);

        _languageLabel = AddLabel(6, Strings.SettingLanguage);
        AddSelect(7, _language, Languages(), settings.Language);

        _newPreset.Dock = DockStyle.Top;
        _newPreset.Height = 32;
        _newPreset.Text = Strings.ButtonNewCustomPreset;
        _newPreset.Margin = new Padding(0, 14, 0, 0);
        _newPreset.Click += (_, _) => OpenPresetEditor(null);
        _layout.Controls.Add(_newPreset, 0, 8);
        _layout.SetColumnSpan(_newPreset, 2);

        Controls.Add(_layout);

        _autoStart.CheckedChanged += (_, _) => AuraSettings.AutoStart = _autoStart.Checked;
        _startMinimised.CheckedChanged += (_, _) => Apply();
        _minimiseOnClose.CheckedChanged += (_, _) => Apply();
        _animate.CheckedChanged += (_, _) => Apply();

        ClientSize = new Size(276, _layout.PreferredSize.Height + Padding.Vertical);
    }

    public AuraSettings Settings { get; private set; }

    /// <summary>Raised whenever a switch is flipped, because there is no OK button.</summary>
    public event EventHandler<AuraSettings>? Changed;

    /// <summary>Raised after a custom preset was saved or deleted in the editor this opens.</summary>
    public event EventHandler? PresetsChanged;

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

    private Label AddLabel(int row, string text)
    {
        var label = new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Margin = new Padding(2, 10, 0, 4),
        };

        _layout.Controls.Add(label, 0, row);
        _layout.SetColumnSpan(label, 2);
        return label;
    }

    private void AddSelect(int row, Select select, IEnumerable<SelectItem> items, string selected)
    {
        select.Dock = DockStyle.Top;
        select.Height = 30;
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

        _layout.Controls.Add(select, 0, row);
        _layout.SetColumnSpan(select, 2);
    }

    private void AddSwitch(int row, string text, ToggleSwitch toggle, bool value)
    {
        var label = new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            Margin = new Padding(2, 5, 0, 5),
        };

        toggle.Checked = value;
        toggle.BackColor = Theme.Surface;
        toggle.Margin = new Padding(10, 2, 0, 2);

        _layout.Controls.Add(label, 0, row);
        _layout.Controls.Add(toggle, 1, row);
    }

    private void Apply()
    {
        bool languageChanged = Settings.Language != (_language.Selected?.Key ?? AuraSettings.LanguageAuto);

        Settings = Settings with
        {
            StartMinimised = _startMinimised.Checked,
            MinimiseOnClose = _minimiseOnClose.Checked,
            Animate = _animate.Checked,
            StartAction = _startAction.Selected?.Key ?? AuraSettings.StartActionNone,
            Language = _language.Selected?.Key ?? AuraSettings.LanguageAuto,
        };
        Settings.Save();

        if (languageChanged)
        {
            // Relocalises this panel in place - closing it here would undo exactly the click
            // that just chose the new language.
            Strings.Override = Settings.Language;
            RefreshLanguage();
        }

        Changed?.Invoke(this, Settings);
    }

    private void RefreshLanguage()
    {
        _startActionLabel.Text = Strings.SettingStartAction;
        _languageLabel.Text = Strings.SettingLanguage;
        _newPreset.Text = Strings.ButtonNewCustomPreset;

        string selectedAction = _startAction.Selected?.Key ?? AuraSettings.StartActionNone;
        _startAction.SetItems(StartActions());
        _startAction.ShowSelection(selectedAction);

        string selectedLanguage = _language.Selected?.Key ?? AuraSettings.LanguageAuto;
        _language.SetItems(Languages());
        _language.ShowSelection(selectedLanguage);
    }

    private void OpenPresetEditor(CustomPreset? preset)
    {
        var editor = new CustomPresetEditor(preset);
        _childOpen = true;
        editor.PresetsChanged += (_, _) => PresetsChanged?.Invoke(this, EventArgs.Empty);
        editor.FormClosed += (_, _) =>
        {
            _childOpen = false;
            editor.Dispose();
            Activate();
        };

        editor.Open(PointToScreen(new Point(Width + 8, 0)), this);
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

        // A child popup (an effect list, the preset editor) is a window of its own; opening
        // one must not dismiss this panel.
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
}
