using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AuraToggle;

/// <summary>
/// Tells whether a control can be seen at all: not when its window is minimised, hidden, on
/// another virtual desktop or off every monitor, the session is locked, or other windows cover
/// every pixel of it. An animation that nobody can see only costs power. Windows reports every
/// window that moves, shows, hides, changes order or is cloaked (<c>SetWinEventHook</c>), so a
/// window moved off the control is noticed on the next pass of the message loop and the
/// animation carries on before a stale frame can show. A slow poll backs that up for windows
/// whose events do not reach this process, such as an elevated one, and for a message lost with
/// a window handle that went away.
/// </summary>
/// <remarks>
/// Errs towards "visible": a window that might let the control show through - see-through,
/// click-through or with per-pixel transparency - never counts as covering it.
/// </remarks>
internal sealed class OcclusionWatch : IDisposable
{
    private readonly Form _form;
    private readonly Control _target;
    private readonly Action<bool> _changed;
    private readonly WinEventProc _proc;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 500 };
    private readonly List<IntPtr> _hooks = new();
    private bool _queued;
    private bool _hidden;
    private bool _disposed;

    public OcclusionWatch(Form form, Control target, Action<bool> changed)
    {
        _form = form;
        _target = target;
        _changed = changed;

        // Kept in a field: the hook calls back through it for as long as it is installed, and
        // a delegate only referenced by native code would be collected out from under it.
        _proc = OnWinEvent;

        // The hooks skip this process, so its own window moving, changing size or coming to the
        // front (a click on it, Alt+Tab) is caught here.
        _form.Move += OnOwnChange;
        _form.Resize += OnOwnChange;
        _form.VisibleChanged += OnOwnChange;
        _form.VisibleChanged += OnFormVisibleChanged;
        _form.Activated += OnOwnChange;
        _form.Deactivate += OnOwnChange;
        _target.VisibleChanged += OnOwnChange;
        _target.SizeChanged += OnOwnChange;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        // Checks straight away rather than through Queue: a queued check whose message was lost
        // would otherwise block every later one for good.
        _poll.Tick += (_, _) => Check();
        SyncHooks();
        Queue();
    }

    public bool Hidden => _hidden;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SyncHooks();
        _poll.Dispose();
        _form.Move -= OnOwnChange;
        _form.Resize -= OnOwnChange;
        _form.VisibleChanged -= OnOwnChange;
        _form.VisibleChanged -= OnFormVisibleChanged;
        _form.Activated -= OnOwnChange;
        _form.Deactivate -= OnOwnChange;
        _target.VisibleChanged -= OnOwnChange;
        _target.SizeChanged -= OnOwnChange;
    }

    private void OnOwnChange(object? sender, EventArgs e) => Queue();

    private void OnFormVisibleChanged(object? sender, EventArgs e) => SyncHooks();

    /// <summary>
    /// The hooks and the poll only run while the window is shown. A window hidden in the
    /// notification area is hidden whatever else happens on screen, and listening to every other
    /// program there would wake this one up for nothing.
    /// </summary>
    private void SyncHooks()
    {
        bool wanted = !_disposed && _form.Visible;
        if (wanted == (_hooks.Count > 0))
        {
            return;
        }

        if (wanted)
        {
            // Only the events that can change what covers the window: foreground, move/size and
            // minimise; a window destroyed, shown, hidden, reordered, moved or (un)cloaked.
            _hooks.Add(SetWinEventHook(EventSystemForeground, EventSystemMinimizeEnd, IntPtr.Zero, _proc, 0, 0, HookFlags));
            _hooks.Add(SetWinEventHook(EventObjectDestroy, EventObjectReorder, IntPtr.Zero, _proc, 0, 0, HookFlags));
            _hooks.Add(SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, IntPtr.Zero, _proc, 0, 0, HookFlags));
            _hooks.Add(SetWinEventHook(EventObjectCloaked, EventObjectUncloaked, IntPtr.Zero, _proc, 0, 0, HookFlags));
            _poll.Start();
        }
        else
        {
            _poll.Stop();
            foreach (IntPtr hook in _hooks)
            {
                UnhookWinEvent(hook);
            }

            _hooks.Clear();
        }
    }

    /// <summary>Only a prompt to look again: whether the session is locked is read fresh on every
    /// check (<see cref="InputDesktopIsOurs"/>), so a missed unlock - a remote reconnect raises
    /// others - cannot leave the animation asleep.</summary>
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) => Queue();

    private void OnWinEvent(IntPtr hook, uint winEvent, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Only whole windows: the same events fire for the caret, the cursor and every child
        // control, none of which can hide this one.
        if (winEvent >= EventObjectDestroy && (idObject != ObjidWindow || idChild != 0))
        {
            return;
        }

        Queue();
    }

    /// <summary>One check per pass of the message loop, however many events arrived in it -
    /// dragging a window raises hundreds.</summary>
    private void Queue()
    {
        if (_queued || _disposed || !_form.IsHandleCreated || _form.IsDisposed)
        {
            return;
        }

        _queued = true;
        _form.BeginInvoke(Check);
    }

    private void Check()
    {
        _queued = false;
        if (_disposed || _form.IsDisposed)
        {
            return;
        }

        bool hidden = ComputeHidden();
        if (hidden != _hidden)
        {
            _hidden = hidden;
            _changed(hidden);
        }
    }

    private bool ComputeHidden()
    {
        if (!_form.Visible || _form.WindowState == FormWindowState.Minimized || !_target.Visible
            || !_target.IsHandleCreated || IsCloaked(_form.Handle) || !InputDesktopIsOurs())
        {
            return true;
        }

        Rectangle area = _target.RectangleToScreen(_target.ClientRectangle);
        var probe = new Rect { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom };
        if (area.IsEmpty || MonitorFromRect(ref probe, MonitorDefaultToNull) == IntPtr.Zero)
        {
            return true;
        }

        var showing = new List<Rectangle> { area };
        uint ownProcess = (uint)Environment.ProcessId;

        // Every window above this one in the z-order, nearest first.
        for (IntPtr window = GetWindow(_form.Handle, GwHwndPrev); window != IntPtr.Zero; window = GetWindow(window, GwHwndPrev))
        {
            if (CoveringBounds(window, ownProcess) is not Rectangle cover)
            {
                continue;
            }

            showing = Subtract(showing, cover);
            if (showing.Count == 0)
            {
                return true;
            }

            // A pile of small windows chipping at the edges: not worth tracking, call it visible.
            if (showing.Count > 64)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>The screen area a window hides for certain, or null when it may let what is
    /// behind it show through.</summary>
    private static Rectangle? CoveringBounds(IntPtr window, uint ownProcess)
    {
        if (!IsWindowVisible(window) || IsIconic(window) || IsCloaked(window))
        {
            return null;
        }

        // This tool's own popups sit on the window for a moment and are not worth pausing for.
        GetWindowThreadProcessId(window, out uint process);
        if (process == ownProcess)
        {
            return null;
        }

        // Click-through, or drawn by DirectComposition (no redirection bitmap): either may be
        // clear where it sits.
        long style = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        if ((style & (WsExTransparent | WsExNoRedirectionBitmap)) != 0)
        {
            return null;
        }

        // A window cut to a shape of its own covers less than its rectangle.
        if (GetWindowRgnBox(window, out _) != RegionError)
        {
            return null;
        }

        if ((style & WsExLayered) != 0)
        {
            // Per-pixel transparency (UpdateLayeredWindow) has no attributes to read and may be
            // clear anywhere; a colour key punches holes; anything below full alpha is see-through.
            if (!GetLayeredWindowAttributes(window, out _, out byte alpha, out uint flags)
                || (flags & LwaColorKey) != 0 || ((flags & LwaAlpha) != 0 && alpha < 255))
            {
                return null;
            }
        }

        // The frame as drawn, without the invisible resize border Windows 10 and later add
        // around it - that border would otherwise "cover" a strip of whatever sits beside it.
        if (DwmGetWindowAttribute(window, DwmwaExtendedFrameBounds, out Rect frame, Marshal.SizeOf<Rect>()) != 0
            && !GetWindowRect(window, out frame))
        {
            return null;
        }

        var bounds = Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
        return bounds.IsEmpty ? null : bounds;
    }

#if REVIEW
    /// <summary>Every window above this one with what it was taken for, for <c>-review perf</c>.</summary>
    internal string DescribeForReview()
    {
        var lines = new List<string>
        {
            $"target {_target.RectangleToScreen(_target.ClientRectangle)}  hidden {ComputeHidden()}  input desktop {InputDesktopIsOurs()}",
        };
        uint ownProcess = (uint)Environment.ProcessId;
        int count = 0;
        for (IntPtr window = GetWindow(_form.Handle, GwHwndPrev); window != IntPtr.Zero && count < 40; window = GetWindow(window, GwHwndPrev), count++)
        {
            GetWindowThreadProcessId(window, out uint process);
            lines.Add($"  {window:X} pid {process} visible {IsWindowVisible(window)} cover {CoveringBounds(window, ownProcess)?.ToString() ?? "-"}");
        }

        lines.Add($"below (form topmost {_form.TopMost}):");
        count = 0;
        for (IntPtr window = GetWindow(_form.Handle, GwHwndNext); window != IntPtr.Zero && count < 12; window = GetWindow(window, GwHwndNext), count++)
        {
            GetWindowThreadProcessId(window, out uint process);
            lines.Add($"  {window:X} pid {process} visible {IsWindowVisible(window)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private const uint GwHwndNext = 2;

    /// <summary>Every top-level window of one process with its state, for <c>-review perf</c>.</summary>
    internal static string DescribeProcessWindows(int processId)
    {
        var lines = new List<string>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint process);
            if (process == processId)
            {
                GetWindowRect(window, out Rect rect);
                long style = GetWindowLongPtr(window, GwlExStyle).ToInt64();
                lines.Add($"  {window:X} visible {IsWindowVisible(window)} cloaked {IsCloaked(window)} exstyle {style:X} "
                    + $"rect {rect.Left},{rect.Top},{rect.Right},{rect.Bottom} cover {CoveringBounds(window, 0)?.ToString() ?? "-"}");
            }

            return true;
        }, IntPtr.Zero);
        return string.Join(Environment.NewLine, lines);
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr parameter);
#endif

    private static List<Rectangle> Subtract(List<Rectangle> pieces, Rectangle cover)
    {
        var left = new List<Rectangle>(pieces.Count + 4);
        foreach (Rectangle piece in pieces)
        {
            if (!piece.IntersectsWith(cover))
            {
                left.Add(piece);
                continue;
            }

            if (cover.Top > piece.Top)
            {
                left.Add(Rectangle.FromLTRB(piece.Left, piece.Top, piece.Right, cover.Top));
            }

            if (cover.Bottom < piece.Bottom)
            {
                left.Add(Rectangle.FromLTRB(piece.Left, cover.Bottom, piece.Right, piece.Bottom));
            }

            int top = Math.Max(piece.Top, cover.Top);
            int bottom = Math.Min(piece.Bottom, cover.Bottom);

            if (cover.Left > piece.Left)
            {
                left.Add(Rectangle.FromLTRB(piece.Left, top, cover.Left, bottom));
            }

            if (cover.Right < piece.Right)
            {
                left.Add(Rectangle.FromLTRB(cover.Right, top, piece.Right, bottom));
            }
        }

        return left;
    }

    /// <summary>False while the lock screen or the secure desktop of a UAC prompt is up: nothing
    /// of this session's desktop is on screen then.</summary>
    private static bool InputDesktopIsOurs()
    {
        IntPtr desktop = OpenInputDesktop(0, false, DesktopSwitchDesktop);
        if (desktop == IntPtr.Zero)
        {
            return false;
        }

        CloseDesktop(desktop);
        return true;
    }

    private static bool IsCloaked(IntPtr window) =>
        DwmGetWindowAttribute(window, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private delegate void WinEventProc(IntPtr hook, uint winEvent, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectReorder = 0x8004;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint EventObjectCloaked = 0x8017;
    private const uint EventObjectUncloaked = 0x8018;
    private const uint HookFlags = 0x0000 | 0x0002; // WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS
    private const int ObjidWindow = 0;
    private const uint GwHwndPrev = 3;
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExLayered = 0x00080000;
    private const long WsExNoRedirectionBitmap = 0x00200000;
    private const int RegionError = 0;
    private const uint DesktopSwitchDesktop = 0x0100;
    private const uint LwaColorKey = 0x1;
    private const uint LwaAlpha = 0x2;
    private const uint MonitorDefaultToNull = 0;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint key, out byte alpha, out uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetWindowRgnBox(IntPtr window, out Rect box);

    [DllImport("user32.dll")]
    private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
