using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// The small panel behind the gear. Every switch applies straight away - there is no OK
/// button, because there is nothing here worth confirming.
/// </summary>
internal sealed class SettingsPopup : PopupForm
{
    private readonly ToggleSwitch _minimiseOnClose = new();
    private readonly ToggleSwitch _alwaysOnTop = new();
    private readonly ToggleSwitch _animate = new();
    private readonly ToggleSwitch _hotkeyEnabled = new();
    private readonly PillButton _hotkeyRecord = new();
    private readonly Label _hotkeyHint;
    private readonly ToggleSwitch _autoStart = new();
    private readonly Select _startAction = new();
    private readonly Select _language = new();
    private readonly PillButton _reset = new();
    private readonly ArmedButton _resetArm;
    private readonly Label _minimiseOnCloseLabel;
    private readonly Label _alwaysOnTopLabel;
    private readonly Label _animateLabel;
    private readonly Label _hotkeyLabel;
    private readonly Label _autoStartLabel;
    private readonly Label _startActionLabel;
    private readonly Label _languageLabel;
    private readonly Layout _layout;

    private bool _childOpen;
    private bool _recordingHotkey;
    private int _pendingHotkey;

    /// <summary>
    /// <see cref="Environment.TickCount64"/> of the last change this panel itself applied - a
    /// switch's own <c>Changed</c> handler in <see cref="ToggleForm"/> can reassign
    /// <c>TopMost</c> or otherwise touch the main window, and Windows activates that window for
    /// some of those touches without <c>SWP_NOACTIVATE</c>. <see cref="OnDeactivate"/> treats an
    /// activation this soon after as the panel's own change stealing focus back, not the user
    /// clicking away.
    /// </summary>
    private long _appliedAt;

    private const int ReactivateWindowMs = 300;

    /// <summary>Every scaled distance this panel sets, so a display-scale change can put them all
    /// back at the new scale instead of leaving the rows measured for the old one.</summary>
    private readonly ScaledMetrics _metrics = new();

    /// <summary>
    /// Where <see cref="Open"/> was told to put the panel's right edge and top, and the monitor to
    /// clamp both against. Kept because the panel is re-fitted after it has been placed - a monitor
    /// at a different scale than the one the panel was created on hands back a different width -
    /// and every one of those re-fits has to put the right edge back on the same screen coordinate.
    /// </summary>
    private Point _anchor;

    private Rectangle _anchorScreen;

    private bool _anchored;

    /// <summary>Review mode only: the percentage of its measured width the panel is fitted to,
    /// standing in for the resize a move to a differently scaled monitor triggers.</summary>
    private int _reviewWidthPercent = 100;

    public SettingsPopup(AuraSettings settings)
    {
        Settings = settings;
        _pendingHotkey = settings.Hotkey;

        AutoScaleMode = AutoScaleMode.Dpi;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        _metrics.Add(() =>
            Padding = new Padding(this.Scaled(14), this.Scaled(12), this.Scaled(14), this.Scaled(12)));

        _layout = new Layout
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 12,
            BackColor = Theme.Surface,
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int row = 0; row < _layout.RowCount; row++)
        {
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        _minimiseOnCloseLabel = AddSwitch(0, Strings.SettingMinimiseOnClose, _minimiseOnClose, settings.MinimiseOnClose);
        _alwaysOnTopLabel = AddSwitch(1, Strings.SettingAlwaysOnTop, _alwaysOnTop, settings.AlwaysOnTop);
        _animateLabel = AddSwitch(2, Strings.SettingAnimate, _animate, settings.Animate);

        _hotkeyLabel = AddSwitch(3, Strings.SettingHotkey, _hotkeyEnabled, settings.HotkeyEnabled);
        AddButton(4, _hotkeyRecord, HotkeyText(_pendingHotkey), Theme.NeutralSoft, Theme.Text);
        _hotkeyRecord.Visible = settings.HotkeyEnabled;
        _hotkeyHint = AddLabel(5, Strings.SettingHotkeyConflict);
        _hotkeyHint.ForeColor = Theme.Danger;
        _hotkeyHint.Visible = false;

        _autoStartLabel = AddSwitch(6, Strings.SettingAutoStart, _autoStart, AuraSettings.AutoStart);
        _startActionLabel = AddLabel(7, Strings.SettingStartAction);
        AddSelect(8, _startAction, Strings.SettingStartAction, StartActions(), settings.StartAction);
        UpdateStartActionVisibility();

        _languageLabel = AddLabel(9, Strings.SettingLanguage);
        AddSelect(10, _language, Strings.SettingLanguage, Languages(), settings.Language);

        AddButton(11, _reset, Strings.SettingReset, Theme.NeutralSoft, Theme.Danger);

        Controls.Add(_layout);

        _layout.DpiChangedAfterParent += (_, _) => Resettle();
        _layout.FontChanged += (_, _) => Resettle();

        _minimiseOnClose.CheckedChanged += (_, _) => Apply();
        _alwaysOnTop.CheckedChanged += (_, _) => Apply();
        _animate.CheckedChanged += (_, _) => Apply();
        _hotkeyEnabled.CheckedChanged += (_, _) => Apply();
        _hotkeyRecord.Click += (_, _) => StartRecordingHotkey();
        _autoStart.CheckedChanged += (_, _) =>
        {
            _appliedAt = Environment.TickCount64;
            AuraSettings.AutoStart = _autoStart.Checked;
            UpdateStartActionVisibility();
            FitToContent();
        };

        _resetArm = new ArmedButton(_reset, Strings.SettingReset, Strings.SettingResetConfirm);
        _resetArm.Confirmed += (_, _) => OnResetConfirmed();

        FitToContent();
    }

    /// <summary>
    /// Width measured too, not just height: the labels are translated and do not ellipsise, so a
    /// fixed 276 clipped the longer German ones once the font grew with the display scale. Called
    /// again whenever a row is shown or hidden - the hotkey field only exists while the switch
    /// above it is on, and an empty row left the panel taller than it needed to be.
    /// </summary>
    private void FitToContent()
    {
        int width = Math.Max(this.Scaled(276), _layout.PreferredSize.Width + Padding.Horizontal);

        ClientSize = new Size(
            width * _reviewWidthPercent / 100,
            _layout.PreferredSize.Height + Padding.Vertical);

        // Every re-fit puts the right edge back where Open() asked for it, rather than leaving the
        // left one where a differently sized panel had been placed. Two things resize the panel
        // after it was placed: switching the hotkey on adds a whole row (near the bottom of the
        // screen that used to push Reset under the taskbar), and being shown on a monitor at
        // another scale than the one it was created on re-fits it at that scale. The second one is
        // what left the panel roughly centred under the gear on a second monitor - it was placed at
        // its old, narrower width and then grew rightwards past the window. Before the first Open
        // there is no anchor to hold yet, so the panel only has to stay on screen.
        if (_anchored)
        {
            PlaceAtAnchor();
        }
        else
        {
            // No horizontal margin, same reason as Open() below - re-clamping with the default
            // margin would pull the panel back off the window edge it was lined up with.
            KeepOnScreen(horizontalMargin: 0);
        }
    }

    /// <summary>
    /// The start action can only ever fire on a real Windows-startup launch, so the row is dead
    /// weight while autostart is off - same reasoning as the hotkey row only existing while its
    /// own switch is on.
    /// </summary>
    private void UpdateStartActionVisibility()
    {
        _startActionLabel.Visible = _autoStart.Checked;
        _startAction.Visible = _autoStart.Checked;
    }

    public AuraSettings Settings { get; private set; }

    /// <summary>Raised whenever a switch is flipped, because there is no OK button.</summary>
    public event EventHandler<AuraSettings>? Changed;

    /// <summary>
    /// Raised after the stored files are gone, so the window behind this panel can reload
    /// its own copies of them and repaint - resetting is not worth a restart.
    /// </summary>
    public event EventHandler? Reset;

    private static IEnumerable<SelectItem> Languages()
    {
        yield return new SelectItem(AuraSettings.LanguageAuto, Strings.LanguageAuto, null);

        // Each language named in itself, never translated: someone who opened the tool in a
        // language they cannot read has to be able to find their own in this list. The name comes
        // out of that language's own resource file, so it is the one place it can go stale.
        foreach ((string code, string _) in Strings.Offered)
        {
            yield return new SelectItem(code, Strings.InLanguage("LanguageName", code), null);
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

    private Label AddLabel(int row, string text)
    {
        var label = new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
        };

        _metrics.Add(() => label.Margin = new Padding(this.Scaled(2), this.Scaled(10), 0, this.Scaled(4)));

        _layout.Controls.Add(label, 0, row);
        _layout.SetColumnSpan(label, 2);
        return label;
    }

    private void AddSelect(int row, Select select, string name, IEnumerable<SelectItem> items, string selected)
    {
        select.Dock = DockStyle.Top;
        select.DesignHeight = 30;
        select.Margin = new Padding(0);
        select.BackColor = Theme.Surface;

        // Its label is a separate control on the row above, so the drop down has to carry the
        // text itself or a screen reader announces an unnamed combo box - same reasoning as
        // AddSwitch below.
        select.AccessibleName = name;
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

    private Label AddSwitch(int row, string text, ToggleSwitch toggle, bool value)
    {
        var label = new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
        };

        toggle.Checked = value;
        toggle.BackColor = Theme.Surface;

        _metrics.Add(() =>
        {
            label.Margin = new Padding(this.Scaled(2), this.Scaled(5), 0, this.Scaled(5));
            toggle.Margin = new Padding(this.Scaled(10), this.Scaled(2), 0, this.Scaled(2));
        });

        // The switch and its label are separate controls, so the switch has to carry the text
        // itself or a screen reader announces an unnamed checkbox.
        toggle.AccessibleName = text;

        _layout.Controls.Add(label, 0, row);
        _layout.Controls.Add(toggle, 1, row);
        return label;
    }

    private void AddButton(int row, PillButton button, string text, Color fill, Color fore)
    {
        button.Text = text;
        button.Fill = fill;
        button.ForeColor = fore;
        button.Dock = DockStyle.Top;
        button.DesignHeight = 30;
        _metrics.Add(() => button.Margin = new Padding(0, this.Scaled(8), 0, 0));

        _layout.Controls.Add(button, 0, row);
        _layout.SetColumnSpan(button, 2);
    }

    /// <summary>The arm/confirm timing and text swap live in <see cref="ArmedButton"/> now; this
    /// is just the action itself, run once the second click confirms it.</summary>
    private void OnResetConfirmed()
    {
        AuraFiles.ResetAll();

        // Not one of the stored files ResetAll clears - it lives in the registry - but it is
        // still one of the switches on this very panel, so "reset settings" leaving it on was as
        // inconsistent as leaving the language or the hotkey behind.
        AuraSettings.AutoStart = false;

        Reset?.Invoke(this, EventArgs.Empty);
        Close();
    }

    /// <summary>Puts the button into "waiting for a key" mode; the next non-modifier key with at
    /// least one modifier held becomes the new combination. Escape cancels.</summary>
    private void StartRecordingHotkey()
    {
        _appliedAt = Environment.TickCount64;
        _recordingHotkey = true;
        _hotkeyHint.Visible = false;
        _hotkeyRecord.Text = Strings.HotkeyRecordPrompt;
    }

    private void CancelRecordingHotkey()
    {
        _recordingHotkey = false;
        _hotkeyRecord.Text = HotkeyText(_pendingHotkey);
    }

    private static bool IsModifierOnly(Keys key) => HotKey.IsModifierKey((int)key);

    /// <summary>"Ctrl+Alt+L" - Win is packed and recognised like the others, but not offered by
    /// the recorder: Windows itself intercepts most Win combinations before a WinForms KeyDown
    /// ever sees them, which would make half the recorded combinations silently not work.</summary>
    private static string HotkeyText(int packed)
    {
        int modifiers = HotKey.Modifiers(packed);
        var parts = new List<string>();

        if ((modifiers & HotKey.ModControl) != 0)
        {
            parts.Add(Strings.HotkeyModifierControl);
        }

        if ((modifiers & HotKey.ModAlt) != 0)
        {
            parts.Add(Strings.HotkeyModifierAlt);
        }

        if ((modifiers & HotKey.ModShift) != 0)
        {
            parts.Add(Strings.HotkeyModifierShift);
        }

        if ((modifiers & HotKey.ModWin) != 0)
        {
            parts.Add(Strings.HotkeyModifierWin);
        }

        parts.Add(HotKey.KeyName(HotKey.VirtualKey(packed)));
        return string.Join("+", parts);
    }

    /// <summary>
    /// Called by <see cref="ToggleForm"/> when registering the new combination failed - already
    /// in use elsewhere. Switches the panel itself back off rather than pretending it took.
    /// </summary>
    public void ShowHotkeyConflict()
    {
        _hotkeyEnabled.Checked = false;
        _hotkeyRecord.Visible = false;
        _hotkeyHint.Visible = true;
        FitToContent();
    }

    private void Apply()
    {
        _appliedAt = Environment.TickCount64;
        bool languageChanged = Settings.Language != (_language.Selected?.Key ?? AuraSettings.LanguageAuto);
        _hotkeyHint.Visible = false;
        _hotkeyRecord.Visible = _hotkeyEnabled.Checked;
        FitToContent();

        // Reloaded and merged under the same lock every other settings writer uses, rather than
        // building on this popup's own possibly-stale Settings snapshot - a window-position save
        // can write independently while this popup is open, and landing between two Apply() calls
        // would otherwise be overwritten right back out (the field it just wrote reverted to
        // whatever this popup had in memory).
        using (AuraFiles.Lock())
        {
            Settings = AuraSettings.Load() with
            {
                MinimiseOnClose = _minimiseOnClose.Checked,
                Animate = _animate.Checked,
                AlwaysOnTop = _alwaysOnTop.Checked,
                StartAction = _startAction.Selected?.Key ?? AuraSettings.StartActionNone,
                Language = _language.Selected?.Key ?? AuraSettings.LanguageAuto,
                HotkeyEnabled = _hotkeyEnabled.Checked,
                Hotkey = _pendingHotkey,
            };
            Settings.Save();
        }

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
        _autoStartLabel.Text = Strings.SettingAutoStart;
        _autoStart.AccessibleName = Strings.SettingAutoStart;
        _minimiseOnCloseLabel.Text = Strings.SettingMinimiseOnClose;
        _minimiseOnClose.AccessibleName = Strings.SettingMinimiseOnClose;
        _animateLabel.Text = Strings.SettingAnimate;
        _animate.AccessibleName = Strings.SettingAnimate;
        _alwaysOnTopLabel.Text = Strings.SettingAlwaysOnTop;
        _alwaysOnTop.AccessibleName = Strings.SettingAlwaysOnTop;
        _hotkeyLabel.Text = Strings.SettingHotkey;
        _hotkeyEnabled.AccessibleName = Strings.SettingHotkey;
        _startActionLabel.Text = Strings.SettingStartAction;
        _startAction.AccessibleName = Strings.SettingStartAction;
        _languageLabel.Text = Strings.SettingLanguage;
        _language.AccessibleName = Strings.SettingLanguage;
        _hotkeyHint.Text = Strings.SettingHotkeyConflict;
        _hotkeyRecord.Text = _recordingHotkey ? Strings.HotkeyRecordPrompt : HotkeyText(_pendingHotkey);
        _resetArm.Relabel(Strings.SettingReset, Strings.SettingResetConfirm);

        string selectedAction = _startAction.Selected?.Key ?? AuraSettings.StartActionNone;
        _startAction.SetItems(StartActions());
        _startAction.ShowSelection(selectedAction);

        string selectedLanguage = _language.Selected?.Key ?? AuraSettings.LanguageAuto;
        _language.SetItems(Languages());
        _language.ShowSelection(selectedLanguage);

        FitToContent();
    }

    /// <summary>
    /// Opens the panel below the gear. It is deliberately not modal: clicking anywhere else,
    /// including the window behind it, dismisses it.
    /// </summary>
    public void Open(Point at, IWin32Window owner)
    {
        // Right edge on at.X, which already sits a few pixels inside the window's own right edge
        // (see ToggleForm.OnSettingsClick), so the panel covers the button below the gear whole and
        // keeps exactly that gap, never more: no extra horizontal screen margin on top of it.
        //
        // The screen to clamp against is resolved from the owner window itself, not from this
        // already-shifted point: on a monitor placed to the left of the one the window is really
        // on, subtracting this panel's own width from at.X can walk the point onto that other
        // monitor, and clamping against ITS bounds is what put the panel nowhere near the gear at
        // all - roughly centred under it with most of it hanging off the window, on exactly one
        // monitor in a multi-monitor layout and nowhere else, which is what made it so easy to
        // miss until someone had that layout.
        _anchor = at;
        _anchorScreen = Screen.FromHandle(owner.Handle).WorkingArea;
        _anchored = true;

        PlaceAtAnchor();
        Show(owner);
        Activate();

        // Showing it on the owner's monitor can put it on another scale than the one it was built
        // at, which re-fits it to a different width - and the placement above used the old one.
        PlaceAtAnchor();
    }

    /// <summary>
    /// Puts the panel's right edge back on the screen coordinate <see cref="Open"/> was given, at
    /// whatever width the panel currently has.
    /// </summary>
    private void PlaceAtAnchor() =>
        Place(new Point(_anchor.X - Width, _anchor.Y), horizontalMargin: 0, screen: _anchorScreen);

    /// <summary>
    /// Review mode only: re-fits the panel at <paramref name="percent"/> of its measured width, the
    /// way a move onto a monitor at another scale does - the regression proof that a resize after
    /// placement keeps the right edge against the window instead of walking the panel out from
    /// under the gear.
    /// </summary>
    internal void RefitForReview(int percent)
    {
        _reviewWidthPercent = percent;
        FitToContent();
    }

    /// <summary>
    /// Set only by <see cref="Program"/>'s review mode: with nothing else on screen to hold
    /// focus first, anything stealing it before the user gets to look (another window, another
    /// process) would otherwise close the panel before it is ever seen.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool KeepOpenOnDeactivate { private get; set; }

    /// <summary>Dragged onto a display with a different scale: the size this was fitted to
    /// belongs to the one it came from.</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Resettle();
    }

    /// <summary>
    /// Puts every row's spacing back at the current scale, then fits the panel to what that
    /// measures. Also hung on the rows' own dpi and font changes, not just this window's: WinForms
    /// reaches the children after it has told the window, and a panel fitted in between is fitted
    /// to the display it came from.
    /// </summary>
    private void Resettle()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (!IsDisposed)
            {
                _metrics.Reapply();
                FitToContent();
            }
        });
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);

        // A child popup (an effect list, the preset editor) is a window of its own; opening
        // one must not dismiss this panel.
        if (_childOpen || KeepOpenOnDeactivate)
        {
            return;
        }

        // One of this panel's own switches just touched the main window (TopMost above all) and
        // that activated it instead of the user clicking away - reclaim activation instead of
        // reading it as a dismissal. Reset itself is exempt: closing there is the action, not an
        // option, same as the comment above already treats a child popup.
        if (Environment.TickCount64 - _appliedAt < ReactivateWindowMs)
        {
            Activate();
            return;
        }

        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_recordingHotkey)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            if (e.KeyCode == Keys.Escape)
            {
                CancelRecordingHotkey();
                return;
            }

            // A hotkey needs at least one modifier - without one it would swallow ordinary
            // typing everywhere else in Windows - so a bare key press is not enough on its own,
            // and a modifier on its own just keeps waiting for the key that goes with it.
            int modifiers = (e.Control ? HotKey.ModControl : 0) | (e.Alt ? HotKey.ModAlt : 0) |
                (e.Shift ? HotKey.ModShift : 0);
            if (IsModifierOnly(e.KeyCode) || modifiers == 0)
            {
                return;
            }

            _pendingHotkey = HotKey.Pack(modifiers, (int)e.KeyCode);
            _recordingHotkey = false;
            _hotkeyRecord.Text = HotkeyText(_pendingHotkey);
            Apply();
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _resetArm.Dispose();
        }

        base.Dispose(disposing);
    }
}
