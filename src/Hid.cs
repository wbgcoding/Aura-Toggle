using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace AuraToggle;

internal sealed record HidInfo(
    string Path,
    ushort Pid,
    ushort UsagePage,
    int InputReportLength,
    int OutputReportLength,
    string Product);

/// <summary>Minimal HID access over setupapi.dll / hid.dll. No third-party dependencies.</summary>
internal static class Hid
{
    private const int DIGCF_PRESENT = 0x02;
    private const int DIGCF_DEVICEINTERFACE = 0x10;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x01;
    private const uint FILE_SHARE_WRITE = 0x02;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const int ERROR_SHARING_VIOLATION = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr hwnd, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, int index,
        ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data,
        IntPtr detail, int detailSize, ref int required, IntPtr devInfoData);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll")]
    private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll")]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsed);

    [DllImport("hid.dll")]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsed);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsed, IntPtr caps);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    private static extern bool HidD_GetProductString(SafeFileHandle handle, StringBuilder buffer, int bufferLengthBytes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    /// <summary>All present HID interfaces whose vendor/product id passes <paramref name="match"/>.</summary>
    public static List<HidInfo> Enumerate(Func<ushort, ushort, bool> match)
    {
        var result = new List<HidInfo>();
        HidD_GetHidGuid(out Guid hidGuid);

        IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
        {
            throw new IOException("SetupDiGetClassDevs failed: " + Marshal.GetLastWin32Error());
        }

        try
        {
            var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };

            for (int index = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref iface); index++)
            {
                int required = 0;
                SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, ref required, IntPtr.Zero);
                if (required <= 0)
                {
                    continue;
                }

                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W, not of the whole buffer.
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, required, ref required, IntPtr.Zero))
                    {
                        continue;
                    }

                    string path = Marshal.PtrToStringUni(detail + 4) ?? "";
                    HidInfo? info = Describe(path, match);
                    if (info != null)
                    {
                        result.Add(info);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    private static HidInfo? Describe(string path, Func<ushort, ushort, bool> match)
    {
        // Opened without access rights: works even while another process holds the device exclusively.
        using SafeFileHandle probe = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
            OPEN_EXISTING, 0, IntPtr.Zero);
        if (probe.IsInvalid)
        {
            return null;
        }

        var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(probe, ref attributes) || !match(attributes.VendorID, attributes.ProductID))
        {
            return null;
        }

        if (!HidD_GetPreparsedData(probe, out IntPtr preparsed))
        {
            return null;
        }

        // caps is allocated inside the try, not before it: preparsed is already a live unmanaged
        // handle at this point, so it needs the same finally to free it even if AllocHGlobal itself
        // is what throws.
        IntPtr caps = IntPtr.Zero;
        try
        {
            caps = Marshal.AllocHGlobal(256);
            if (HidP_GetCaps(preparsed, caps) != 0x00110000) // HIDP_STATUS_SUCCESS
            {
                return null;
            }

            // Best effort: not every device exposes a product string, and that is fine - the
            // caller falls back to a generic name built from the product id.
            var productBuilder = new StringBuilder(126);
            string product = HidD_GetProductString(probe, productBuilder, productBuilder.Capacity * 2)
                ? productBuilder.ToString().Trim()
                : "";

            return new HidInfo(
                path,
                attributes.ProductID,
                UsagePage: (ushort)Marshal.ReadInt16(caps, 2),
                InputReportLength: (ushort)Marshal.ReadInt16(caps, 4),
                OutputReportLength: (ushort)Marshal.ReadInt16(caps, 6),
                Product: product);
        }
        finally
        {
            if (caps != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(caps);
            }

            HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>Opens a HID interface for reading and writing reports.</summary>
    public static HidStream Open(HidInfo info)
    {
        SafeFileHandle handle = CreateFile(info.Path, GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();

            // Only a real sharing violation means another process already has this interface open.
            // Anything else - the device unplugged between enumeration and here, above all - is not
            // "busy", and reporting it that way sends the user looking for a program to close that
            // was never there.
            if (error != ERROR_SHARING_VIOLATION)
            {
                throw new IOException($"Cannot open the LED controller (Windows error {error}).");
            }

            throw new HidAccessException(error);
        }

        try
        {
            return new HidStream(handle, info);
        }
        catch
        {
            // Without this the handle would only come back at the next finalizer pass, leaving
            // the controller open and the next discovery locked out of it.
            handle.Dispose();
            throw;
        }
    }
}

internal sealed class HidAccessException : IOException
{
    public HidAccessException(int win32Error)
        : base($"Cannot open the LED controller (Windows error {win32Error}).")
    {
    }
}

internal sealed class HidStream : IDisposable
{
    /// <summary>
    /// A report is a few dozen bytes to a device on the local bus, so this is not a budget but a
    /// backstop: a controller that never completes the write would otherwise park the switching
    /// thread for good, leaving the window permanently busy.
    /// </summary>
    private const int WriteTimeoutMs = 3000;

    private readonly FileStream _stream;

    /// <summary>
    /// Set once a read has timed out. The abandoned read still owns the caller's buffer and may
    /// yet consume the next report, so this stream cannot be trusted for further exchanges.
    /// </summary>
    private bool _readAbandoned;

    /// <summary>
    /// Set once a write has timed out. The abandoned write still owns the stream and the
    /// caller's buffer, so starting a second WriteAsync on the same stream while it is still
    /// pending would race it - the same reasoning <see cref="_readAbandoned"/> exists for.
    /// </summary>
    private bool _writeAbandoned;

    public HidStream(SafeFileHandle handle, HidInfo info)
    {
        Info = info;
        // bufferSize 0: every report must hit the device unbuffered and at its exact length.
        _stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
    }

    public HidInfo Info { get; }

    /// <summary>Writes one output report. The buffer must start with the report id.</summary>
    public void Write(byte[] report)
    {
        if (_writeAbandoned)
        {
            // A previous write is still in flight on this stream; starting another would race it.
            AuraLog.Warn("Hid: write refused, an earlier write on this stream never completed");
            throw new IOException(Strings.ErrorWriteBusy);
        }

        var cancel = new CancellationTokenSource();
        Task write = _stream.WriteAsync(report, 0, report.Length, cancel.Token);

        // Disposed exactly once, whenever the write itself finally settles - the normal case
        // (already scheduled here, runs almost immediately) and the abandoned-on-timeout case
        // (runs later, whenever the controller's answer or the cancellation actually lands) alike.
        // A second, conditional dispose used to sit in a finally below for the normal case, which
        // could race this same continuation for the rare write that completes at the exact moment
        // the timeout below gives up on it.
        _ = write.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);

        try
        {
            if (!write.Wait(WriteTimeoutMs))
            {
                cancel.Cancel();
                _writeAbandoned = true;

                // The controller stopped acknowledging writes. Logged here rather than only where
                // it surfaces, because this stream is now abandoned for the rest of its life and
                // every later call on it fails for a different-looking reason.
                AuraLog.Warn($"Hid: write timed out after {WriteTimeoutMs} ms, stream abandoned " +
                    $"(report {report[1]:X2}, {report.Length} bytes)");
                throw new IOException(Strings.ErrorWriteTimeout);
            }

            write.GetAwaiter().GetResult();
            _stream.Flush();
        }
        catch (AggregateException ex)
        {
            throw new IOException(Strings.ErrorWriteGeneric, ex.InnerException ?? ex);
        }
    }

    /// <summary>Reads one input report. Returns false on timeout or on a device level error.</summary>
    public bool Read(byte[] buffer, int timeoutMs)
    {
        if (_readAbandoned)
        {
            // Reading again would race the previous, still pending read for the same buffer.
            return false;
        }

        var cancel = new CancellationTokenSource();
        Task<int> read = _stream.ReadAsync(buffer, 0, buffer.Length, cancel.Token);

        // Disposed exactly once, whenever the read itself finally settles - see the identical
        // comment in Write() above for why this replaced a second, conditional dispose that used
        // to sit in a finally below.
        _ = read.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);

        try
        {
            if (!read.Wait(timeoutMs))
            {
                cancel.Cancel();
                _readAbandoned = true;
                return false;
            }

            return read.Result > 0;
        }
        catch (AggregateException)
        {
            // A device that disappears mid-read faults the task. Treat it as no answer;
            // the caller decides what that means.
            return false;
        }
    }

    public void Dispose() => _stream.Dispose();
}
