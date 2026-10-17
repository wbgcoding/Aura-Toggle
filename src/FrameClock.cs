using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// Ticks an animation 60 times a second, evenly. A WinForms timer only fires on the system's
/// 15.6 ms clock, so its 33 ms asked for 30 fps and delivered 21 - the stutter in the moving
/// effects. A background thread waits on a high-resolution waitable timer and posts the tick to
/// the UI thread; a tick still waiting there is never posted twice, so a busy UI thread drops
/// frames instead of queuing them. Waiting for the composed frame (<c>DwmFlush</c>) instead was
/// tried and fell to 4 fps on a display that composes rarely.
/// </summary>
internal sealed class FrameClock : IDisposable
{
    private const double FrameMs = 1000.0 / 60;

    private readonly Control _owner;
    private readonly Action _tick;
    private readonly object _gate = new();
    private Thread? _thread;
    private bool _running;
    private int _posted;

    public FrameClock(Control owner, Action tick)
    {
        _owner = owner;
        _tick = tick;
    }

    public bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
        set
        {
            lock (_gate)
            {
                _running = value;
                if (value && _thread == null)
                {
                    _thread = new Thread(Loop) { IsBackground = true, Name = "Frame clock" };
                    _thread.Start();
                }
            }
        }
    }

    public void Dispose() => Enabled = false;

    private void Loop()
    {
        // High resolution exists from Windows 10 1803 on; before that the plain timer still
        // beats the WinForms one, which is bound to the 15.6 ms clock on top of the message loop.
        IntPtr timer = CreateWaitableTimerExW(IntPtr.Zero, null, HighResolution, TimerAllAccess);
        if (timer == IntPtr.Zero)
        {
            timer = CreateWaitableTimerExW(IntPtr.Zero, null, 0, TimerAllAccess);
        }

        try
        {
            Run(timer);
        }
        finally
        {
            if (timer != IntPtr.Zero)
            {
                CloseHandle(timer);
            }
        }
    }

    private void Run(IntPtr timer)
    {
        // Paced against a running deadline rather than "a frame from now", so the time a tick
        // takes does not stretch every frame by that much.
        double next = Stopwatch.GetElapsedTime(0).TotalMilliseconds;
        while (true)
        {
            lock (_gate)
            {
                if (!_running)
                {
                    _thread = null;
                    return;
                }
            }

            next += FrameMs;
            double wait = next - Stopwatch.GetElapsedTime(0).TotalMilliseconds;
            if (wait < -FrameMs)
            {
                // Fell well behind (the machine slept, the thread was starved): start over from
                // now instead of firing a burst of frames to catch up.
                next = Stopwatch.GetElapsedTime(0).TotalMilliseconds;
            }
            else if (wait > 0)
            {
                Wait(timer, wait);
            }

            if (Interlocked.Exchange(ref _posted, 1) == 1)
            {
                continue;
            }

            try
            {
                if (_owner.IsHandleCreated && !_owner.IsDisposed)
                {
                    _owner.BeginInvoke(Tick);
                    continue;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // The handle went away between the check and the post.
            }

            Interlocked.Exchange(ref _posted, 0);
        }
    }

    private void Tick()
    {
        Interlocked.Exchange(ref _posted, 0);
        if (Enabled && !_owner.IsDisposed)
        {
            _tick();
        }
    }

    private static void Wait(IntPtr timer, double milliseconds)
    {
        long due = -(long)(milliseconds * 10_000); // relative, in 100 ns units
        if (timer != IntPtr.Zero && SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            WaitForSingleObject(timer, Infinite);
        }
        else
        {
            Thread.Sleep(Math.Max(1, (int)milliseconds));
        }
    }

    private const uint HighResolution = 0x00000002; // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
    private const uint TimerAllAccess = 0x001F0003;
    private const uint Infinite = 0xFFFFFFFF;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion,
        IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
