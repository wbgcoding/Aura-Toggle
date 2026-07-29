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
    ushort Vid,
    ushort Pid,
    ushort UsagePage,
    ushort Usage,
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

        IntPtr caps = Marshal.AllocHGlobal(256);
        try
        {
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
                attributes.VendorID,
                attributes.ProductID,
                UsagePage: (ushort)Marshal.ReadInt16(caps, 2),
                Usage: (ushort)Marshal.ReadInt16(caps, 0),
                InputReportLength: (ushort)Marshal.ReadInt16(caps, 4),
                OutputReportLength: (ushort)Marshal.ReadInt16(caps, 6),
                Product: product);
        }
        finally
        {
            Marshal.FreeHGlobal(caps);
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
            throw new HidAccessException(error);
        }

        return new HidStream(handle, info);
    }
}

internal sealed class HidAccessException : IOException
{
    public HidAccessException(int win32Error)
        : base($"Cannot open the LED controller (Windows error {win32Error}).")
    {
        Win32Error = win32Error;
    }

    public int Win32Error { get; }
}

internal sealed class HidStream : IDisposable
{
    private readonly FileStream _stream;

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
        _stream.Write(report, 0, report.Length);
        _stream.Flush();
    }

    /// <summary>Reads one input report. Returns false on timeout or on a device level error.</summary>
    public bool Read(byte[] buffer, int timeoutMs)
    {
        var cancel = new CancellationTokenSource();
        Task<int> read = _stream.ReadAsync(buffer, 0, buffer.Length, cancel.Token);

        try
        {
            if (!read.Wait(timeoutMs))
            {
                cancel.Cancel();

                // The read is still in flight. Observing it keeps its failure from surfacing
                // later as an unobserved task exception, and disposes the token source once
                // nothing references it any more.
                _ = read.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);
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
        finally
        {
            if (read.IsCompleted)
            {
                cancel.Dispose();
            }
        }
    }

    public void Dispose() => _stream.Dispose();
}
