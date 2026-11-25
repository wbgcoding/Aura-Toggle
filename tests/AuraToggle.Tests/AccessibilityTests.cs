using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace AuraToggle.Tests;

[Trait(Layer.Name, Layer.UI)]
public class AccessibilityTests
{
    private static void OnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread never finished");
        if (failure != null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    private static string[] ChildNames(AccessibleObject list) =>
        Enumerable.Range(0, list.GetChildCount()).Select(i => list.GetChild(i)!.Name ?? "").ToArray();

    [Fact]
    public void EveryColourChipHasANameInTheCurrentLanguage()
    {
        OnSta(() =>
        {
            string before = Strings.Override;
            try
            {
                using var strip = new ColourStrip { Colour = Color.FromArgb(255, 56, 56) };
                AccessibleObject list = strip.AccessibilityObject;

                Strings.Override = "en";
                Assert.Equal(AccessibleRole.List, list.Role);
                Assert.Equal(
                    new[] { "White", "Red", "Orange", "Yellow", "Green", "Cyan", "Blue", "Purple", "Pink", "Custom colour" },
                    ChildNames(list));

                Strings.Override = "de";
                Assert.Equal("Rot", list.GetChild(1)!.Name);
                Assert.Equal("Eigene Farbe", list.GetChild(9)!.Name);
            }
            finally
            {
                Strings.Override = before;
            }
        });
    }

    [Fact]
    public void OnlyTheCurrentColourChipReadsAsSelected()
    {
        OnSta(() =>
        {
            using var strip = new ColourStrip { Colour = Color.FromArgb(255, 56, 56) };
            AccessibleObject list = strip.AccessibilityObject;

            int[] selected = Enumerable.Range(0, list.GetChildCount())
                .Where(i => list.GetChild(i)!.State.HasFlag(AccessibleStates.Selected))
                .ToArray();

            Assert.Equal(new[] { 1 }, selected);
            Assert.Equal(AccessibleRole.ListItem, list.GetChild(1)!.Role);
        });
    }

    [Fact]
    public void ChoosingAChipThroughItsAccessibleObjectPicksThatColour()
    {
        OnSta(() =>
        {
            using var strip = new ColourStrip { Colour = Color.White };
            bool picked = false;
            strip.ColourPicked += (_, _) => picked = true;

            strip.AccessibilityObject.GetChild(6)!.DoDefaultAction();

            Assert.True(picked);
            Assert.Equal(Color.FromArgb(64, 124, 255).ToArgb(), strip.Colour.ToArgb());
        });
    }

    [Fact]
    public void ListRowsAreNamedAndOnlyTheOpeningEntryReadsAsSelected()
    {
        OnSta(() =>
        {
            var items = new List<SelectItem>
            {
                new("static", "Static", 1),
                new("rainbow", "Rainbow", 5),
                new("hint", "Needs a second controller", null, IsHint: true, Hint: "why"),
            };

            using var popup = new SelectPopup(items, items[1], Color.White, 200, SystemFonts.MessageBoxFont!, 96);
            AccessibleObject list = popup.AccessibilityObject;

            Assert.Equal(AccessibleRole.List, list.Role);
            Assert.Equal(new[] { "Static", "Rainbow", "Needs a second controller" }, ChildNames(list));
            Assert.False(list.GetChild(0)!.State.HasFlag(AccessibleStates.Selected));
            Assert.True(list.GetChild(1)!.State.HasFlag(AccessibleStates.Selected));
            Assert.True(list.GetChild(1)!.State.HasFlag(AccessibleStates.Focused));
            Assert.Equal(AccessibleStates.Unavailable, list.GetChild(2)!.State);
            Assert.Equal("why", list.GetChild(2)!.Description);
            Assert.Null(list.GetChild(3));
        });
    }
}
