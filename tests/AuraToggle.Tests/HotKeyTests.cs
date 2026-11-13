using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Xunit;

namespace AuraToggle.Tests;

public class HotKeyTests
{
    public static IEnumerable<object[]> ModifierRows() => Enumerable.Range(0, 256).Select(m => new object[] { m });

    [Fact]
    public void DefaultIsControlAltL()
    {
        Assert.Equal(HotKey.ModControl | HotKey.ModAlt, HotKey.Modifiers(HotKey.Default));
        Assert.Equal((int)Keys.L, HotKey.VirtualKey(HotKey.Default));
        Assert.Equal(0x034C, HotKey.Default);
    }

    [Fact]
    public void ModifierFlagsMatchTheWin32Values()
    {
        Assert.Equal(1, HotKey.ModAlt);
        Assert.Equal(2, HotKey.ModControl);
        Assert.Equal(4, HotKey.ModShift);
        Assert.Equal(8, HotKey.ModWin);
        Assert.Equal(0x0312, HotKey.WM_HOTKEY);
    }

    [Theory]
    [MemberData(nameof(ModifierRows))]
    public void PackRoundTripsEveryModifierAndKey(int modifiers)
    {
        for (int key = 0; key < 256; key++)
        {
            int packed = HotKey.Pack(modifiers, key);
            Assert.Equal(modifiers, HotKey.Modifiers(packed));
            Assert.Equal(key, HotKey.VirtualKey(packed));
            Assert.InRange(packed, 0, 0xFFFF);
        }
    }

    [Theory]
    [InlineData(0x1FF, 0x1FF, 0xFFFF)]
    [InlineData(0x100, 0x100, 0)]
    [InlineData(0x102, 0x14C, 0x024C)]
    [InlineData(-1, -1, 0xFFFF)]
    [InlineData(-1, 0, 0xFF00)]
    [InlineData(0, -1, 0x00FF)]
    [InlineData(int.MaxValue, int.MinValue, 0xFF00)]
    public void PackMasksOutOfRangeInput(int modifiers, int key, int expected)
    {
        Assert.Equal(expected, HotKey.Pack(modifiers, key));
    }

    [Theory]
    [InlineData(0x034C, 3, 0x4C)]
    [InlineData(0x0000, 0, 0)]
    [InlineData(0xFFFF, 0xFF, 0xFF)]
    [InlineData(-1, 0xFF, 0xFF)]
    [InlineData(0x12345678, 0x56, 0x78)]
    public void UnpackIgnoresBitsAboveTheTwoBytes(int packed, int modifiers, int key)
    {
        Assert.Equal(modifiers, HotKey.Modifiers(packed));
        Assert.Equal(key, HotKey.VirtualKey(packed));
    }

    [Theory]
    [InlineData(0x10, true)] // Shift
    [InlineData(0x11, true)] // Control
    [InlineData(0x12, true)] // Alt
    [InlineData(0x5B, true)] // left Windows
    [InlineData(0x5C, true)] // right Windows
    [InlineData(0xA0, true)]
    [InlineData(0xA1, true)]
    [InlineData(0xA2, true)]
    [InlineData(0xA3, true)]
    [InlineData(0xA4, true)]
    [InlineData(0xA5, true)]
    [InlineData(0x41, false)] // A
    [InlineData(0x20, false)] // Space
    [InlineData(0x70, false)] // F1
    [InlineData(0x14, false)] // Caps Lock
    [InlineData(0x5D, false)] // Apps
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void IsModifierKeyOnlyForShiftControlAltAndWindows(int virtualKey, bool expected)
    {
        Assert.Equal(expected, HotKey.IsModifierKey(virtualKey));
    }

    [Theory]
    [InlineData(0x41, true)] // A
    [InlineData(0x5A, true)] // Z
    [InlineData(0x30, true)] // 0
    [InlineData(0x39, true)] // 9
    [InlineData(0x70, true)] // F1
    [InlineData(0x87, true)] // F24
    [InlineData(0x20, true)] // Space
    [InlineData(0x0D, true)] // Enter
    [InlineData(0x1B, true)] // Escape
    [InlineData(0x08, true)] // Back
    [InlineData(0x09, true)] // Tab
    [InlineData(0x25, true)] // Left
    [InlineData(0x60, true)] // NumPad0
    [InlineData(0x6B, true)] // Add
    [InlineData(0xBC, true)] // Oemcomma
    [InlineData(0, false)]
    [InlineData(1, false)] // mouse buttons
    [InlineData(2, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)] // unassigned
    [InlineData(0x0E, false)] // unassigned
    [InlineData(0x3A, false)] // unassigned
    [InlineData(0x10, false)] // modifiers
    [InlineData(0x11, false)]
    [InlineData(0x12, false)]
    [InlineData(0x5B, false)]
    [InlineData(0xA0, false)]
    [InlineData(0xA5, false)]
    [InlineData(-1, false)]
    [InlineData(0x1234, false)]
    public void IsUsableKeyTable(int virtualKey, bool expected)
    {
        Assert.Equal(expected, HotKey.IsUsableKey(virtualKey));
    }

    [Fact]
    public void NoMouseButtonOrModifierIsEverUsable()
    {
        // A hand-edited settings file could carry a mouse button or a lone modifier as the key.
        for (int key = 0; key <= (int)Keys.XButton2; key++)
        {
            Assert.False(HotKey.IsUsableKey(key), $"vk {key}");
        }

        for (int key = 0; key < 256; key++)
        {
            if (HotKey.IsModifierKey(key))
            {
                Assert.False(HotKey.IsUsableKey(key), $"vk {key}");
            }
        }
    }

    [Theory]
    [InlineData(Keys.A)]
    [InlineData(Keys.Z)]
    [InlineData(Keys.D1)]
    [InlineData(Keys.F1)]
    [InlineData(Keys.F12)]
    [InlineData(Keys.NumPad0)]
    [InlineData(Keys.NumPad7)]
    [InlineData(Keys.Space)]
    [InlineData(Keys.Home)]
    [InlineData(Keys.Left)]
    [InlineData(Keys.Oemcomma)]
    public void KeyNameIsNeverEmpty(Keys key)
    {
        Assert.False(string.IsNullOrWhiteSpace(HotKey.KeyName((int)key)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(0xFF)]
    [InlineData(-5)]
    public void KeyNameFallsBackToTheEnumSpellingForKeysWithoutAScanCode(int virtualKey)
    {
        Assert.False(string.IsNullOrWhiteSpace(HotKey.KeyName(virtualKey)));
    }

    [Fact]
    public void ExtendedKeysAreNotNamedAfterTheKeypadKeySharingTheirScanCode()
    {
        // Home and the keypad 7 share one scan code; without the extended-key bit both read the same.
        Assert.NotEqual(HotKey.KeyName((int)Keys.Home), HotKey.KeyName((int)Keys.NumPad7));
        Assert.NotEqual(HotKey.KeyName((int)Keys.Left), HotKey.KeyName((int)Keys.NumPad4));
        Assert.NotEqual(HotKey.KeyName((int)Keys.Delete), HotKey.KeyName((int)Keys.Decimal));
    }

    [Fact]
    public void IsHotKeyMessageNeedsTheMessageAndTheFixedId()
    {
        Assert.True(HotKey.IsHotKeyMessage(HotKey.WM_HOTKEY, new IntPtr(0xA000)));
        Assert.False(HotKey.IsHotKeyMessage(HotKey.WM_HOTKEY, IntPtr.Zero));
        Assert.False(HotKey.IsHotKeyMessage(HotKey.WM_HOTKEY, new IntPtr(0xA001)));
        Assert.False(HotKey.IsHotKeyMessage(0x0100, new IntPtr(0xA000)));
        Assert.False(HotKey.IsHotKeyMessage(0, IntPtr.Zero));
    }
}
