#if REVIEW
using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToggle;

/// <summary>Review build only: what the surface tool reads back from the window.</summary>
internal sealed partial class ToggleForm
{
    /// <summary>The big button, for <c>-review perf</c> to drive and time.</summary>
    internal EffectButton ToggleForReview => _toggle;

    /// <summary>What the occlusion check sees right now.</summary>
    internal string DescribeOcclusion() => _occlusion?.DescribeForReview() ?? "no occlusion watch";

    /// <summary>A click on the big button, as the mouse would give it.</summary>
    internal void ClickToggleForReview() => OnToggleClick(this, EventArgs.Empty);

    /// <summary>
    /// The open effect list against the big button it drops over: a list whose left edge is not
    /// clearly left of the button's leaves a column of the button showing beside it (STRIP).
    /// </summary>
    internal string DescribeEffectsAnchor(SelectPopup list)
    {
        System.Drawing.Rectangle toggle = _toggle.RectangleToScreen(_toggle.ClientRectangle);
        int overhang = toggle.Left - list.Left;
        string verdict = list.Bottom > toggle.Top && overhang < this.Scaled(2) ? "STRIP " : "";
        return $"{verdict}list.Left {list.Left}  toggle.Left {toggle.Left}  overhang {overhang}";
    }

    /// <summary>
    /// Every measurement the window's width depends on, as text - the check for a layout
    /// fit that can only be judged at a real display scale. Reports the room the top
    /// row actually needs against the room it was given, so "cut off on the right" is a number
    /// here rather than a guess from a screenshot.
    /// </summary>
    internal string DescribeLayout()
    {
        int usable = ClientSize.Width - Padding.Horizontal;
        int needed = _effects.PreferredWidthForItems(includeHints: false) + _effects.Margin.Horizontal
            + _gear.Width + _gear.Margin.Horizontal
            + (_channelShown ? _channel.Width + _channel.Margin.Horizontal : 0);

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
            $"  channel     w={_channel.Width} visible={_channelShown}",
            $"  gear        w={_gear.Width} (expected {this.Scaled(GearSize)})",
            $"  colours     w={_colours.Width} visible={_colours.Visible}",
            // The slider only exists for an effect brightness can reach - the one thing about it
            // no screenshot of a still window can tell apart from a window that is simply short.
            $"  brightness  visible={_brightnessRow.Visible} h={_brightnessRow.Height}",
            // The reason and the retry button: shown only while discovery has come back empty.
            $"  notice      visible={_noticeRow.Visible} h={_noticeRow.Height}"
                + (_noticeRow.Visible ? $" text w={_noticeText.Width}" : ""),
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

        // Does anything actually stick out past the right edge of the window's content box?
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
}
#endif
