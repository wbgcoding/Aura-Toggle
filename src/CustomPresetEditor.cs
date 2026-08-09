using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// Creates or edits one custom preset: a name plus, for every channel the machine has, a
/// built-in effect, a colour and a brightness. Saving switches the board to the preset and adds
/// it to the effect list, where it can be picked again like any built-in effect.
/// </summary>
internal sealed class CustomPresetEditor : PopupForm
{
    // Wide enough for the colour strip's nine chips, which do not shrink to fit their parent.
    private const int ContentWidth = 330;

    private readonly List<ChannelRow> _rows = new();
    private readonly Layout _root;
    private readonly Panel _scroll;
    private readonly TextField _name = new();
    private readonly PillButton _save = new();
    private readonly PillButton _delete = new();
    private readonly ArmedButton _deleteArm;
    private readonly string? _editing;

    /// <summary>Every scaled distance this editor sets, so a display-scale change can put them all
    /// back at the new scale instead of leaving the rows measured for the old one.</summary>
    private readonly ScaledMetrics _metrics = new();

    /// <summary>Read once rather than on every keystroke of the name field.</summary>
    private readonly HashSet<string> _existingNames;

    /// <summary>
    /// Set once Save has applied the preset, so closing does not undo it. Left false by Delete -
    /// nothing new was applied there, so the hardware still has to be put back to its own records.
    /// </summary>
    private bool _saved;

    /// <param name="preset">The preset to edit, or null to create one.</param>
    /// <param name="devices">
    /// The controllers the window already found. Passed in rather than looked up here: talking
    /// to the hardware from a constructor on the UI thread stalls the window, and a second
    /// discovery while the first one is still settling came back empty.
    /// </param>
    /// <param name="current">
    /// The lighting as it runs right now, used to seed a new preset's rows. Each row prefers
    /// what that channel itself was last set to and falls back to this global state, since the
    /// controller cannot be asked what any channel is currently running.
    /// </param>
    public CustomPresetEditor(CustomPreset? preset, List<AuraDeviceSummary> devices, AuraState current)
    {
        _editing = preset?.Name;
        _existingNames = AuraCustomPresets.Load().Select(p => p.Name).ToHashSet();

        AutoScaleMode = AutoScaleMode.Dpi;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        AccessibleName = Strings.CustomPresetAccessibleName;
        _metrics.Add(() =>
            Padding = new Padding(this.Scaled(16), this.Scaled(14), this.Scaled(16), this.Scaled(14)));

        _root = new Layout
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Theme.Surface,
        };

        // A board with a lot of channels would make this taller than the screen, so the rows
        // live in a scrolling panel and the window itself stops at the work area.
        _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface };
        _scroll.Controls.Add(_root);
        Controls.Add(_scroll);

        // WinForms reaches the rows after it has told this window about a display-scale change, so
        // the spacing has to be put back when they say so too, not only when the window does.
        _root.DpiChangedAfterParent += (_, _) => Resettle();
        _root.FontChanged += (_, _) => Resettle();

        // Heading on the left, a discard button on the right: closing without saving needs a
        // visible way out, not just the Escape key.
        var header = new Layout
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = Theme.Surface,
        };
        _metrics.Add(() => header.Margin = new Padding(0, 0, 0, this.Scaled(8)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var heading = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Text = preset == null ? Strings.CustomPresetNew : Strings.CustomPresetEdit,
            Font = Theme.Heading,
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
        };
        _metrics.Add(() => heading.Margin = new Padding(this.Scaled(2), this.Scaled(2), 0, 0));
        header.Controls.Add(heading, 0, 0);

        var discard = new DeleteButton
        {
            Anchor = AnchorStyles.Right,
            AccessibleName = Strings.CustomPresetDiscard,
            Margin = new Padding(0),
        };
        discard.Click += (_, _) => Close();
        header.Controls.Add(discard, 1, 0);

        _root.Controls.Add(header);

        _name.Dock = DockStyle.Top;
        _name.PlaceholderText = Strings.CustomPresetNamePlaceholder;
        _name.AccessibleName = Strings.CustomPresetNamePlaceholder;
        _name.MaxLength = 40;
        _metrics.Add(() => _name.Margin = new Padding(0, 0, 0, this.Scaled(10)));
        _name.Text = preset?.Name ?? "";
        _name.Accepted += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                SaveAndClose();
                e.SuppressKeyPress = true;
            }
        };
        _name.TextChanged += (_, _) => UpdateSaveState();
        _root.Controls.Add(_name);

        if (devices.Count == 0)
        {
            var empty = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Text = Strings.CustomPresetNoDevices,
                ForeColor = Theme.TextMuted,
                BackColor = Theme.Surface,
            };
            _metrics.Add(() => empty.Margin = new Padding(this.Scaled(2), 0, 0, this.Scaled(10)));
            _root.Controls.Add(empty);
        }

        Dictionary<string, string> chosen = AuraChannelNames.All();
        Dictionary<string, ChannelLighting> remembered = AuraChannelStates.All();

        foreach (AuraDeviceSummary device in devices)
        {
            foreach (AuraChannel channel in device.Channels)
            {
                _rows.Add(BuildChannelRow(device, channel, devices.Count > 1, current, chosen, remembered));
            }
        }

        var buttons = new Layout
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = Theme.Surface,
        };
        _metrics.Add(() => buttons.Margin = new Padding(0, this.Scaled(6), 0, 0));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _save.Text = preset == null ? Strings.CustomPresetCreate : Strings.CustomPresetSave;
        _save.Primary = true;
        _save.Font = Theme.Action; // the one action this panel leads with, so it says so
        _save.DesignHeight = 40;
        _metrics.Add(() => _save.Width = this.Scaled(130));
        _save.Dock = DockStyle.Left;
        _save.BackColor = Theme.Surface;
        _save.Click += (_, _) => SaveAndClose();
        _save.FitToText();
        buttons.Controls.Add(_save, 0, 0);

        _delete.Text = Strings.CustomPresetDelete;
        _delete.DesignHeight = 40;
        _delete.Fill = Theme.NeutralSoft;
        _delete.ForeColor = Theme.Danger;
        _delete.BackColor = Theme.Surface;
        _metrics.Add(() =>
        {
            _delete.Width = this.Scaled(96);
            _delete.Margin = new Padding(this.Scaled(8), 0, 0, 0);
        });
        _delete.Visible = _editing != null;

        _deleteArm = new ArmedButton(_delete, Strings.CustomPresetDelete, Strings.CustomPresetConfirmDelete, 16);
        _deleteArm.Confirmed += (_, _) => OnDeleteConfirmed();

        buttons.Controls.Add(_delete, 1, 0);

        _root.Controls.Add(buttons);

        if (preset != null)
        {
            Fill(preset);
        }

        UpdateSaveState();
        FitToContent();

        // The window has no title bar of its own, and - like ErrorDialog - it stays open rather
        // than closing on an outside click, so it needs its own way to move. Dragged by its
        // heading, and by its own background, which is what is left of the window once the rows
        // have their say.
        WindowDrag.Enable(this, this, header, heading);
    }

    /// <summary>
    /// A preset needs a name and at least one channel. Greying the button out says so, instead of
    /// the button looking ready and then quietly doing nothing.
    /// </summary>
    private void UpdateSaveState()
    {
        bool named = _name.Text.Trim().Length > 0;
        _save.Enabled = named && _rows.Count > 0;

        // Saving onto a name that already belongs to another preset replaces it, so the button
        // says that rather than "Create".
        bool replaces = named && _name.Text.Trim() != _editing && _existingNames.Contains(_name.Text.Trim());

        _save.Text = replaces
            ? Strings.CustomPresetReplace
            : _editing == null ? Strings.CustomPresetCreate : Strings.CustomPresetSave;

        _save.FitToText();
    }

    private sealed class ChannelRow
    {
        public required string DeviceKey;
        public required int Channel;
        public required string Label;
        public required Select Effect;
        public required ColourStrip Colours;

        /// <summary>Label, percentage and slider together, so the row hides as one.</summary>
        public required Layout BrightnessRow;

        public required Slider Brightness;

        public required Label BrightnessValue;
    }

    /// <summary>Raised after a save or a delete, so the window behind can refresh its list.</summary>
    public event EventHandler? PresetsChanged;

    /// <summary>
    /// Raised once Save has written the preset to disk, carrying it so the window behind can
    /// actually apply it - committing what the live preview already put on the hardware, and
    /// bringing <c>state.json</c>/<c>channel-state.json</c> in step with what is now showing.
    /// Without this, saving left the board showing the new preset while every record still
    /// described the old one, and the very next unrelated action would snap it back.
    /// </summary>
    public event EventHandler<CustomPreset>? Saved;

    /// <summary>Raised once Delete has removed the preset, carrying its name, so the window
    /// behind can clear it from the active state if it was the one running.</summary>
    public event EventHandler<string>? Deleted;

    /// <summary>
    /// Raised whenever a row changes, carrying the preset as it stands. The window applies it to
    /// the hardware without recording anything, so the preset can be judged on the machine
    /// itself while it is being put together.
    /// </summary>
    public event EventHandler<CustomPreset>? PreviewRequested;

    /// <summary>Raised once the editor closes, so the lighting goes back to what it was.</summary>
    public event EventHandler? PreviewEnded;

    private ChannelRow BuildChannelRow(AuraDeviceSummary device, AuraChannel channel, bool nameDevice,
        AuraState current, Dictionary<string, string> chosen, Dictionary<string, ChannelLighting> remembered)
    {
        string label = ChannelLabels.For(device, channel, nameDevice, chosen);

        // One block per channel, and the block has to say which channel at a glance - the rows
        // below it look identical from one channel to the next. A hairline above every block but
        // the first keeps them apart once the list is long enough to scroll.
        if (_rows.Count > 0)
        {
            var rule = new Panel
            {
                Dock = DockStyle.Top,
                BackColor = Theme.Border,
            };
            _metrics.Add(() =>
            {
                rule.Height = this.Scaled(1);
                rule.Margin = new Padding(this.Scaled(2), this.Scaled(6), this.Scaled(2), 0);
            });
            _root.Controls.Add(rule);
        }

        var caption = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = label,
            Font = Theme.Subheading,
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
        };
        _metrics.Add(() =>
        {
            // Wraps rather than clips: a channel can be renamed to anything up to 30 characters,
            // and in this weight that outgrows the panel well before the limit.
            caption.MaximumSize = new Size(this.Scaled(ContentWidth - 4), 0);
            caption.Margin = new Padding(this.Scaled(2), this.Scaled(10), 0, this.Scaled(6));
        });
        _root.Controls.Add(caption);

        var effect = new Select
        {
            Dock = DockStyle.Top,
            DesignHeight = 32,
            BackColor = Theme.Surface,
            AccessibleName = label,
        };
        _metrics.Add(() => effect.Margin = new Padding(0, 0, 0, this.Scaled(8)));
        // What this very channel last ran, or the board-wide state when it has never been set
        // on its own.
        ChannelLighting seed = AuraChannelStates.Get(remembered, device.Key, channel.Index)
            ?? new ChannelLighting(current.Mode, current.Red, current.Green, current.Blue);

        effect.SetItems(AuraPresets.All.Select(p => new SelectItem(p.Key, p.DisplayName, p.Mode)));
        effect.ShowSelection(AuraPresets.ByMode(seed.Mode)?.Key ?? AuraPresets.All[0].Key);
        _root.Controls.Add(effect);

        var colours = new ColourStrip
        {
            Anchor = AnchorStyles.Left,
            BackColor = Theme.Surface,
            Colour = Color.FromArgb(seed.Red, seed.Green, seed.Blue),
        };
        _metrics.Add(() => colours.Margin = new Padding(0, 0, 0, this.Scaled(8)));
        _root.Controls.Add(colours);

        // Brightness per channel, laid out like the one in the window so both read the same way.
        var brightnessValue = new Label
        {
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
        };
        _metrics.Add(() => brightnessValue.Margin = new Padding(0, 0, this.Scaled(2), this.Scaled(2)));

        var brightness = new Slider
        {
            Dock = DockStyle.Top,
            Minimum = AuraState.MinBrightness,
            Maximum = AuraState.MaxBrightness,
            AccessibleName = $"{label} - {Strings.SettingBrightness}",
            Margin = new Padding(0),
            // A channel that has one of its own starts there, otherwise at the board-wide value.
            Value = seed.Brightness == 0 ? current.Brightness : seed.Brightness,
        };

        var brightnessRow = new Layout
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Theme.Surface,
        };
        brightnessRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        brightnessRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var brightnessLabel = new Label
        {
            AutoSize = true,
            Text = Strings.SettingBrightness,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
        };
        _metrics.Add(() =>
        {
            brightnessRow.Margin = new Padding(0, 0, 0, this.Scaled(10));
            brightnessLabel.Margin = new Padding(this.Scaled(2), 0, 0, this.Scaled(2));
        });
        brightnessRow.Controls.Add(brightnessLabel, 0, 0);
        brightnessRow.Controls.Add(brightnessValue, 1, 0);
        brightnessRow.Controls.Add(brightness, 0, 1);
        brightnessRow.SetColumnSpan(brightness, 2);
        _root.Controls.Add(brightnessRow);

        var row = new ChannelRow
        {
            DeviceKey = device.Key,
            Channel = channel.Index,
            Label = label,
            Effect = effect,
            Colours = colours,
            BrightnessRow = brightnessRow,
            Brightness = brightness,
            BrightnessValue = brightnessValue,
        };

        brightness.ValueChanged += (_, _) => ShowBrightnessValue(row);

        // Debounced by the slider itself, so dragging it sends one switch when it settles rather
        // than one per pixel - the controller takes a few hundred ms per switch.
        brightness.ValueCommitted += (_, _) => RaisePreview();
        ShowBrightnessValue(row);

        // The row's own icon previews the effect in the colour picked right below it.
        effect.Colour = colours.Colour;
        colours.ColourPicked += (_, _) =>
        {
            effect.Colour = colours.Colour;
            effect.Invalidate();
            RaisePreview();
        };

        effect.SelectionChanged += (_, _) =>
        {
            ShowColours(row);
            FitToContent();
            RaisePreview();
        };

        ShowColours(row);
        return row;
    }

    /// <summary>
    /// The chips and the brightness only make sense for the effects that actually carry a colour -
    /// the firmware colours the rest itself and takes no brightness either.
    /// </summary>
    private static void ShowColours(ChannelRow row)
    {
        AuraPreset? preset = row.Effect.Selected == null ? null : AuraPresets.Find(row.Effect.Selected.Key);
        bool usesColour = preset?.UsesColour ?? false;

        row.Colours.Visible = usesColour;
        row.BrightnessRow.Visible = usesColour;
    }

    private static void ShowBrightnessValue(ChannelRow row) => row.BrightnessValue.Text =
        string.Format(CultureInfo.CurrentCulture, Strings.BrightnessValue, row.Brightness.Value);

    /// <summary>
    /// Keeps the window exactly as tall as its rows, which change with each effect. Every call
    /// before <see cref="Open"/> has run measures against <see cref="Screen.FromPoint"/> of
    /// (0, 0) - wherever that lands is corrected once <see cref="Open"/> knows the real screen
    /// and re-measures against <paramref name="workingArea"/> instead.
    /// </summary>
    private void FitToContent(Rectangle? workingArea = null)
    {
        int wanted = _root.PreferredSize.Height + Padding.Vertical;
        int available = (workingArea ?? Screen.FromPoint(Location).WorkingArea).Height - this.Scaled(48);
        bool scrolls = wanted > available;

        // Scaled, because this runs again on every effect change - long after WinForms scaled the
        // window for the display it opened on. Assigning the plain 96 dpi number here squeezed the
        // panel back to half its width on a 200 % screen and cut the colour chips off with it.
        ClientSize = new Size(
            this.Scaled(ContentWidth) + Padding.Horizontal +
                (scrolls ? SystemInformation.VerticalScrollBarWidth : 0),
            Math.Min(wanted, available));

        // This runs on every effect change, long after Open() placed the window - a row that grows
        // by its colour chips and brightness slider would otherwise push Save and Delete off the
        // bottom of the screen.
        KeepOnScreen();
    }

    /// <summary>Fills every field from an existing preset, so it can be edited.</summary>
    private void Fill(CustomPreset preset)
    {
        foreach (ChannelRow row in _rows)
        {
            // An entry saved before presets went per-channel carries channel -1 and stands for
            // the whole controller, so it seeds every channel of that controller.
            CustomPresetEntry? entry = preset.Entries.FirstOrDefault(
                e => e.DeviceKey == row.DeviceKey && (e.Channel == row.Channel || e.Channel < 0));

            if (entry == null)
            {
                continue;
            }

            row.Effect.ShowSelection(AuraPresets.ByMode(entry.Mode)?.Key ?? AuraPresets.All[0].Key);
            row.Colours.Colour = Color.FromArgb(entry.Red, entry.Green, entry.Blue);
            row.Effect.Colour = row.Colours.Colour;

            if (entry.Brightness != 0)
            {
                row.Brightness.Value = entry.Brightness;
            }

            ShowColours(row);
        }

        FitToContent();
    }

    private void SaveAndClose()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0 || _rows.Count == 0)
        {
            _name.Focus();
            return;
        }

        List<CustomPresetEntry> entries = CurrentEntries();

        if (entries.Count == 0)
        {
            return;
        }

        CustomPreset saved;
        using (IDisposable guard = AuraFiles.Lock())
        {
            List<CustomPreset> presets = AuraCustomPresets.Load();
            saved = new CustomPreset(name, entries);

            // Renaming replaces the entry being edited; saving over another name overwrites it,
            // which is the expected meaning of Save here.
            presets.RemoveAll(p => p.Name == name || p.Name == _editing);
            presets.Add(saved);
            AuraCustomPresets.Save(presets);
        }

        _saved = true;
        PresetsChanged?.Invoke(this, EventArgs.Empty);
        Saved?.Invoke(this, saved);
        Close();
    }

    /// <summary>The rows as preset entries, as they stand right now.</summary>
    private List<CustomPresetEntry> CurrentEntries()
    {
        var entries = new List<CustomPresetEntry>();

        foreach (ChannelRow row in _rows)
        {
            if (row.Effect.Selected == null || AuraPresets.Find(row.Effect.Selected.Key) is not AuraPreset preset)
            {
                continue;
            }

            entries.Add(new CustomPresetEntry(
                row.DeviceKey, row.Channel, row.Label, preset.Mode,
                row.Colours.Colour.R, row.Colours.Colour.G, row.Colours.Colour.B,
                // The firmware coloured effects take no brightness, so the slider is hidden for
                // them and nothing is saved rather than a value that would never be used.
                preset.UsesColour ? (byte)row.Brightness.Value : (byte)0));
        }

        return entries;
    }

    /// <summary>
    /// Shows the rows as they stand on the real hardware, so the preset is judged by looking at
    /// the machine rather than at nine small icons. Nothing is written - <see cref="PreviewEnded"/>
    /// puts the lighting back when the editor closes.
    /// </summary>
    private void RaisePreview()
    {
        List<CustomPresetEntry> entries = CurrentEntries();
        if (entries.Count > 0)
        {
            PreviewRequested?.Invoke(this, new CustomPreset(_name.Text.Trim(), entries));
        }
    }

    /// <summary>The arm/confirm timing and text swap live in <see cref="ArmedButton"/> now; this
    /// is just the action itself, run once the second click confirms it.</summary>
    private void OnDeleteConfirmed()
    {
        if (_editing == null)
        {
            return;
        }

        using (IDisposable guard = AuraFiles.Lock())
        {
            List<CustomPreset> presets = AuraCustomPresets.Load();
            presets.RemoveAll(p => p.Name == _editing);
            AuraCustomPresets.Save(presets);
        }

        // Deleted first: it clears _state.CustomPreset if this was the active one, so the render
        // PresetsChanged triggers next already reflects that instead of repainting the tray with
        // a name that is about to disappear and then never repainting again.
        Deleted?.Invoke(this, _editing);
        PresetsChanged?.Invoke(this, EventArgs.Empty);
        Close();
    }

    /// <summary>
    /// Opens next to the window. Deliberately the one popup that an outside click does not
    /// dismiss - it holds text that has not been saved yet. Escape, Save and Delete close it.
    /// </summary>
    public void Open(Point at, IWin32Window? owner)
    {
        FitToContent(Screen.FromPoint(at).WorkingArea);
        Place(at);

        if (owner == null)
        {
            Show();
        }
        else
        {
            Show(owner);
        }

        Activate();
        _name.FocusInput();

        // The rows are seeded from what each channel is already running, so the first preview is
        // usually a no-op - but not when an existing preset was loaded into them.
        RaisePreview();
    }

    /// <summary>Dragged onto a display with a different scale: the height and width this was
    /// fitted to belong to the one it came from.</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Resettle();
    }

    /// <summary>Every row's spacing back at the current scale, then the window fitted to what that
    /// measures - queued, since WinForms is still working through its own rescale.</summary>
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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        // Saving already applied and recorded the preset; anything else leaves the preview on the
        // hardware and nothing on record, so the lighting has to be put back.
        if (!_saved)
        {
            PreviewEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }

        // Enter is handled by the name field itself (TextField.Accepted): this form only holds
        // the field, so its own Focused is never true while the caret is in it.
        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deleteArm.Dispose();
        }

        base.Dispose(disposing);
    }
}
