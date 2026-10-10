using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// How the window keeps its picture of the lighting in step with the files on disk. The command
/// line (the two shipped shortcuts, a script, a scheduled task) switches the board without the
/// window being told, so nothing here trusts the copy held in <c>_state</c> for a decision.
/// </summary>
internal sealed partial class ToggleForm
{
    /// <summary>
    /// <see cref="Displayed"/> worked out from the given state and records instead of the cached
    /// ones. <paramref name="remembered"/> only matters when a controller or channel is named.
    /// </summary>
    private (byte Mode, Color Colour, bool On, byte Brightness) DisplayedFor(
        AuraState state, Dictionary<string, ChannelLighting> remembered, string? deviceKey, int channel)
    {
        var board = (state.Mode, Color.FromArgb(state.Red, state.Green, state.Blue), state.On, state.Brightness);

        if (deviceKey == null)
        {
            return board;
        }

        if (channel >= 0)
        {
            return AuraChannelStates.Get(remembered, deviceKey, channel) is ChannelLighting one
                ? Look(one, state)
                : board;
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

        return first == null ? board : Look(first with { On = anyOn }, state);
    }

    /// <summary>One channel's record as the window shows it, brightness resolved.</summary>
    private static (byte Mode, Color Colour, bool On, byte Brightness) Look(ChannelLighting look, AuraState state) => (
        look.Mode,
        Color.FromArgb(look.Red, look.Green, look.Blue),
        look.On,
        look.Brightness == 0 ? state.Brightness : look.Brightness);

    /// <summary>
    /// The power the board shows right now, read from disk. The tray entry, the tray click and
    /// the hotkey all flip it, and a flip based on the copy from the last time the window looked
    /// switches nothing visible when a shortcut already did the same in between. Called on the
    /// worker thread inside <see cref="Run"/>, so the small read never touches the UI thread.
    /// </summary>
    private static bool BoardIsOn() => AuraState.Load().On;

    /// <summary>
    /// Takes in whatever the command line changed behind the window and repaints, so the button,
    /// the tray icon and the tray entry show the real state. Skipped while a switch is running
    /// (its result replaces the state anyway) and before the first reveal.
    /// </summary>
    private void RefreshFromDisk()
    {
        if (_busy || !_revealed || IsDisposed)
        {
            return;
        }

        AuraState fresh = AuraState.Load();
        (string? deviceKey, int channel) = Target;
        var shown = DisplayedFor(fresh, deviceKey == null ? new() : AuraChannelStates.All(), deviceKey, channel);

        if (fresh != _state || shown != _displayed)
        {
            _state = fresh;
            Render();
        }
    }

    protected override void OnActivated(System.EventArgs e)
    {
        base.OnActivated(e);
        RefreshFromDisk();
    }
}
