using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
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

    /// <summary>The per-device selector's default: every controller is switched together.</summary>
    private const string ChannelAll = "all";

    /// <summary>Key for the non-selectable hint row under the effect list (<see cref="SelectItem.IsHint"/>).</summary>
    private const string ChannelEffectHintKey = "channel-effect-hint";

    /// <summary>How much room the big switch keeps for itself, whatever else the window shows.</summary>
    private const int ToggleHeight = 96;

    /// <summary>Window width, at 96 dpi: never below the first, never above the second.</summary>
    private const int MinWidth = 380;
    private const int MaxWidth = 560;

    /// <summary>
    /// Client height the window is built with before anything has been measured, at 96 dpi.
    /// <see cref="ResizeToContent"/> replaces it with the real, scaled height while the window is
    /// still invisible.
    /// </summary>
    private const int InitialHeight = 214;

    /// <summary>
    /// Every fixed distance the window is built from, at 96 dpi. They are applied through
    /// <see cref="ApplyScaledMetrics"/> rather than written straight onto the controls, because
    /// <see cref="ContainerControl.AutoScaleDimensions"/> is deliberately never set here: with it unset
    /// WinForms' own auto-scaling does nothing at all, and everything that has to grow with the
    /// display has to say so itself. Anything left as a plain number stayed 96 dpi sized on a
    /// 150 % screen - a half-size gear next to full-size text, and window padding that did not
    /// grow with the content it was supposed to frame.
    /// </summary>
    private const int PadX = 16;
    private const int PadY = 14;
    private const int GearSize = 30;
    private const int RowGap = 8;
    private const int GearDrop = 2;
    private const int ToggleGap = 14;
    private const int ColourGap = 14;
    private const int BrightnessGap = 12;
    private const int LabelInset = 2;

    /// <summary>How far the settings panel's right edge sits inside the window's own - see
    /// <see cref="OnSettingsClick"/>.</summary>
    private const int SettingsEdgeGap = 4;

    /// <summary>WM_SETTINGCHANGE, which is how a light or dark theme switch arrives.</summary>
    private const int SettingChange = 0x001A;

    /// <summary>WM_ENTERSIZEMOVE and WM_EXITSIZEMOVE: the window is following the mouse.</summary>
    private const int EnterSizeMove = 0x0231;

    private const int ExitSizeMove = 0x0232;

    /// <summary>
    /// True between those two. <see cref="KeepOnScreen"/> checks this and does nothing while it
    /// holds, so nothing here repositions a window the user is still holding on to - but
    /// <see cref="SettleAfterDpiChange"/> does not check it any more: <c>WM_DPICHANGED</c> only
    /// arrives once Windows has already put the window on its new monitor and knows the new
    /// scale, so a re-measure at that point is not guessing between two displays, it is reading
    /// the one Windows just said this window is on.
    /// </summary>
    private bool _userMoving;

    private readonly EffectButton _toggle = new();
    private readonly Select _effects = new();
    private readonly Select _channel = new();
    private readonly ColourStrip _colours = new();
    private readonly Slider _brightness = new();
    private readonly Label _brightnessValue = new();
    private readonly Label _brightnessLabel = new();
    private readonly Layout _brightnessRow;
    private readonly Layout _topRow;
    private RowStyle _topRowHeight = null!;
    private readonly GlyphButton _gear = new();
    private readonly Layout _layout = new();
    private readonly NotifyIcon _tray = new();
    private readonly ToolStripMenuItem _trayLighting = new();

    // A single left click on the tray icon toggles the lighting, but the first click of a
    // double-click looks identical until the second one either arrives or does not - so it is
    // held here until SystemInformation.DoubleClickTime passes with no second click, and dropped
    // if DoubleClick fires first.
    private readonly System.Windows.Forms.Timer _trayClickTimer = new();

    /// <summary>Delays the visible half of <see cref="SetBusy"/> - see <see cref="BusyFlashDelayMs"/>.</summary>
    private readonly System.Windows.Forms.Timer _busyTimer = new() { Interval = BusyFlashDelayMs };

    private AuraState _state;
    private AuraSettings _settings;
    private SettingsPopup? _settingsPopup;
    private List<AuraDeviceSummary> _devices = new();
    private bool _busy;
    private bool _exiting;
    private bool _dark = Theme.Dark;
    private CancellationTokenSource? _identifyCts;
    private Task? _identifyTask;
    private Task<AuraState>? _runTask;
    private readonly Icon? _iconOff = LoadIconOff();
    private readonly Icon? _iconOnTray = LoadIconOnTray();

    /// <summary>When the settings panel last closed, so the gear does not immediately reopen it.</summary>
    private long _settingsClosedAt;

    /// <summary>Set once <see cref="Reveal"/> has actually shown the window - guards against the
    /// 500 ms backstop timer and the post-discovery call both firing.</summary>
    private bool _revealed;

    /// <summary>Safety net: forces the window visible even if a hung controller never lets
    /// <see cref="DiscoverDevices"/> return. Stopped and disposed the moment <see cref="Reveal"/>
    /// actually runs, by whichever of the two gets there first.</summary>
    private System.Windows.Forms.Timer? _revealTimer;

    /// <summary>
    /// Stands in for the real controllers when <c>-review layout</c> measures the window. Never
    /// set by the normal application, which always asks the hardware.
    /// </summary>
    internal static List<AuraDeviceSummary>? ReviewDevices { get; set; }

    public ToggleForm()
    {
        _state = AuraState.Load();
        _settings = AuraSettings.Load();

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = Strings.WindowTitle;
        Icon = LoadIcon();
        ClientSize = new Size(MinWidth, InitialHeight);   // unscaled: replaced by ResizeToContent before the window shows
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        // Invisible until Reveal() decides there is something worth showing - see OnShown.
        Opacity = 0;

        // Only trusted when the saved point still lands on a display that exists right now -
        // unplugging the second monitor a window sat on must not open it off-screen and
        // unreachable. Screen.AllScreens is queried fresh here rather than cached, since it is
        // Windows' own idea of the current monitor layout and construction only runs once anyway.
        if (_settings.WindowX is int savedX && _settings.WindowY is int savedY &&
            Screen.AllScreens.Any(display => display.WorkingArea.Contains(new Point(savedX, savedY))))
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(savedX, savedY);
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
        }

        TopMost = _settings.AlwaysOnTop;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        DoubleBuffered = true;

        _effects.AccessibleName = Strings.PresetAccessibleName;
        _effects.Dock = DockStyle.Fill;
        _effects.TakesWhatIsLeft = true;
        _effects.PopupWidth = this.Scaled(276); // room for a preset name next to its duplicate, edit and delete buttons
        _effects.SelectionChanged += OnEffectChosen;
        _effects.ActionPicked += (_, _) => OpenPresetEditor(null);
        _effects.EditRequested += (_, item) =>
        {
            // Silently reopening as a blank "new preset" would look like the requested preset had
            // vanished from under the user; skipping is at least honest about not finding it.
            if (FindCustomPreset(item.Text) is CustomPreset preset)
            {
                OpenPresetEditor(preset);
            }
        };
        _effects.DeleteRequested += (_, item) => DeleteCustomPreset(item.Text);
        _effects.DuplicateRequested += (_, item) => DuplicateCustomPreset(item.Text);

        _channel.AccessibleName = Strings.ChannelAccessibleName;
        _channel.Width = this.Scaled(112); // "Alle Kanäle" has to fit without being cut off
        _channel.PopupWidth = this.Scaled(210);
        _channel.Visible = false; // shown once the board has more than one switchable channel
        _channel.SelectionChanged += (_, _) =>
        {
            // Rebuilt so the ChannelEffectHint row follows the selection.
            RefreshEffectItems();
            Render();
        };
        _channel.EditRequested += (_, item) => OpenChannelRename(item);

        _gear.AccessibleName = Strings.SettingsAccessibleName;
        _gear.Click += OnSettingsClick;

        // A display-scale change reaches these two in their own time: WinForms updates a child's
        // own dpi and replaces its font after this window has already been told, so the widths
        // measured in between are still the old display's - and consistently so, which is why no
        // check could tell. Re-measuring when the control itself reports the change is what an
        // unrelated later action (the switch, another move) was doing by accident every time it
        // appeared to fix itself.
        foreach (Control control in new Control[] { _effects, _channel })
        {
            control.DpiChangedAfterParent += (_, _) => QueueSettle();
            control.FontChanged += (_, _) => QueueSettle();
        }

        _toggle.Dock = DockStyle.Fill;
        _toggle.Font = Theme.Display;
        _toggle.AccessibleName = Strings.ButtonAccessibleName;
        _toggle.Click += OnToggleClick;

        _colours.Anchor = AnchorStyles.None; // centred under the button
        _colours.ColourPicked += OnColourPicked;

        _brightnessLabel.AutoSize = true;
        _brightnessLabel.Text = Strings.SettingBrightness;
        _brightnessLabel.ForeColor = Theme.TextMuted;

        _brightnessValue.AutoSize = true;
        _brightnessValue.ForeColor = Theme.TextMuted;

        _brightness.Dock = DockStyle.Top;
        _brightness.Minimum = AuraState.MinBrightness;
        _brightness.Maximum = AuraState.MaxBrightness;
        _brightness.AccessibleName = Strings.SettingBrightness;
        _brightness.Margin = new Padding(0);
        _brightness.ValueChanged += (_, _) =>
        {
            ShowBrightnessValue();
            PreviewBrightness();
        };
        _brightness.ValueCommitted += OnBrightnessCommitted;

        _brightnessRow = new Layout
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
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
            // Not AutoSize: that would make the row's own preferred width - swayed by whatever
            // _effects last measured, including a stale figure surviving a display-scale change -
            // a floor Dock=Fill can grow past but never shrink below. A log from the field caught
            // exactly that: "effects w=243 preferred=209", the panel refusing to give the row back
            // to 209 even though the window was already sized for it, pushing the gear off the edge.
            // Only Height is ever read from this row (ResizeToContent), and GetPreferredSize()
            // answers that without opting the row's own width into the same floor.
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
        };
        _topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // An explicit row style, not left to default: without AutoSize on the panel itself (see
        // the comment above), an unstyled row's height stopped being reliably "as tall as its
        // tallest cell" - the effect list's row could balloon on a selection change and starve
        // the toggle button of the height _layout's own Percent row leaves for it.
        _topRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _topRow.Controls.Add(_effects, 0, 0);
        _topRow.Controls.Add(_channel, 1, 0);
        _topRow.Controls.Add(_gear, 2, 0);

        _layout.Dock = DockStyle.Fill;
        _layout.ColumnCount = 1;
        // Explicitly the full width, not the default: an unstyled column is sized to fit its
        // content, which makes whatever the top row last measured a floor the window cannot shrink
        // below. After a move to a display at another scale that floor still belonged to the old
        // one, and the row it forced open pushed the gear past the right-hand edge.
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.RowCount = 4;
        // Absolute, not AutoSize: a RowStyle.AutoSize row asks its cell's control for a preferred
        // height, but _topRow is a Dock=Fill child of this row and that combination makes the row
        // engine guess rather than measure - the row reports 34 px and is handed 100, starving the
        // toggle row below it. Told the true number instead, kept current by ResizeToContent.
        _topRowHeight = new RowStyle(SizeType.Absolute, _topRow.PreferredSize.Height);
        _layout.RowStyles.Add(_topRowHeight);                        // effect list, channel, gear
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // toggle
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // colours
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // brightness
        _layout.Controls.Add(_topRow, 0, 0);
        _layout.Controls.Add(_toggle, 0, 1);
        _layout.Controls.Add(_colours, 0, 2);
        _layout.Controls.Add(_brightnessRow, 0, 3);
        Controls.Add(_layout);

        ApplyScaledMetrics();
        RefreshEffectItems();
        SetUpTray();
        Render();

        // A first guess at the real width, so the window does not visibly grow the moment
        // DiscoverDevices() finds out how wide the channel selector actually needs it to be -
        // the reveal backstop shows the window after half a second whether discovery has answered
        // or not, and on a board that takes longer than that the growth happened in plain sight.
        // Applied after Render(), not before: the selector is still hidden this early, so the
        // width Render() works out is the one for a row without it and would overwrite the guess.
        // Never trusted outright - clamped the same as WantedWidth, in case a hand-edited file
        // carries something absurd - and the Render() after discovery still corrects it if this
        // guess turns out wrong (different monitor, renamed channel, new preset).
        if (_settings.WindowWidth is int savedWidth)
        {
            int clamped = Math.Clamp(this.Scaled(savedWidth), this.Scaled(MinWidth), this.Scaled(MaxWidth));
            ClientSize = new Size(clamped, ClientSize.Height);
        }

        // The big button owns the focus, so the window does not open with a ringed drop down.
        ActiveControl = _toggle;

        _busyTimer.Tick += OnBusyTimerTick;

        Shown += OnShown;
        FormClosing += OnFormClosing;
        Resize += OnResize;
    }

    /// <summary>The application icon, embedded so the window matches the executable.</summary>
    private static Icon? LoadIcon() => LoadIcon("AuraToggle.aura.ico", SystemInformation.IconSize);

    /// <summary>The "on" icon at tray size - the large titlebar <see cref="Icon"/> reused here
    /// was only ever downscaled by Windows, which read softer than loading it small directly.</summary>
    private static Icon? LoadIconOnTray() => LoadIcon("AuraToggle.aura.ico", SystemInformation.SmallIconSize);

    /// <summary>The dimmed variant shown in the tray while the lighting is off.</summary>
    private static Icon? LoadIconOff() => LoadIcon("AuraToggle.aura-off.ico", SystemInformation.SmallIconSize);

    private static Icon? LoadIcon(string resourceName, Size size)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        return stream == null ? null : new Icon(stream, size);
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

        // The whole board, never the selector's target: this entry sits next to a label that
        // reads the board state, and there is no channel selector in the notification area to
        // make a per-channel switch mean anything. Same reasoning as the hotkey.
        _trayLighting.Click += (_, _) => _ = Run(() => Program.Switch(!_state.On));
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

        _tray.Icon = _iconOnTray ?? Icon;
        _tray.Text = Strings.WindowTitle;
        _tray.ContextMenuStrip = menu;

        _trayClickTimer.Tick += (_, _) =>
        {
            _trayClickTimer.Stop();
            _ = Run(() => Program.Switch(!_state.On));
        };

        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _trayClickTimer.Interval = SystemInformation.DoubleClickTime;
                _trayClickTimer.Start();
            }
        };

        _tray.DoubleClick += (_, _) =>
        {
            _trayClickTimer.Stop();
            RestoreFromTray();
        };
    }

    /// <summary>
    /// Every built-in effect plus every saved custom preset, in one list. Called again after
    /// the custom preset editor reports a change, so a new or deleted preset shows up at once.
    /// </summary>
    private void RefreshEffectItems()
    {
        string? selected = _effects.Selected?.Key;

        // All effects are always offered, even with a single channel selected. The controller
        // still runs its own firmware-generated modes across every channel of that controller at
        // once, so picking one here still affects the others — the hint row below is what tells
        // the user that, not a missing list entry.
        bool oneChannel = Target.Channel >= 0;

        var effects = AuraPresets.All
            .Select(p => new SelectItem(p.Key, p.DisplayName, p.Mode, Hint: p.HintText))
            .ToList();

        if (oneChannel)
        {
            effects.Add(new SelectItem(ChannelEffectHintKey, Strings.ChannelEffectHint, null, IsHint: true));
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

        // The editor shows its rows on the real hardware while they are being put together, and
        // hands the lighting back when it closes without saving. Neither writes anything, so the
        // window's own state does not change and does not need re-rendering.
        editor.PreviewRequested += (_, shown) => QueuePreview(() => Program.Preview(shown));
        editor.PreviewEnded += (_, _) => QueuePreview(Program.RestoreFromRecords);

        // Save committed the preview to disk as a preset - applying it now is what makes the
        // hardware, state.json and channel-state.json agree on it, instead of the board showing
        // the new preset while every record still describes the old one. Queued rather than
        // dropped when busy: a Save that lands while the last live preview is still running would
        // otherwise write the preset and never apply it.
        editor.Saved += (_, saved) => _ = Run(() => Program.ApplyCustomPreset(saved), queue: true);
        editor.Deleted += (_, name) => ClearActiveCustomPreset(name);

        editor.FormClosed += (_, _) => editor.Dispose();

        editor.Open(new Point(Right + this.Scaled(8), Top), this);
    }

    /// <summary>
    /// Deletes a preset the drop down asked to remove. The list there has already made the
    /// user confirm it.
    /// </summary>
    private void DeleteCustomPreset(string name)
    {
        using (IDisposable guard = AuraFiles.Lock())
        {
            List<CustomPreset> presets = AuraCustomPresets.Load();
            if (presets.RemoveAll(p => p.Name == name) == 0)
            {
                return;
            }

            AuraCustomPresets.Save(presets);
        }

        ClearActiveCustomPreset(name);
        RefreshEffectItems();
        Render();
    }

    /// <summary>
    /// Copies a preset under "&lt;name&gt; (2)", counting up on a collision, then opens the copy
    /// in the editor straight away - the list that offered Duplicate is already closed by the
    /// time this runs, so there is nothing left to interrupt. Cancelling out of the editor leaves
    /// the copy in place; it was saved the moment it was created, same as the original.
    /// </summary>
    private void DuplicateCustomPreset(string name)
    {
        CustomPreset copy;

        using (IDisposable guard = AuraFiles.Lock())
        {
            List<CustomPreset> presets = AuraCustomPresets.Load();
            if (presets.Find(p => p.Name == name) is not CustomPreset original)
            {
                return;
            }

            var existingNames = presets.Select(p => p.Name).ToHashSet();
            string copyName;
            int suffix = 2;
            do
            {
                // A name already at AuraFiles.MaxPresetName leaves no room for the suffix -
                // AuraCustomPresets.Load re-caps every name to that length on the next read, which
                // silently dropped the suffix back off and left two presets sharing one name.
                string suffixText = $" ({suffix})";
                string baseName = name[..Math.Min(name.Length, AuraFiles.MaxPresetName - suffixText.Length)];
                copyName = $"{baseName}{suffixText}";
                suffix++;
            }
            while (existingNames.Contains(copyName));

            copy = new CustomPreset(copyName, new List<CustomPresetEntry>(original.Entries));
            presets.Add(copy);
            AuraCustomPresets.Save(presets);
        }

        RefreshEffectItems();
        OpenPresetEditor(copy);
    }

    /// <summary>
    /// Forgets a preset name from the active state if it was the one running - the lighting
    /// keeps running, but it is no longer a named preset. Shared by both places a preset can be
    /// deleted from: the effect list's own drop down, and the editor's delete button.
    /// </summary>
    private void ClearActiveCustomPreset(string name)
    {
        // Locked and re-read, like every other read-modify-write of state.json: the cached copy
        // here can be older than what a command line switch wrote a moment ago, and saving it
        // back would undo that.
        using IDisposable guard = AuraFiles.Lock();

        AuraState state = AuraState.Load();
        if (state.CustomPreset != name)
        {
            return;
        }

        _state = state with { CustomPreset = "" };
        _state.Save();
    }

    /// <summary>Talking to the controller happens after the window is up, never before.</summary>
    private async void OnShown(object? sender, EventArgs e)
    {
        // Backstop: whatever else happens, the window is visible within half a second - a hung
        // controller must never leave it sitting fully transparent.
        _revealTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _revealTimer.Tick += (_, _) => Reveal();
        _revealTimer.Start();

        // Starting the tool by hand always shows the window; only the Run key entry may
        // open straight into the notification area.
        if (Program.LaunchedAtStartup)
        {
            // Already hidden below, so there is nothing left for Opacity to hide - and
            // RestoreFromTray must never bring back a window that is still transparent.
            Opacity = 1;
            HideToTray();
        }

        ApplyHotkeySetting(popup: null);

        await DiscoverDevices();

        // A manual open must never re-apply the configured start action - only a genuine
        // Windows-logon launch may, since that is the only time LaunchedAtStartup is true.
        if (_devices.Count > 0 && Program.LaunchedAtStartup)
        {
            await ApplyStartAction();
        }

        Reveal();
    }

    /// <summary>
    /// Makes the window visible - once. Called after the first real measurement
    /// (<see cref="DiscoverDevices"/> and the <see cref="Render"/> it ends with, so the window
    /// never grows on screen) and from a 500 ms backstop timer in case a hung controller never
    /// gets that far.
    /// </summary>
    private void Reveal()
    {
        if (_revealed)
        {
            return;
        }

        _revealed = true;
        _revealTimer?.Stop();
        _revealTimer?.Dispose();
        _revealTimer = null;
        Opacity = 1;

        if (Visible)
        {
            // A fresh install hands off to this process through the installer's own de-elevated
            // relaunch, which does not carry the right to activate a window - without this the
            // very first start opened behind the installer instead of in front of it. Skipped
            // when the window is hidden to the tray (autostart): there is nothing to bring
            // forward.
            ForegroundWindow.Claim(this);
        }
    }

    /// <summary>
    /// Looks for the controller and enables or disables the window's hardware-facing controls to
    /// match. Called again from <see cref="RestoreFromTray"/> when nothing was found the first
    /// time - unplugging the tool's window into disabled controls forever is otherwise the only
    /// outcome, since nothing else ever asks the hardware again.
    /// </summary>
    private async Task DiscoverDevices()
    {
        // Waited out first, then held. Two discoveries at once fight over the controller's
        // answers - each one's handshake times out waiting for the other's reply - and the loser
        // concludes there is no controller at all. The busy flag alone does not prevent that:
        // bringing the window back from the notification area discovers again whenever nothing
        // was found yet, which is exactly the state while the first sweep is still running.
        while (_busy && !IsDisposed)
        {
            await Task.Delay(50);
        }

        if (IsDisposed)
        {
            return;
        }

        SetBusy(true);
        try
        {
            _devices = ReviewDevices ?? await Task.Run(AuraDevice.ListDevices);
        }
        finally
        {
            // ListDevices answers a missing or unreachable controller with an empty list, but a
            // denied device or a driver that faults outright still throws. Handing the flag back
            // in every case matters more than the error itself: left held, the window comes up
            // behind the error dialog with every control greyed out and no way to try again.
            if (!IsDisposed)
            {
                SetBusy(false);
            }
        }

        if (IsDisposed)
        {
            // Quit from the notification area while discovery was still running - the window and
            // its controls are already gone, and nothing here may reach the controller after
            // OnFormClosing has settled everything.
            return;
        }

        bool found = _devices.Count > 0;
        _toggle.Enabled = found;
        _effects.Enabled = found;
        _channel.Enabled = found;
        _colours.Enabled = found;
        _brightness.Enabled = found;

        // The tray entry has to go grey too, or it stays clickable and every click just raises
        // the same balloon about there being no controller.
        _trayLighting.Enabled = found;

        if (!found)
        {
            Text = $"{Strings.WindowTitle} - {Strings.StatusControllerMissing}";
            return;
        }

        SetUpChannelSelector();
        Text = Strings.WindowTitle;

        // Re-measured now that the selector is there: the width worked out in the constructor was
        // for a row without it, and the selector is the widest thing that ever joins that row.
        // Every other caller of SetUpChannelSelector already renders afterwards; this one did not,
        // which left the window at its 380 px minimum with the gear pushed off the right edge.
        Render();
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

        var items = new List<SelectItem> { new(ChannelAll, Strings.ChannelAll, null) };

        if (several)
        {
            items.AddRange(_devices.Select(d => new SelectItem(DevicePrefix + d.Key, d.Name, null)));
        }

        // Read once for the whole list rather than per channel.
        Dictionary<string, string> chosen = AuraChannelNames.All();

        foreach (AuraDeviceSummary device in _devices)
        {
            items.AddRange(device.Channels.Select(channel => new SelectItem(
                ChannelPrefix + AuraFiles.ChannelKey(device.Key, channel.Index),
                ChannelLabels.For(device, channel, several, chosen),
                null, Renamable: true)));
        }

        _channel.SetItems(items);
        _channel.ShowSelection(selected ?? ChannelAll);
        _channel.Visible = items.Count > 2;

        // Measured from the actual labels: channels can be renamed to anything, and on a
        // multi-controller board the name is prefixed too, so a fixed width would clip them.
        if (_channel.Visible)
        {
            // Kept as narrow as its own longest label: the effect list takes the rest of the row,
            // and that is where a long preset name would otherwise be cut off. The list it opens
            // stays wider than the button, since it has room to spare. The bounds are scaled
            // because what they bound is not: the measured label width grows with the display,
            // so a limit written for 96 dpi would clip every name on a 150 % screen.
            _channel.Width = Math.Clamp(_channel.PreferredWidthForItems(withIcon: false) + this.Scaled(6),
                this.Scaled(92), this.Scaled(190));
            _channel.PopupWidth = Math.Max(_channel.Width, this.Scaled(200));
        }

        FitEffectList();
    }

    /// <summary>
    /// The effect list takes whatever the row has left. Its own entries are translated, so the
    /// drop down is opened at least as wide as the longest of them even when the closed control
    /// has to be narrower.
    /// </summary>
    private void FitEffectList() =>
        _effects.PopupWidth = Math.Clamp(_effects.PreferredWidthForItems() + this.Scaled(52),
            this.Scaled(252), this.Scaled(340));

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
            if (!_channel.Visible || _channel.Selected?.Key is not string key || key == ChannelAll)
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

        // Created here rather than inside RunIdentify: that method may sit waiting for another
        // hardware call to finish before it ever reaches its own assignment, and closing the popup
        // during that wait used to cancel a field that was still null. The identify then started
        // anyway with nothing left able to stop it, parked on an infinite wait, and held the busy
        // flag - and with it every control - for the rest of the session.
        var cts = new CancellationTokenSource();

        var popup = new RenamePopup(item.Text);
        popup.Renamed += (_, name) =>
        {
            AuraChannelNames.Set(deviceKey, channelIndex, name);
            SetUpChannelSelector();
        };
        popup.FormClosed += (_, _) =>
        {
            cts.Cancel();
            popup.Dispose();
        };
        popup.Open(_channel.PointToScreen(new Point(0, _channel.Height + this.Scaled(4))), this);

        _ = RunIdentify(deviceKey, channelIndex, cts);
    }

    /// <summary>
    /// Blinks the channel being renamed so its header is easy to find, and locks the main
    /// buttons for as long as it runs - nothing else may write to the controller at the same
    /// time. Stopped by cancelling <see cref="_identifyCts"/>, which <see cref="Program.Identify"/>
    /// answers by putting every channel of that controller back to its own record before returning.
    /// </summary>
    private async Task RunIdentify(string deviceKey, int channelIndex, CancellationTokenSource cts)
    {
        // The channel selector's own popup stays open across a switch (it is a window of its
        // own, not disabled by SetBusy), so the pencil can be clicked while _busy is already
        // held elsewhere - the hotkey, the toggle button. Waiting it out here is what every other
        // hardware path already does; without it two DiscoverAll sweeps raced each other and this
        // method's own SetBusy(true) below re-enabled controls out from under a switch mid-flight.
        while (_busy && !IsDisposed)
        {
            await Task.Delay(50);
        }

        if (IsDisposed)
        {
            return;
        }

        // A rename opened while a previous one is still running (the popup closes on
        // deactivation, but its own Program.Identify keeps holding the controller for a moment
        // after Cancel() until it notices) has to wait that moment out - starting the new
        // AuraDevice.DiscoverAll straight away would race the old run's still-open handles on
        // the same controller.
        if (_identifyCts != null)
        {
            _identifyCts.Cancel();
            try
            {
                if (_identifyTask != null)
                {
                    await _identifyTask;
                }
            }
            catch (Exception ex)
            {
                // Same widened catch as the one below, and for the same reason - a refused
                // command throws InvalidOperationException, which used to fall through here
                // uncaught and take the newly requested rename down with the previous run's own
                // failure. Every awaiter of the same Task sees this independently, so it is
                // logged again even though the failing run's own RunIdentify call already logged
                // it once.
                AuraLog.Error("Identify", ex);
            }
        }

        // Awaiting the previous run above yielded the thread again, so a hotkey or a toggle click
        // may have taken the flag in the meantime. Without this second wait, the SetBusy(false)
        // in this method's finally would re-enable every control in the middle of that switch.
        while (_busy && !IsDisposed)
        {
            await Task.Delay(50);
        }

        if (IsDisposed)
        {
            cts.Dispose();
            return;
        }

        // The popup owning this request may have been dismissed while the waits above ran, in
        // which case there is nothing left to point at a header for and the controller must not
        // be touched at all.
        if (cts.IsCancellationRequested)
        {
            cts.Dispose();
            return;
        }

        // The field is only taken now, at the point this run actually owns the controller, so the
        // block above cancels the previous run rather than this one. Kept in a local too: a second
        // rename starting before this continuation resumes moves the field on, and clearing it
        // blind in the finally would disarm the newer run instead.
        _identifyCts = cts;
        Task identify = Task.Run(() => Program.Identify(deviceKey, channelIndex, cts.Token));
        _identifyTask = identify;

        SetBusy(true);
        try
        {
            await identify;
        }
        catch (Exception ex)
        {
            // Nothing awaits this task (OpenChannelRename fires it with a discarded "_ ="), so
            // an unhandled fault here would vanish as an unobserved task exception instead of
            // reaching the user or the log - the same reasoning ToggleForm.Run already follows
            // for its own fire-and-forget switches. Catching every exception rather than only the
            // two expected ones for exactly that reason: a refused command throws
            // InvalidOperationException, which used to disappear without a trace.
            AuraLog.Error("Identify", ex);
        }
        finally
        {
            if (!IsDisposed)
            {
                SetBusy(false);
            }

            if (ReferenceEquals(_identifyCts, cts))
            {
                _identifyCts = null;
            }

            cts.Dispose();
        }
    }

    /// <summary>
    /// Puts the lighting into the state chosen in the settings. Only called for a real
    /// Windows-startup launch (see <see cref="OnShown"/>), never for the user opening the window
    /// by hand later in the same session.
    /// </summary>
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

    /// <summary>
    /// <see cref="Displayed"/> as of the last <see cref="Render"/>, which every click handler
    /// reads instead of the property itself. <see cref="Displayed"/> parses
    /// <c>channel-state.json</c> from disk with a channel or controller selected, and Render
    /// already runs after every action that could have changed it - reading it again per click
    /// was a second disk read for the same answer Render had just computed.
    /// </summary>
    private (byte Mode, Color Colour, bool On, byte Brightness) _displayed;

    /// <summary>
    /// The mode and colour <see cref="Render"/> actually painted the switch with, which for a
    /// custom preset is the most-used channel's effect, not <see cref="_displayed"/>'s own mode
    /// and colour - see the custom-preset branch there. <see cref="PreviewBrightness"/> reads
    /// these instead of recomputing them, so dragging the slider previews the same thing Render
    /// last drew rather than a different, wrong answer for a custom preset.
    /// </summary>
    private byte _paintedMode;

    private Color _paintedColour;

    private void OnToggleClick(object? sender, EventArgs e)
    {
        // Switches whatever the button is showing, which for a single channel is that channel
        // rather than the whole board.
        bool target = !_displayed.On;
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
            Color colour = _displayed.Colour;
            (string? deviceKey, int channel) = Target;
            _ = Run(() => Program.ApplyPreset(built, colour, deviceKey, channel));
        }
    }

    private void OnColourPicked(object? sender, EventArgs e)
    {
        if (AuraPresets.ByMode(_displayed.Mode) is AuraPreset preset && preset.UsesColour)
        {
            Color colour = _colours.Colour;
            (string? deviceKey, int channel) = Target;
            _ = Run(() => Program.ApplyPreset(preset, colour, deviceKey, channel));
        }
    }

    private void ShowBrightnessValue() => _brightnessValue.Text =
        string.Format(CultureInfo.CurrentCulture, Strings.BrightnessValue, _brightness.Value);

    /// <summary>
    /// Live-dims the switch while the slider is being dragged, before the value is committed to
    /// the controller - <see cref="Render"/> already does the same dimming after every real
    /// change, this just previews it a knob-drag early. Skipped for a firmware effect
    /// (<see cref="AuraPreset.UsesColour"/> false): those four cannot be dimmed at all
    /// (docs/INVARIANTS.md, brightness model), and a preview that ignored that would lie. Reads
    /// only fields <see cref="Render"/> already set - no store, no measurement, so the call
    /// <see cref="Render"/> itself makes when it assigns <see cref="_brightness"/>.Value below
    /// stays harmless: it previews with the very values Render just painted.
    /// </summary>
    private void PreviewBrightness()
    {
        if (AuraPresets.ByMode(_paintedMode)?.UsesColour != true)
        {
            return;
        }

        (byte red, byte green, byte blue) =
            AuraState.Dim(_paintedColour.R, _paintedColour.G, _paintedColour.B, (byte)_brightness.Value);
        _toggle.Show(_displayed.On, _paintedMode, Color.FromArgb(red, green, blue));
    }

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
            bool hotkeyChanged = settings.HotkeyEnabled != _settings.HotkeyEnabled || settings.Hotkey != _settings.Hotkey;
            _settings = settings;
            _toggle.Animate = settings.Animate;

            // Assigning TopMost, even to the value it already holds, makes WinForms reissue
            // SetWindowPos(HWND_TOPMOST/HWND_NOTOPMOST) without SWP_NOACTIVATE - which activates
            // this window and, with it, closes the settings panel sitting on top of it. Every
            // other option's Changed handler runs whether or not that option actually changed, so
            // this guard is what keeps the panel open for all of them, not just this one.
            if (TopMost != settings.AlwaysOnTop)
            {
                TopMost = settings.AlwaysOnTop;
            }

            if (languageChanged)
            {
                Strings.Override = settings.Language;
                RefreshTexts();
            }

            if (hotkeyChanged)
            {
                ApplyHotkeySetting(popup);
            }
        };
        popup.Reset += (_, _) => ResetToDefaults();
        popup.FormClosed += (_, _) =>
        {
            _settingsPopup = null;
            _settingsClosedAt = Environment.TickCount64;
            popup.Dispose();
        };

        // Drops from under the gear, but its right edge follows the window's - a few pixels inside
        // it, not the gear's own. The big toggle button underneath runs the full content width, so
        // an edge lined up with the gear left a strip of the button showing next to the panel; from
        // the window's edge inward the panel covers it completely and still keeps a hairline of
        // window visible on the right. SettingsPopup.Open keeps this exact gap rather than adding
        // its own screen-edge margin on top of it.
        var anchor = new Point(
            this.RectangleToScreen(this.ClientRectangle).Right - this.Scaled(SettingsEdgeGap),
            _gear.PointToScreen(new Point(0, _gear.Height + this.Scaled(6))).Y);
        popup.Open(anchor, this);
    }

    /// <summary>
    /// Review mode only: opens the settings panel the same way a real gear click does, then keeps
    /// it open past the deactivation that would otherwise close it immediately - with nothing else
    /// on screen to hold focus first, anything else stealing it (another window, another process)
    /// counts as one.
    /// </summary>
    internal void OpenSettingsForReview()
    {
        OnSettingsClick(this, EventArgs.Empty);
        if (_settingsPopup != null)
        {
            _settingsPopup.KeepOpenOnDeactivate = true;
        }
    }

    /// <summary>Review mode only: see <see cref="SettingsPopup.RefitForReview"/>.</summary>
    internal void RefitSettingsForReview(int percent) => _settingsPopup?.RefitForReview(percent);

    /// <summary>
    /// Review mode only: closes the settings panel synchronously if one is open, so a review can
    /// force a fresh <see cref="OpenSettingsForReview"/> at each simulated display scale instead of
    /// reading the stale position of a panel that has been open since before the simulated move -
    /// this popup is an owned top-level window, not a child, so it never follows the main window on
    /// its own.
    /// </summary>
    internal void CloseSettingsForReview()
    {
        _settingsPopup?.Close();
    }

    /// <summary>
    /// The settings panel's placement against the window edge, the gear and the big toggle button
    /// it opened from - the regression proof for two complaints: a strip of the button left visible
    /// next to the panel (anything but a positive <c>panel.Right - toggle.Right</c>), and the panel
    /// landing nowhere near the gear at all (the multi-monitor case where
    /// <see cref="SettingsPopup.Open"/> used to clamp against the wrong monitor's bounds - a gap far
    /// wider than <see cref="SettingsEdgeGap"/> is that, not rounding).
    /// </summary>
    internal string DescribeSettingsAnchor()
    {
        if (_settingsPopup == null)
        {
            return "no settings panel open";
        }

        Rectangle gear = _gear.RectangleToScreen(_gear.ClientRectangle);
        Rectangle toggle = _toggle.RectangleToScreen(_toggle.ClientRectangle);
        Rectangle window = this.RectangleToScreen(this.ClientRectangle);
        Rectangle panel = _settingsPopup.Bounds;
        Rectangle screen = Screen.FromControl(this).WorkingArea;
        int edge = panel.Right - window.Right;
        int expectedEdge = -this.Scaled(SettingsEdgeGap);
        int cover = panel.Right - toggle.Right;

        var lines = new List<string>
        {
            $"gear                        {gear}",
            $"toggle                      {toggle}",
            $"window                      {window}",
            $"panel                       {panel}",
            $"screen                      {screen}",
            $"panel.Right - window.Right  {edge}  (expected {expectedEdge})",
            $"panel.Right - toggle.Right  {cover}",
            $"panel.Left - toggle.Left    {panel.Left - toggle.Left}",
            $"panel.Top - gear.Bottom     {panel.Top - gear.Bottom}",
        };

        if (edge > 0)
        {
            lines.Add($"OVERFLOW window: panel reaches {edge} px past the window's right edge");
        }
        else if (cover <= 0)
        {
            lines.Add($"CLIPPED toggle: {-cover} px strip of the button left visible on its right");
        }
        else if (Math.Abs(edge - expectedEdge) > this.Scaled(2))
        {
            lines.Add($"CLIPPED toggle: gap is {edge} px, expected {expectedEdge} px - panel drifted from the window edge");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// (Re-)registers the hotkey to match <see cref="_settings"/>, called on startup and every
    /// time the settings panel reports a change to either the switch or the combination. A
    /// combination already claimed by another application fails the registration silently at the
    /// Win32 level, so it is <see cref="_settings"/> itself that is switched back off here -
    /// otherwise the switch would show "on" for a hotkey that does nothing.
    /// </summary>
    private void ApplyHotkeySetting(SettingsPopup? popup)
    {
        HotKey.Unregister(Handle);

        if (_settings.HotkeyEnabled && !HotKey.Register(Handle, _settings.Hotkey))
        {
            // At startup there is no popup open to show this in - logged so it leaves a trace
            // beyond the setting silently flipping back off.
            AuraLog.Info("Hotkey already in use elsewhere, disabled.");
            _settings = _settings with { HotkeyEnabled = false };
            _settings.Save();
            popup?.ShowHotkeyConflict();
        }
    }

    /// <summary>
    /// How many times <see cref="SettleAfterDpiChange"/> re-measures before it gives up. The
    /// measurement it repeats is now right on the first pass (the stale layout it used to read is
    /// refreshed in <see cref="ResizeToContent"/>), so this is a backstop rather than the fix:
    /// a few spaced-out attempts cost nothing, and the log says so if one is ever needed.
    /// </summary>
    private const int MaxDpiSettleAttempts = 5;

    /// <summary>
    /// Dragged onto a display with a different scale. WinForms rescales the controls' own bounds,
    /// but not the widths this window measured for the display it was on before - and not the
    /// metrics inside a custom paint, which read the current dpi and so only need a repaint. The
    /// popups are windows of their own and get told separately.
    /// </summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        QueueSettle();
    }

    /// <summary>
    /// Re-measures once the current round of messages is through. Queued rather than run on the
    /// spot: WinForms is still working through its own rescale while it raises these, and
    /// measuring against controls it has not finished with gives the old display's sizes back.
    /// </summary>
    private void QueueSettle()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        BeginInvoke(() => SettleAfterDpiChange(1));
    }

    /// <summary>
    /// Re-applies every dpi-dependent measurement, then checks what the complaint is actually
    /// about - whether anything is short of room, the effect list's own text included - instead of
    /// trusting that one pass was enough. Runs while the window is still being dragged, on
    /// purpose - see <see cref="_userMoving"/> - so the layout catches up to the new monitor
    /// immediately instead of only once the mouse button comes back up.
    /// </summary>
    private void SettleAfterDpiChange(int attempt)
    {
        if (IsDisposed)
        {
            return;
        }

        ApplyScaledMetrics();

        if (_devices.Count > 0)
        {
            SetUpChannelSelector(); // already ends with FitEffectList()
        }
        else
        {
            FitEffectList();
        }

        Render();

        // "Nothing overflows" is only worth believing once the controls agree with the window
        // about which display they are on. Until they do, every width here - the room measured
        // and the room needed alike - comes from the same stale dpi, so they always match and the
        // check always passes, which is exactly how the layout could be left wrong.
        bool settled = _effects.DeviceDpi == DeviceDpi && _channel.DeviceDpi == DeviceDpi;
        bool clipped = RightOverflow().Any(measured => measured.Overflow > 0);

        if ((settled && !clipped) || attempt >= MaxDpiSettleAttempts)
        {
            return;
        }

        var retry = new System.Windows.Forms.Timer { Interval = 200 };
        retry.Tick += (_, _) =>
        {
            retry.Stop();
            retry.Dispose();
            SettleAfterDpiChange(attempt + 1);
        };
        retry.Start();
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

        SaveWindowPosition();

        if (_identifyCts != null)
        {
            // Program.Identify's restore pass (putting every channel of that controller back to
            // its own record) runs on a thread-pool thread that would otherwise die with the
            // process before it finishes, leaving the identified channel stuck lit.
            _identifyCts.Cancel();
            Settle(_identifyTask);
        }

        // A preview that nothing has written down is still on the board - either from a preset
        // editor that is still open (its own PreviewEnded fires only once this window is already
        // tearing down, and the restore behind it is an async continuation that never gets to run)
        // or from one closed a moment ago, whose restore is that very continuation. Either way the
        // board ends up on a look no file describes, and the next start shows a window that
        // disagrees with it. Handled here, on the same margin as the identify pass.
        bool previewOpen = OwnedForms.OfType<CustomPresetEditor>().Any();

        if (previewOpen || _previewRunning || _pendingPreview != null)
        {
            _pendingPreview = null;

            // Whatever is on the controller right now first, then the switch behind it: two
            // overlapping DiscoverAll sweeps time each other out.
            Settle(_previewTask);
            Settle(_runTask);
            Settle(Task.Run(Program.RestoreFromRecords));
        }
        else
        {
            // A switch may still be on its way to the controller; letting it finish is what keeps
            // state.json and the board describing the same thing.
            Settle(_runTask);
        }

        HotKey.Unregister(Handle);
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _trayClickTimer.Dispose();
        _busyTimer.Stop();
        _busyTimer.Dispose();
        _revealTimer?.Stop();
        _revealTimer?.Dispose();
        Icon?.Dispose();
        _iconOff?.Dispose();
        _iconOnTray?.Dispose();
    }

    /// <summary>
    /// Gives one last hardware task the moment it needs to finish while the window closes. Two
    /// seconds comfortably covers a full restore even on a board with many channels - each one
    /// costs two passes of two 8 ms-gapped writes - and matches the margin
    /// <see cref="AuraFiles.Lock"/> already gives a wedged process.
    /// </summary>
    private static void Settle(Task? task)
    {
        try
        {
            task?.Wait(2000);
        }
        catch (AggregateException)
        {
            // A cancelled or failed run has nothing left worth doing here; the window is going.
        }
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

        // Always the whole board, regardless of what is selected in the window - the hotkey is a
        // blunt "make it dark" switch, not a stand-in for the channel selector.
        if (HotKey.IsHotKeyMessage(m.Msg, m.WParam) && !IsDisposed)
        {
            _ = Run(() => Program.Switch(!_state.On));
        }

        // The window is being dragged or resized by hand. Repositioning it while it is still
        // following the mouse would only fight the user, so KeepOnScreen holds off on that until
        // the drag ends - but a WM_DPICHANGED that arrives mid-drag already names the new
        // monitor and scale, so SettleAfterDpiChange does not wait for it; only the position is
        // held back. WM_EXITSIZEMOVE still queues one more settle, for whatever a child control's
        // own dpi query answered too late to catch while dragging (see SettleAfterDpiChange).
        if (m.Msg == EnterSizeMove)
        {
            _userMoving = true;
        }
        else if (m.Msg == ExitSizeMove)
        {
            _userMoving = false;
            QueueSettle();
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
        SaveWindowPosition();
        _tray.Visible = true;
        _toggle.Paused = true;
        Hide();
    }

    /// <summary>
    /// Called from every path that ends the window being visible - minimising, closing to tray,
    /// and the real close below - so the position sticks regardless of which one the user takes.
    /// <see cref="RestoreBounds"/> rather than <see cref="Control.Location"/> once minimised: the
    /// live location while minimised is an off-screen placeholder Windows uses for the animation,
    /// not where the window actually sat.
    /// </summary>
    private void SaveWindowPosition()
    {
        Point location = WindowState == FormWindowState.Normal ? Location : RestoreBounds.Location;

        // Normalised to the 96 dpi baseline everything else here is written in, or a window
        // remembered from a 150 % display would come back too wide on a 100 % one.
        int width96 = ClientSize.Width * 96 / DeviceDpi;

        // Reloaded and merged under the same lock every other settings writer uses, rather than
        // saving this object's own possibly-stale copy of Settings - an open settings popup or
        // the background update check both save independently, and either landing between this
        // method's read and write would otherwise be overwritten right back out.
        using IDisposable guard = AuraFiles.Lock();
        _settings = AuraSettings.Load() with { WindowX = location.X, WindowY = location.Y, WindowWidth = width96 };
        _settings.Save();
    }

    private async void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        _tray.Visible = false;
        _toggle.Paused = false;

        if (_devices.Count == 0)
        {
            // The tray icon was the only thing left to click while no controller was found - a
            // reasonable moment to check again, since the user just came back to look.
            await DiscoverDevices();
        }
    }

    /// <summary>The newest preview waiting for the controller to be free, or null.</summary>
    private Action? _pendingPreview;

    private bool _previewRunning;

    /// <summary>The preview currently on its way to the controller, for a closing window to wait
    /// out before it hands the board back itself.</summary>
    private Task? _previewTask;

    /// <summary>
    /// Runs a preview on the controller, latest wins. A switch takes a few hundred ms, and the
    /// editor can easily produce another one in that time - dropping those (which is what
    /// <see cref="Run"/> does while busy) would leave the board showing an edit or two ago, so
    /// the newest one is held instead and run as soon as the previous finishes.
    /// </summary>
    /// <remarks>
    /// Waits out <see cref="_busy"/> before touching the controller, and holds it itself while
    /// the preview runs. Without that, a preview and a switch from <see cref="Run"/> could open
    /// two concurrent <see cref="AuraDevice.DiscoverAll"/> sweeps - each one's handshake times
    /// out waiting for the other's reply, which is the same failure <see cref="OnShown"/>
    /// already locks the first discovery against.
    /// </remarks>
    private async void QueuePreview(Action preview)
    {
        _pendingPreview = preview;

        if (_previewRunning)
        {
            return;
        }

        _previewRunning = true;
        try
        {
            while (_pendingPreview is Action next && !IsDisposed)
            {
                _pendingPreview = null;

                while (_busy && !IsDisposed)
                {
                    await Task.Delay(50);
                }

                if (IsDisposed)
                {
                    break;
                }

                SetBusy(true);
                try
                {
                    _previewTask = Task.Run(next);
                    await _previewTask;
                }
                catch (Exception ex)
                {
                    // The preview is a convenience, not the change itself - a controller that
                    // went away mid-edit is reported when Save actually tries to apply it.
                    // Anything else is a bug, and this runs as async void: uncaught, it would
                    // leave the loop with _previewRunning stuck and take the process with it
                    // instead of reaching the same dialog a failed switch gets.
                    if (ex is AuraNotFoundException or IOException)
                    {
                        AuraLog.Error("Preview", ex);
                    }
                    else if (!IsDisposed && Visible)
                    {
                        ErrorDialog.Report(ex, "Preview", this);
                    }
                    else
                    {
                        AuraLog.Error("Preview", ex);
                    }
                }
                finally
                {
                    if (!IsDisposed)
                    {
                        SetBusy(false);
                    }
                }
            }
        }
        finally
        {
            _previewRunning = false;
        }
    }

    /// <summary>
    /// Runs one switching action on a worker thread. Talking to the controller takes a moment,
    /// and doing it here instead of on the UI thread keeps the window painting and responsive.
    /// </summary>
    /// <param name="queue">
    /// Waits the running action out instead of dropping this one. Only for callers where losing
    /// the call would leave something half done: saving a preset has already written it to disk,
    /// so the apply that follows has to happen even when the editor's own live preview still
    /// holds the controller - and the editor is a window of its own, so <see cref="SetBusy"/>
    /// never disabled its Save button in the first place.
    /// </param>
    private async Task Run(Func<AuraState> action, bool queue = false)
    {
        if (_busy)
        {
            if (!queue)
            {
                return;
            }

            while (_busy && !IsDisposed)
            {
                await Task.Delay(50);
            }

            if (IsDisposed)
            {
                return;
            }
        }

        SetBusy(true);
        try
        {
            // Kept, so closing the window mid-switch can wait it out rather than leave the
            // controller half written and state.json describing the other half.
            _runTask = Task.Run(action);
            _state = await _runTask;
        }
        catch (Exception ex)
        {
            // AuraNotFoundException/IOException are expected hardware/IO conditions; anything
            // else is a bug, but this task is started fire-and-forget (WndProc, the hotkey, the
            // toggle click) - left uncaught, it would simply vanish instead of reaching
            // Application.ThreadException. Both get the same treatment either way.
            if (IsDisposed)
            {
                // Nothing left to show or update - the window is already gone.
            }
            else if (Visible)
            {
                ErrorDialog.Report(ex, "Switch", this);
            }
            else
            {
                AuraLog.Error("Switch", ex);
                _tray.ShowBalloonTip(4000, Strings.WindowTitle, ex.Message, ToolTipIcon.Warning);
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                SetBusy(false);
                Render();
            }
        }
    }

    /// <summary>
    /// Measured on the VM (no controller, so the fastest hardware call there is the one that
    /// fails outright): a brightness commit held <see cref="_busy"/> for about 188 ms end to end.
    /// Greying every control out and back in for that long reads as a flash, not feedback - so the
    /// visible part of being busy waits <see cref="BusyFlashDelayMs"/> before it shows at all, and
    /// never shows for anything that finishes inside that delay.
    /// </summary>
    private const int BusyFlashDelayMs = 180;

    private void SetBusy(bool busy)
    {
        // Dropping a request that arrives while one is already in flight must not wait for the
        // delay below - only the *visible* grey-out does.
        _busy = busy;

        if (busy)
        {
            _busyTimer.Stop();
            _busyTimer.Start();
            return;
        }

        _busyTimer.Stop();
        ApplyBusyVisuals(busy: false);
    }

    /// <summary>
    /// Fires <see cref="BusyFlashDelayMs"/> after a busy operation starts. A <see cref="SetBusy"/>
    /// call with <c>false</c> stops this timer before it ever ticks for anything that finished
    /// inside the delay, so nothing here runs for those - <see cref="_busy"/> is the source of
    /// truth this checks, not the timer having been started.
    /// </summary>
    private void OnBusyTimerTick(object? sender, EventArgs e)
    {
        _busyTimer.Stop();

        if (_busy)
        {
            ApplyBusyVisuals(busy: true);
        }
    }

    /// <summary>
    /// Nothing that talks to the controller comes back on while there is no controller to talk
    /// to. Releasing it used to re-enable all of it unconditionally, so a global hotkey press on a
    /// machine with no board left the effect list, chips and slider live behind a greyed-out
    /// button, each click raising another error.
    /// </summary>
    private void ApplyBusyVisuals(bool busy)
    {
        bool ready = !busy && _devices.Count > 0;

        _toggle.Busy = busy;
        _toggle.Enabled = ready;
        _effects.Enabled = ready;
        _channel.Enabled = ready;
        _colours.Enabled = ready;
        _brightness.Enabled = ready;

        // Requests that arrive while a switch is in flight are dropped, so the tray entry says
        // so instead of looking like it did nothing.
        _trayLighting.Enabled = ready;
        UseWaitCursor = busy;
    }

    /// <summary>Re-reads every piece of text, so a language change shows up straight away.</summary>
    private void RefreshTexts()
    {
        _effects.AccessibleName = Strings.PresetAccessibleName;
        _channel.AccessibleName = Strings.ChannelAccessibleName;
        _gear.AccessibleName = Strings.SettingsAccessibleName;
        _toggle.AccessibleName = Strings.ButtonAccessibleName;
        _colours.AccessibleName = Strings.ColourAccessibleName;
        _brightnessLabel.Text = Strings.SettingBrightness;
        _brightness.AccessibleName = Strings.SettingBrightness;

        RefreshEffectItems();
        if (_devices.Count > 0)
        {
            SetUpChannelSelector();
            Text = Strings.WindowTitle;
        }
        else
        {
            // The "no controller" title is a translated string too, so it has to follow a
            // language change like everything else on the window.
            Text = $"{Strings.WindowTitle} - {Strings.StatusControllerMissing}";
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

    /// <summary>
    /// Reloads every stored file after <see cref="SettingsPopup"/> has deleted all five, so the
    /// window comes back to first-run defaults without a restart. Nothing here touches a
    /// controller - what is on record for each channel changes, not what the hardware is doing,
    /// which is exactly what a plain repaint after <see cref="RefreshTexts"/> already handles.
    /// </summary>
    private void ResetToDefaults()
    {
        _settings = AuraSettings.Load();
        _state = AuraState.Load();
        _toggle.Animate = _settings.Animate;
        TopMost = _settings.AlwaysOnTop;

        // Reset can clear the hotkey, and a combination already registered with Windows keeps
        // switching the lighting until it is handed back - the setting alone does not do that.
        ApplyHotkeySetting(popup: null);

        Strings.Override = _settings.Language;
        RefreshTexts();
    }

    private void Render()
    {
        _displayed = Displayed;
        (byte mode, Color colour, bool on, byte brightness) = _displayed;
        bool wholeBoard = Target.DeviceKey == null;

        string effectKey;
        bool usesColour;

        // What the button paints. The same as what the rest of the window shows, except for a
        // custom preset, where the board-wide mode is only whatever was last picked before the
        // preset took over and would animate the button as something the board is not running.
        byte painted = mode;
        Color paintedColour = colour;
        byte paintedBrightness = brightness;

        // A custom preset is a bundle across channels, so it only names itself while the
        // selector is on "all channels"; with one channel picked, that channel's effect is
        // the honest thing to show.
        if (wholeBoard && _state.CustomPreset.Length > 0)
        {
            effectKey = CustomKey(_state.CustomPreset);
            usesColour = false;

            if (FindCustomPreset(_state.CustomPreset) is CustomPreset active &&
                MostUsed(active) is CustomPresetEntry common)
            {
                painted = common.Mode;
                paintedColour = Color.FromArgb(common.Red, common.Green, common.Blue);
                paintedBrightness = common.Brightness > 0 ? common.Brightness : brightness;
            }
        }
        else
        {
            AuraPreset preset = AuraPresets.ByMode(mode) ?? AuraPresets.All[0];
            effectKey = preset.Key;
            usesColour = preset.UsesColour;
        }

        _paintedMode = painted;
        _paintedColour = paintedColour;

        (byte red, byte green, byte blue) =
            AuraState.Dim(paintedColour.R, paintedColour.G, paintedColour.B, paintedBrightness);

        _toggle.Text = on ? Strings.ButtonStateOn : Strings.ButtonStateOff;
        _toggle.Animate = _settings.Animate;

        // The button previews what the lighting actually looks like, brightness included; the
        // chips and the list icons keep showing the pure colour, because that is the choice.
        _toggle.Show(on, painted, Color.FromArgb(red, green, blue));

        _effects.Colour = colour;
        _effects.ShowSelection(effectKey);

        _colours.Colour = colour;
        _colours.Visible = usesColour;
        _colours.Invalidate();

        // The board-wide brightness is a real, stored value even while a firmware effect (which
        // cannot be dimmed) is the one actually running - it is what the next dimmable effect
        // picked, or a channel with none of its own, will use. Only hidden for a single channel
        // sharing a controller-wide firmware effect, where no per-channel value applies at all.
        bool showBrightness = usesColour || wholeBoard;

        _brightness.Value = brightness;
        ShowBrightnessValue();
        _brightnessRow.Visible = showBrightness;

        // Everything in the notification area speaks for the whole board, never for whatever
        // single channel the selector happens to point at - the entry there switches the board
        // (same blunt gesture as the hotkey), so its label has to describe the board too.
        string boardEffect = _state.CustomPreset.Length > 0
            ? _state.CustomPreset
            : (AuraPresets.ByMode(_state.Mode) ?? AuraPresets.All[0]).DisplayName;

        _trayLighting.Text = _state.On ? Strings.ButtonStateOn : Strings.ButtonStateOff;
        _trayLighting.Checked = _state.On;

        // NotifyIcon.Text throws past 128 characters - AuraFiles.Caption already keeps a stored
        // preset name well under that, but a translated WindowTitle or preset name pushed right up
        // to that edge could still tip it over combined. Capped again here, hard, so no combination
        // of the two ever can.
        _tray.Text = AuraFiles.Caption(
            $"{Strings.WindowTitle} - {(_state.On ? boardEffect : Strings.ButtonStateOff)}", 63);
        _tray.Icon = _state.On ? (_iconOnTray ?? Icon) : (_iconOff ?? Icon);

        ResizeToContent(usesColour, showBrightness);
    }

    /// <summary>
    /// The entry a custom preset repeats most across its channels - the effect the board is mostly
    /// running while that preset is on, which is what the button animates instead of the
    /// board-wide mode the preset replaced. The entry itself is returned rather than just the
    /// mode, so the button can take its colour and brightness from the same channel rather than
    /// pairing an effect with a colour no channel actually has. A tie goes to whichever comes
    /// first, which is the order the editor lists the channels in.
    /// </summary>
    private static CustomPresetEntry? MostUsed(CustomPreset preset)
    {
        var counted = new Dictionary<byte, int>();
        CustomPresetEntry? best = null;
        int most = 0;

        foreach (CustomPresetEntry entry in preset.Entries)
        {
            counted.TryGetValue(entry.Mode, out int seen);
            counted[entry.Mode] = ++seen;

            if (seen > most)
            {
                most = seen;
                best = entry;
            }
        }

        // The winning mode's first entry, not the one that happened to push the count over: with
        // three channels breathing and the third one a different colour, the button should show
        // the colour the run of them starts with.
        return best == null ? null : preset.Entries.Find(entry => entry.Mode == best.Mode);
    }

    /// <summary>
    /// Room for the chips and the brightness slider is only reserved for what is actually
    /// visible - the firmware colours some effects itself, so the chips never apply to those,
    /// and a single channel sharing a controller-wide firmware effect has no brightness of its
    /// own to show either. The height is measured rather than hardcoded: at a larger display
    /// scale, or in German, the rows grow and a fixed number would eat into the button until it
    /// collapsed.
    /// </summary>
    private void ResizeToContent(bool showColour, bool showBrightness)
    {
        // Kept in step with what the row actually needs now - see _topRowHeight's own comment for
        // why this can't just be a RowStyle.AutoSize on _layout instead.
        _topRowHeight.Height = _topRow.PreferredSize.Height;

        int wanted = Padding.Vertical
            + _topRow.PreferredSize.Height
            + _toggle.Margin.Vertical + this.Scaled(ToggleHeight);

        if (showColour)
        {
            wanted += _colours.Height + _colours.Margin.Vertical;
        }

        if (showBrightness)
        {
            wanted += _brightnessRow.PreferredSize.Height + _brightnessRow.Margin.Vertical;
        }

        // Measured once: the property behind it walks every entry in the effect list.
        int width = WantedWidth;
        if (ClientSize.Height != wanted || ClientSize.Width != width)
        {
            ClientSize = new Size(width, wanted);
            KeepOnScreen();
        }

        // The panels inside the window keep the width they had before a display-scale change:
        // WinForms rescales their bounds itself, and the client size set above does not re-run the
        // dock pass for them afterwards. The effect list is then a few pixels narrower than the
        // width this window was just sized for and ellipsises its own longest entry - "cut off on
        // the right after moving to the other monitor", with nothing actually sticking out of the
        // window, which is why every overflow check missed it. Measured on the review harness
        // (-review layout 100 from a 150 % display): 473 px of panel inside a 479 px content box,
        // effect list 237 where it needed 243. One layout pass here brings both up to date.
        PerformLayout();
    }

    /// <summary>
    /// How far each visible top-row/colour-strip control's right edge sits past the content box,
    /// in pixels - zero or negative once it fits. Measured, never corrected: a window that grows
    /// itself out of a bad measurement keeps the extra width when the measurement was only wrong
    /// because a display-scale change was still in flight, which is how a trip to the second
    /// monitor and back left the window permanently wider. The check drives the settle in
    /// <see cref="SettleAfterDpiChange"/>, which re-measures instead, and the report
    /// <see cref="DescribeLayout"/> builds below.
    /// </summary>
    private IEnumerable<(string Name, int Right, int Overflow)> RightOverflow()
    {
        foreach ((string name, Control control) in new (string, Control)[]
                 { ("effects", _effects), ("channel", _channel), ("gear", _gear), ("colours", _colours) })
        {
            if (!control.Visible)
            {
                continue;
            }

            Point atForm = control.Parent!.PointToClient(PointToScreen(Point.Empty));
            int right = control.Right - atForm.X;
            yield return (name, right, right - (ClientSize.Width - Padding.Right));
        }

        // The other shape of the same complaint: everything sits inside the window, but the effect
        // list was handed less room than its longest entry needs and cuts the text off itself.
        // Reported in the same pixels-too-few units so one check covers both, and only while the
        // window can still grow at all - at the maximum width a shortened entry is the intended
        // result (see WantedWidth), not a fault.
        int missing = _effects.PreferredWidthForItems(includeHints: false) - _effects.Width;
        if (_effects.Visible && missing > 0 && ClientSize.Width < this.Scaled(MaxWidth))
        {
            yield return ("effects room", _effects.Right, missing);
        }
    }

    /// <summary>
    /// Pulls the window back into the working area after it has grown. It is centred at its
    /// starting size, before the channel selector has widened it, so it grows to the right from
    /// a position chosen for a narrower window - on a small screen at a high display scale that
    /// pushed its right-hand edge, gear and all, off the side.
    /// </summary>
    /// <remarks>
    /// A window lying across two displays is left exactly where it is. Pulling it onto the display
    /// it happened to overlap most put it fully on the other monitor, which changed the display
    /// scale, which re-measured and pulled it back - the window ping-ponged between the two for as
    /// long as it took to land, and every measurement in between was taken mid-flight. That is
    /// what left the layout wrong until something later (the on/off switch) measured it again on a
    /// window that had finally stopped moving.
    /// </remarks>
    private void KeepOnScreen()
    {
        if (!IsHandleCreated || !Visible || WindowState != FormWindowState.Normal || _userMoving)
        {
            return;
        }

        if (Screen.AllScreens.Count(display => display.Bounds.IntersectsWith(Bounds)) > 1)
        {
            return;
        }

        Rectangle screen = Screen.FromControl(this).WorkingArea;
        Location = new Point(
            Math.Max(screen.Left, Math.Min(Location.X, screen.Right - Width)),
            Math.Max(screen.Top, Math.Min(Location.Y, screen.Bottom - Height)));
    }

    /// <summary>
    /// Sizes every fixed distance for the display the window is on. Computed from the constants
    /// each time rather than by scaling whatever is there now, so calling it again after a
    /// display change lands on the same numbers instead of compounding them.
    /// </summary>
    private void ApplyScaledMetrics()
    {
        Padding = new Padding(this.Scaled(PadX), this.Scaled(PadY), this.Scaled(PadX), this.Scaled(PadY));

        int gap = this.Scaled(RowGap);
        _effects.Margin = new Padding(0, 0, gap, 0);
        _channel.Margin = new Padding(0, 0, gap, 0);

        _gear.Size = new Size(this.Scaled(GearSize), this.Scaled(GearSize));
        _gear.Margin = new Padding(0, this.Scaled(GearDrop), 0, 0);

        _toggle.Margin = new Padding(0, this.Scaled(ToggleGap), 0, 0);
        _colours.Margin = new Padding(0, this.Scaled(ColourGap), 0, 0);
        _brightnessRow.Margin = new Padding(0, this.Scaled(BrightnessGap), 0, 0);

        int inset = this.Scaled(LabelInset);
        _brightnessLabel.Margin = new Padding(inset, 0, 0, inset);
        _brightnessValue.Margin = new Padding(0, 0, inset, inset);
    }

    /// <summary>
    /// Every measurement the window's width depends on, as text - the regression proof for a
    /// layout complaint that can only be judged at a real display scale. Reports the room the top
    /// row actually needs against the room it was given, so "cut off on the right" is a number
    /// here rather than a guess from a screenshot.
    /// </summary>
    internal string DescribeLayout()
    {
        int usable = ClientSize.Width - Padding.Horizontal;
        int needed = _effects.PreferredWidthForItems(includeHints: false) + _effects.Margin.Horizontal
            + _gear.Width + _gear.Margin.Horizontal
            + (_channel.Visible ? _channel.Width + _channel.Margin.Horizontal : 0);

        var lines = new List<string>
        {
            $"dpi           {DeviceDpi} ({DeviceDpi * 100 / 96}%)  text measured at {Theme.TextDpi}"
                + (DeviceDpi == Theme.TextDpi ? "" : " - window is off the system scale, so the"
                    + " font carries the difference"),
            $"font          {_effects.Font.SizeInPoints:0.##}pt height={_effects.Font.Height} "
                + $"(baseline {Theme.Ui.SizeInPoints:0.##}pt height={Theme.Ui.Height})",
            $"clientsize    {ClientSize.Width}x{ClientSize.Height}",
            $"padding       {Padding.Left},{Padding.Top},{Padding.Right},{Padding.Bottom}",
            $"width bounds  min={this.Scaled(MinWidth)} max={this.Scaled(MaxWidth)} wanted={WantedWidth}",
            $"toprow needs  {needed}  usable={usable}  headroom={usable - needed}",
            $"  effects     w={_effects.Width} preferred={_effects.PreferredWidthForItems(includeHints: false)}",
            $"  channel     w={_channel.Width} visible={_channel.Visible}",
            $"  gear        w={_gear.Width} (expected {this.Scaled(GearSize)})",
            $"  colours     w={_colours.Width}",
            $"  margins     effects={_effects.Margin.Right} toggle={_toggle.Margin.Top} "
                + $"colours={_colours.Margin.Top} brightness={_brightnessRow.Margin.Top}",
            $"  panels      layout w={_layout.Width} margin={_layout.Margin.Horizontal} "
                + $"toprow w={_topRow.Width} margin={_topRow.Margin.Horizontal} "
                + $"window w={Width} display w={DisplayRectangle.Width}",
            $"  columns     effects x={_effects.Left} channel x={_channel.Left} gear x={_gear.Left}",
            $"button        effect={AuraPresets.ByMode(_toggle.Showing.Mode)?.Key ?? "?"} "
                + $"colour=#{_toggle.Showing.Colour.R:X2}{_toggle.Showing.Colour.G:X2}{_toggle.Showing.Colour.B:X2}"
                + (_state.CustomPreset.Length > 0 ? $"  (custom preset \"{_state.CustomPreset}\")" : ""),
            $"  child dpi   effects={_effects.DeviceDpi} channel={_channel.DeviceDpi}"
                + (_effects.DeviceDpi == DeviceDpi && _channel.DeviceDpi == DeviceDpi
                    ? ""
                    : " - STALE, still on the display this window came from"),
        };

        // The real question behind the complaint: does anything actually stick out past the right
        // edge of the window's content box?
        foreach ((string name, int right, int rightOverflow) in RightOverflow())
        {
            if (rightOverflow > 0)
            {
                lines.Add($"CLIPPED {name}: {rightOverflow} px short "
                    + $"(right={right}, limit={ClientSize.Width - Padding.Right})");
            }
        }

        if (needed > usable)
        {
            lines.Add($"OVERFLOW top row needs {needed - usable} px more than it has");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Wide enough for the top row to show its longest entry. The width used to be fixed, which
    /// English fitted into and German did not: with the channel selector shown, the effect list
    /// had barely 196 px left and cut off both "Lauflicht mit Ausblenden" and any preset name past
    /// about twenty characters. Capped, because a preset may be named up to forty characters and
    /// a window that grows to match would be absurd - past the cap the list ellipsises again.
    /// </summary>
    private int WantedWidth
    {
        get
        {
            // A hint row is never shown closed - it cannot be picked - so it must not drive how
            // wide the window becomes; the popup that opens still measures it via FitEffectList.
            int row = _effects.PreferredWidthForItems(includeHints: false) + _effects.Margin.Horizontal
                + _gear.Width + _gear.Margin.Horizontal;

            if (_channel.Visible)
            {
                row += _channel.Width + _channel.Margin.Horizontal;
            }

            return Math.Clamp(row + Padding.Horizontal, this.Scaled(MinWidth), this.Scaled(MaxWidth));
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

        // Sized from the box the menu gave it rather than in fixed pixels: a renderer is not a
        // control, so it has no dpi of its own to scale against - but that box already carries the
        // menu's, which is the same thing one step earlier.
        Rectangle box = e.ImageRectangle;
        int dot = Math.Max(4, Math.Min(box.Width, box.Height) / 2);
        e.Graphics.FillEllipse(brush, box.X + ((box.Width - dot) / 2), box.Y + ((box.Height - dot) / 2), dot, dot);
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
