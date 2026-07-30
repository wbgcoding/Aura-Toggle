using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// Creates or edits one custom preset: a name plus, for every channel the machine has, a
/// built-in effect and a colour. Saving applies nothing by itself - the preset appears in the
/// effect list like any other, and is switched to from there.
/// </summary>
internal sealed class CustomPresetEditor : Form
{
    // Wide enough for the colour strip's nine chips, which do not shrink to fit their parent.
    private const int ContentWidth = 330;

    private readonly List<ChannelRow> _rows = new();
    private readonly Layout _root;
    private readonly Panel _scroll;
    private readonly TextField _name = new();
    private readonly PillButton _save = new();
    private readonly PillButton _delete = new();
    private readonly string? _editing;

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

        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        AccessibleName = Strings.CustomPresetAccessibleName;
        DoubleBuffered = true;
        KeyPreview = true;
        Padding = new Padding(16, 14, 16, 14);

        _root = new Layout
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Theme.Surface,
            Width = ContentWidth,
        };

        // A board with a lot of channels would make this taller than the screen, so the rows
        // live in a scrolling panel and the window itself stops at the work area.
        _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface };
        _scroll.Controls.Add(_root);
        Controls.Add(_scroll);

        // Heading on the left, a discard button on the right: closing without saving needs a
        // visible way out, not just the Escape key.
        var header = new Layout
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 0, 8),
        };
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
            Margin = new Padding(2, 2, 0, 0),
        };
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
        _name.Margin = new Padding(0, 0, 0, 10);
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
            _root.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Text = Strings.CustomPresetNoDevices,
                ForeColor = Theme.TextMuted,
                BackColor = Theme.Surface,
                Margin = new Padding(2, 0, 0, 10),
            });
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
            Margin = new Padding(0, 6, 0, 0),
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _save.Text = preset == null ? Strings.CustomPresetCreate : Strings.CustomPresetSave;
        _save.Primary = true;
        _save.Font = Theme.Action; // the one action this panel leads with, so it says so
        _save.Height = 40;
        _save.Width = 130;
        _save.Dock = DockStyle.Left;
        _save.BackColor = Theme.Surface;
        _save.Click += (_, _) => SaveAndClose();
        _save.FitToText();
        buttons.Controls.Add(_save, 0, 0);

        _delete.Text = Strings.CustomPresetDelete;
        _delete.Height = 40;
        _delete.Width = 96;
        _delete.Fill = Theme.NeutralSoft;
        _delete.ForeColor = Theme.Danger;
        _delete.BackColor = Theme.Surface;
        _delete.Margin = new Padding(8, 0, 0, 0);
        _delete.Visible = _editing != null;
        _delete.Click += (_, _) => DeleteAndClose();
        _delete.FitToText(16);
        buttons.Controls.Add(_delete, 1, 0);

        _root.Controls.Add(buttons);

        if (preset != null)
        {
            Fill(preset);
        }

        UpdateSaveState();
        FitToContent();

        // The window has no title bar of its own, and it is the one panel that stays open, so it
        // is dragged by its heading - and by its own background, which is what is left of the
        // window once the rows have their say.
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
        bool replaces = named && _name.Text.Trim() != _editing &&
            AuraCustomPresets.Load().Exists(p => p.Name == _name.Text.Trim());

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

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return parameters;
        }
    }

    private ChannelRow BuildChannelRow(AuraDeviceSummary device, AuraChannel channel, bool nameDevice,
        AuraState current, Dictionary<string, string> chosen, Dictionary<string, ChannelLighting> remembered)
    {
        string label = ChannelLabels.For(device, channel, nameDevice, chosen);

        _root.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = label,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Margin = new Padding(2, 4, 0, 4),
        });

        var effect = new Select
        {
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = Theme.Surface,
            AccessibleName = label,
            Margin = new Padding(0, 0, 0, 8),
        };
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
            Margin = new Padding(0, 0, 0, 8),
        };
        _root.Controls.Add(colours);

        // Brightness per channel, laid out like the one in the window so both read the same way.
        var brightnessValue = new Label
        {
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 2, 2),
        };

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
            Margin = new Padding(0, 0, 0, 10),
        };
        brightnessRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        brightnessRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        brightnessRow.Controls.Add(new Label
        {
            AutoSize = true,
            Text = Strings.SettingBrightness,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Margin = new Padding(2, 0, 0, 2),
        }, 0, 0);
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
        ShowBrightnessValue(row);

        // The row's own icon previews the effect in the colour picked right below it.
        effect.Colour = colours.Colour;
        colours.ColourPicked += (_, _) =>
        {
            effect.Colour = colours.Colour;
            effect.Invalidate();
        };

        effect.SelectionChanged += (_, _) =>
        {
            ShowColours(row);
            FitToContent();
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

    /// <summary>Keeps the window exactly as tall as its rows, which change with each effect.</summary>
    private void FitToContent()
    {
        int wanted = _root.PreferredSize.Height + Padding.Vertical;
        int available = Screen.FromPoint(Location).WorkingArea.Height - 48;
        bool scrolls = wanted > available;

        ClientSize = new Size(
            ContentWidth + Padding.Horizontal + (scrolls ? SystemInformation.VerticalScrollBarWidth : 0),
            Math.Min(wanted, available));
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

        if (entries.Count == 0)
        {
            return;
        }

        List<CustomPreset> presets = AuraCustomPresets.Load();

        // Renaming replaces the entry being edited; saving over another name overwrites it,
        // which is the expected meaning of Save here.
        presets.RemoveAll(p => p.Name == name || p.Name == _editing);
        presets.Add(new CustomPreset(name, entries));
        AuraCustomPresets.Save(presets);

        PresetsChanged?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void DeleteAndClose()
    {
        if (_editing == null)
        {
            return;
        }

        List<CustomPreset> presets = AuraCustomPresets.Load();
        presets.RemoveAll(p => p.Name == _editing);
        AuraCustomPresets.Save(presets);

        PresetsChanged?.Invoke(this, EventArgs.Empty);
        Close();
    }

    /// <summary>
    /// Opens next to the window. Deliberately the one popup that an outside click does not
    /// dismiss - it holds text that has not been saved yet. Escape, Save and Delete close it.
    /// </summary>
    public void Open(Point at, IWin32Window? owner)
    {
        Rectangle screen = Screen.FromPoint(at).WorkingArea;
        int x = Math.Min(at.X, screen.Right - Width - 4);
        int y = Math.Min(at.Y, screen.Bottom - Height - 4);
        Location = new Point(Math.Max(screen.Left + 4, x), Math.Max(screen.Top + 4, y));

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
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.RoundWindowCorners(Handle);
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
}
