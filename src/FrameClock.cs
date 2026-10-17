using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AuraToggle;

/// <summary>
/// Ticks an animation about 60 times a second, in step with the display. A WinForms timer only
/// fires on the system's 15.6 ms clock, so its 33 ms asked for 30 fps and delivered 21 - the
/// stutter in the moving effects. A background thread waits on a high-resolution waitable timer
/// and posts the tick to the UI thread; a tick still waiting there is never posted twice, so a
/// busy UI thread drops frames instead of queuing them.
/// </summary>
/// <remarks>
/// Each tick is placed just after a vertical blank, read from the compositor's timing
/// (<c>DwmGetCompositionTimingInfo</c>). A free-running 60 Hz clock beside a 60 Hz screen
/// drifts against it: every few seconds two ticks fell into one screen frame and the next frame
/// got none, a repeated picture that showed as a hitch in the smoothly scrolling rainbow. On a
/// faster screen it ticks on every second or third blank, whichever lands nearest 60 fps.
/// Waiting on <c>DwmFlush</c> instead was tried and fell to 4 fps on a display that composes
/// rarely; without usable timing the clock runs free at 60 fps.
/// </remarks>
internal sealed class FrameClock : IDisposable
{
    private const double FrameMs = 1000.0 / 60;

    /// <summary>How long after a blank the tick lands: late enough to be past it for certain,
    /// early enough to leave the rest of the frame for the paint.</summary>
    private const double AfterBlankMs = 1.0;

    private readonly Control _owner;
    private readonly Action _tick;
    private readonly object _gate = new();
    private Thread? _thread;

    /// <summary>Whether the last tick was placed against the screen's blanks rather than running
    /// free - for the timing report, the one place that can tell the two apart.</summary>
    internal static bool Synced { get; private set; }

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
        double last = Now();
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

            double now = Now();
            double next;
            (double Blank, double Period)? timing = VBlank(now);
            Synced = timing != null;
            if (timing is (double blank, double period))
            {
                double step = Math.Max(1, Math.Round(FrameMs / period)) * period;

                // The first blank at least a step after the last tick (half a frame of slack, so
                // a tick that ran a little late does not skip a whole frame) and not in the past.
                double earliest = Math.Max(now, last + step - (period / 2));
                next = blank + AfterBlankMs + (Math.Ceiling((earliest - blank - AfterBlankMs) / period) * period);
            }
            else
            {
                next = last + FrameMs;

                // Fell well behind (the machine slept, the thread was starved): start over from
                // now instead of firing a burst of frames to catch up.
                if (next < now - FrameMs)
                {
                    next = now;
                }
            }

            if (next > now)
            {
                Wait(timer, next - now);
            }

            last = next;

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

    private static double Now() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// The time of the last vertical blank and the screen's refresh period, both in
    /// <see cref="Now"/>'s milliseconds, or null when the compositor has nothing believable.
    /// </summary>
    private static (double Blank, double Period)? VBlank(double now)
    {
        Span<byte> info = stackalloc byte[TimingInfoSize];
        info.Clear();
        BitConverter.TryWriteBytes(info, TimingInfoSize);
        if (DwmGetCompositionTimingInfo(IntPtr.Zero, ref MemoryMarshal.GetReference(info)) != 0)
        {
            return null;
        }

        // QPC ticks, the same counter Stopwatch reads.
        double toMs = 1000.0 / Stopwatch.Frequency;
        double period = BitConverter.ToUInt64(info[12..]) * toMs;
        double blank = BitConverter.ToUInt64(info[28..]) * toMs;

        // A blank from long ago would still give the right phase, but drift in the period adds
        // up over it; a period outside any real screen's means the numbers are not usable.
        if (period is < 2 or > 50 || blank > now + 100 || blank < now - 5000)
        {
            return null;
        }

        return (blank, period);
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

    /// <summary>sizeof(DWM_TIMING_INFO), which is byte-packed: cbSize at 0, qpcRefreshPeriod at
    /// 12, qpcVBlank at 28.</summary>
    private const int TimingInfoSize = 292;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(IntPtr window, ref byte info);

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
