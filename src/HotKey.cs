using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// The one global hotkey the window owns, packed into the single int <c>settings.json</c> stores
/// it as: the modifier flags in the high byte, the virtual-key code in the low byte.
/// </summary>
internal static class HotKey
{
    public const int WM_HOTKEY = 0x0312;

    public const int ModAlt = 0x0001;
    public const int ModControl = 0x0002;
    public const int ModShift = 0x0004;
    public const int ModWin = 0x0008;

    /// <summary>Win32-only, never packed into the stored int: without it, holding the hotkey
    /// down re-fires <see cref="WM_HOTKEY"/> on every OS key-repeat tick and flashes the board.</summary>
    private const uint ModNoRepeat = 0x4000;

    /// <summary>Ctrl+Alt+L.</summary>
    public const int Default = ((ModControl | ModAlt) << 8) | 0x4C;

    /// <summary>
    /// Fixed: this window registers exactly one hotkey, so there is nothing to tell apart in
    /// <see cref="WM_HOTKEY"/>'s wParam.
    /// </summary>
    private const int Id = 0xA000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int lParam, StringBuilder buffer, int size);

    public static bool IsHotKeyMessage(int msg, IntPtr wParam) => msg == WM_HOTKEY && wParam.ToInt32() == Id;

    public static bool Register(IntPtr window, int packed) =>
        RegisterHotKey(window, Id, (uint)Modifiers(packed) | ModNoRepeat, (uint)VirtualKey(packed));

    public static void Unregister(IntPtr window) => UnregisterHotKey(window, Id);

    public static int Modifiers(int packed) => (packed >> 8) & 0xFF;

    public static int VirtualKey(int packed) => packed & 0xFF;

    public static int Pack(int modifiers, int virtualKey) => ((modifiers & 0xFF) << 8) | (virtualKey & 0xFF);

    /// <summary>
    /// The key as it is actually printed on the user's keyboard. <see cref="Keys"/> has only
    /// internal spellings to offer - "D1" for the 1 key, "Oemcomma" for the comma - and where the
    /// punctuation keys sit differs per layout anyway, so Windows is asked instead of guessing.
    /// It answers in the layout's own language too, which a table here never would.
    /// </summary>
    public static string KeyName(int virtualKey)
    {
        const uint MapVkToScanCode = 0;

        uint scanCode = MapVirtualKey((uint)virtualKey, MapVkToScanCode);
        if (scanCode == 0)
        {
            return ((Keys)virtualKey).ToString();
        }

        // Bit 24 tells GetKeyNameText this is one of the keys that share a scan code with the
        // numeric keypad. Without it Home, the arrows and the rest come back named after the
        // keypad key sitting on the same code.
        int lParam = (int)(scanCode << 16) | (IsExtended(virtualKey) ? 1 << 24 : 0);

        var name = new StringBuilder(64);
        return GetKeyNameText(lParam, name, name.Capacity) > 0
            ? name.ToString()
            : ((Keys)virtualKey).ToString();
    }

    /// <summary>A modifier pressed on its own - never the key half of a combination.</summary>
    public static bool IsModifierKey(int virtualKey) => (Keys)virtualKey is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.LWin or Keys.RWin;

    /// <summary>
    /// Whether a key can be the second half of a combination at all. The recorder only ever hands
    /// over a key a keyboard actually produced, but a hand-edited <c>settings.json</c> carries
    /// whatever someone typed into it: mouse buttons are virtual-key codes too
    /// (<see cref="Keys.LButton"/> and its neighbours), and most of the code range belongs to no
    /// key at all. Either one registers as a combination nobody can press, and the settings panel
    /// would show it as the one that is set.
    /// </summary>
    public static bool IsUsableKey(int virtualKey) =>
        virtualKey > (int)Keys.XButton2 &&
        Enum.IsDefined(typeof(Keys), (Keys)virtualKey) &&
        !IsModifierKey(virtualKey);

    private static bool IsExtended(int virtualKey) => (Keys)virtualKey is
        Keys.Insert or Keys.Delete or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or
        Keys.Left or Keys.Right or Keys.Up or Keys.Down or
        Keys.NumLock or Keys.PrintScreen or Keys.Divide or Keys.Apps;
}
