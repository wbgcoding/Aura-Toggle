using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace AuraToggle.Tests;

[Trait(Layer.Name, Layer.UI)]
public class ThemedMenuTests
{
    /// <summary>Text boxes and the clipboard want a single-threaded apartment, which the test
    /// runner's threads are not.</summary>
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

    private static ToolStripMenuItem[] Items(TextBoxBase box) =>
        box.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().ToArray();

    [Fact]
    public void TheMenuReplacesTheWindowsOneWithSixThemedCommands()
    {
        OnSta(() =>
        {
            using var box = new TextBox();
            ThemedMenu.AttachTo(box);

            Assert.NotNull(box.ContextMenuStrip);
            Assert.IsType<MenuRenderer>(box.ContextMenuStrip!.Renderer);
            Assert.Equal(6, Items(box).Length);
            Assert.Equal(2, box.ContextMenuStrip.Items.OfType<ToolStripSeparator>().Count());
        });
    }

    [Fact]
    public void LabelsAndShortcutsFollowTheLanguageAtEachOpening()
    {
        OnSta(() =>
        {
            string before = Strings.Override;
            try
            {
                using var box = new TextBox();
                ThemedMenu.AttachTo(box);

                Strings.Override = "en";
                ThemedMenu.Prepare(box);
                Assert.Equal(new[] { "Undo", "Cut", "Copy", "Paste", "Delete", "Select all" },
                    Items(box).Select(i => i.Text));
                Assert.Equal("Ctrl+Z", Items(box)[0].ShortcutKeyDisplayString);
                Assert.Null(Items(box)[4].ShortcutKeyDisplayString);

                Strings.Override = "de";
                ThemedMenu.Prepare(box);
                Assert.Equal("Rückgängig", Items(box)[0].Text);
                Assert.Equal("Strg+A", Items(box)[5].ShortcutKeyDisplayString);
            }
            finally
            {
                Strings.Override = before;
            }
        });
    }

    [Fact]
    public void CommandsAreOfferedOnlyWhenTheyCanAct()
    {
        OnSta(() =>
        {
            using var box = new TextBox { Text = "preset name" };
            ThemedMenu.AttachTo(box);

            box.Select(0, 0);
            ThemedMenu.Prepare(box);
            ToolStripMenuItem[] items = Items(box);
            Assert.False(items[1].Enabled, "cut without a selection");
            Assert.False(items[2].Enabled, "copy without a selection");
            Assert.False(items[4].Enabled, "delete without a selection");
            Assert.True(items[5].Enabled, "select all with text not yet selected");

            box.SelectAll();
            ThemedMenu.Prepare(box);
            Assert.True(items[1].Enabled, "cut with a selection");
            Assert.True(items[2].Enabled, "copy with a selection");
            Assert.True(items[4].Enabled, "delete with a selection");
            Assert.False(items[5].Enabled, "select all when everything is selected");

            box.ReadOnly = true;
            ThemedMenu.Prepare(box);
            Assert.False(items[0].Enabled, "undo in a read-only box");
            Assert.False(items[1].Enabled, "cut in a read-only box");
            Assert.True(items[2].Enabled, "copy in a read-only box");
            Assert.False(items[3].Enabled, "paste in a read-only box");
            Assert.False(items[4].Enabled, "delete in a read-only box");
        });
    }

    [Fact]
    public void DeleteRemovesOnlyTheSelection()
    {
        OnSta(() =>
        {
            using var box = new TextBox { Text = "Aura Night" };
            ThemedMenu.AttachTo(box);
            box.Select(4, 6);

            Items(box)[4].PerformClick();

            Assert.Equal("Aura", box.Text);
        });
    }
}
