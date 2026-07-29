using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// Creates and manages custom presets: a name plus, for every connected controller, one
/// built-in effect and colour. Saving applies nothing by itself - the preset shows up in the
/// effect list like any other, and is switched to from there.
/// </summary>
internal sealed class CustomPresetEditor : Form
{
    // Wide enough for the colour strip's nine chips, which do not shrink to fit their parent.
    private const int Width_ = 330;

    private readonly List<AuraDeviceSummary> _devices;
    private readonly List<DeviceRow> _rows = new();
    private readonly Layout _root;
    private readonly Layout _presetListPanel;
    private readonly TextBox _name;
    private readonly Label _noDevices;
    private readonly PillButton _save = new();
    private readonly PillButton _delete = new();

    private List<CustomPreset> _presets;
    private string? _editingName;

    public CustomPresetEditor(CustomPreset? preset)
    {
        _presets = AuraCustomPresets.Load();
        _devices = AuraDevice.ListDevices();

        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9F);
        DoubleBuffered = true;
        KeyPreview = true;
        Padding = new Padding(16, 14, 16, 14);

        _root = new Layout
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Width = Width_,
        };
        Controls.Add(_root);

        _presetListPanel = new Layout
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 4),
        };
        _root.Controls.Add(_presetListPanel);
        RefreshPresetList();

        _name = new TextBox
        {
            Dock = DockStyle.Top,
            Font = new Font(Font.FontFamily, 10F),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            PlaceholderText = Strings.CustomPresetNamePlaceholder,
            Margin = new Padding(0, 4, 0, 10),
            MaxLength = 40,
        };
        _root.Controls.Add(_name);

        _noDevices = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = Strings.CustomPresetNoDevices,
            ForeColor = Theme.TextMuted,
            Visible = _devices.Count == 0,
            Margin = new Padding(0, 0, 0, 10),
        };
        _root.Controls.Add(_noDevices);

        foreach (AuraDeviceSummary device in _devices)
        {
            _rows.Add(BuildDeviceRow(device));
        }

        var buttons = new Layout
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Margin = new Padding(0, 8, 0, 0),
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _save.Text = Strings.CustomPresetSave;
        _save.Height = 32;
        _save.Width = 100;
        _save.Dock = DockStyle.Left;
        _save.Enabled = _devices.Count > 0;
        _save.Click += (_, _) => SaveAndClose();
        buttons.Controls.Add(_save, 0, 0);

        _delete.Text = Strings.CustomPresetDelete;
        _delete.Height = 32;
        _delete.Width = 90;
        _delete.Fill = Theme.NeutralSoft;
        _delete.ForeColor = Theme.TextMuted;
        _delete.Margin = new Padding(8, 0, 0, 0);
        _delete.Visible = false;
        _delete.Click += (_, _) => DeleteEditingAndClose();
        buttons.Controls.Add(_delete, 1, 0);

        _root.Controls.Add(buttons);

        if (preset != null)
        {
            LoadPreset(preset);
        }

        ClientSize = new Size(Width_ + Padding.Horizontal, _root.PreferredSize.Height + Padding.Vertical);
        Resize += (_, _) => Height = _root.PreferredSize.Height + Padding.Vertical;
    }

    private sealed class DeviceRow
    {
        public required AuraDeviceSummary Device;
        public required Select Effect;
        public required ColourStrip Colours;
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

    private void RefreshPresetList()
    {
        _presetListPanel.Controls.Clear();
        _presetListPanel.RowCount = _presets.Count;
        for (int i = 0; i < _presets.Count; i++)
        {
            _presetListPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        foreach (CustomPreset preset in _presets)
        {
            var row = new Layout
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Margin = new Padding(0, 0, 0, 2),
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var link = new LinkLabel
            {
                AutoSize = true,
                Text = preset.Name,
                LinkColor = Theme.Text,
                ActiveLinkColor = Theme.Accent,
                VisitedLinkColor = Theme.Text,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Theme.Surface,
                Margin = new Padding(2, 4, 0, 4),
            };
            link.Click += (_, _) => LoadPreset(preset);
            row.Controls.Add(link, 0, 0);

            var delete = new DeleteButton { Margin = new Padding(4, 0, 0, 0) };
            delete.Click += (_, _) => DeletePreset(preset);
            row.Controls.Add(delete, 1, 0);

            _presetListPanel.Controls.Add(row);
        }
    }

    private DeviceRow BuildDeviceRow(AuraDeviceSummary device)
    {
        var label = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = device.Name,
            ForeColor = Theme.TextMuted,
            Margin = new Padding(2, 6, 0, 4),
        };
        _root.Controls.Add(label);

        var effect = new Select
        {
            Dock = DockStyle.Top,
            Height = 32,
            Margin = new Padding(0, 0, 0, 8),
        };
        effect.SetItems(AuraPresets.All.Select(p => new SelectItem(p.Key, p.DisplayName, p.Mode)));
        effect.ShowSelection(AuraPresets.All[0].Key);
        _root.Controls.Add(effect);

        var colours = new ColourStrip
        {
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 0, 8),
        };
        _root.Controls.Add(colours);

        var row = new DeviceRow { Device = device, Effect = effect, Colours = colours };

        effect.SelectionChanged += (_, _) => UpdateColourVisibility(row);
        colours.ColourPicked += (_, _) => { }; // live preview only; the value is read on save

        UpdateColourVisibility(row);
        return row;
    }

    private static void UpdateColourVisibility(DeviceRow row)
    {
        AuraPreset? preset = row.Effect.Selected == null ? null : AuraPresets.Find(row.Effect.Selected.Key);
        row.Colours.Visible = preset?.UsesColour ?? false;
    }

    /// <summary>Fills every field from an existing preset, so it can be edited or resaved.</summary>
    private void LoadPreset(CustomPreset preset)
    {
        _editingName = preset.Name;
        _name.Text = preset.Name;
        _delete.Visible = true;

        foreach (DeviceRow row in _rows)
        {
            CustomPresetEntry? entry = preset.Entries.FirstOrDefault(e => e.DeviceKey == row.Device.Key);
            if (entry == null)
            {
                continue;
            }

            row.Effect.ShowSelection(AuraPresets.ByMode(entry.Mode)?.Key ?? AuraPresets.All[0].Key);
            row.Colours.Colour = Color.FromArgb(entry.Red, entry.Green, entry.Blue);
            UpdateColourVisibility(row);
        }

        Height = _root.PreferredSize.Height + Padding.Vertical;
        Invalidate(true);
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
        foreach (DeviceRow row in _rows)
        {
            if (row.Effect.Selected == null || AuraPresets.Find(row.Effect.Selected.Key) is not AuraPreset preset)
            {
                continue;
            }

            entries.Add(new CustomPresetEntry(
                row.Device.Key, row.Device.Name, preset.Mode,
                row.Colours.Colour.R, row.Colours.Colour.G, row.Colours.Colour.B));
        }

        if (entries.Count == 0)
        {
            return;
        }

        // Saving under a name that is already taken - including the one being edited -
        // overwrites it, which is the expected meaning of Save here.
        _presets.RemoveAll(p => p.Name == name);
        _presets.Add(new CustomPreset(name, entries));
        AuraCustomPresets.Save(_presets);

        PresetsChanged?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void DeleteEditingAndClose()
    {
        if (_editingName == null)
        {
            return;
        }

        _presets.RemoveAll(p => p.Name == _editingName);
        AuraCustomPresets.Save(_presets);
        PresetsChanged?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void DeletePreset(CustomPreset preset)
    {
        _presets.RemoveAll(p => p.Name == preset.Name);
        AuraCustomPresets.Save(_presets);
        RefreshPresetList();
        Height = _root.PreferredSize.Height + Padding.Vertical;
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens beside the settings panel. Not modal - a click elsewhere closes it.</summary>
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
        _name.Focus();
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

        base.OnKeyDown(e);
    }
}
