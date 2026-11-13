using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace AuraToggle.Tests;

/// <summary>
/// A controller that is a file: every report the app writes lands in it, and the tests read the
/// reports back in the order they were sent. No hardware, no HID driver, no window.
/// </summary>
internal sealed class FakeController : IDisposable
{
    public const int ReportLength = 65;

    /// <summary>A mainboard controller id, the command set 0x35/0x36.</summary>
    public const ushort MainboardPid = 0x18F3;

    /// <summary>An addressable controller id, the command set 0x3B.</summary>
    public const ushort AddressablePid = 0x1872;

    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "fakehid");

    private readonly string _folder;
    private readonly string _file;

    private FakeController(string key, ushort pid, int onboardLeds, int headers, string product, bool refuseWrites)
    {
        _folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _file = Path.Combine(_folder, "reports.bin");

        // Created first so the read-only handle of a refusing controller has something to open.
        File.WriteAllBytes(_file, Array.Empty<byte>());

        SafeFileHandle handle = refuseWrites
            ? File.OpenHandle(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.Asynchronous)
            : File.OpenHandle(_file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite,
                FileOptions.Asynchronous);

        var info = new HidInfo(key, pid, 0xFF72, ReportLength, ReportLength, product);
        var table = new byte[60];
        table[0x02] = (byte)headers;
        table[0x1B] = (byte)onboardLeds;

        Device = new AuraDevice(new HidStream(handle, info), table) { Name = product };
    }

    /// <summary>A mainboard controller: the onboard zone as channel 0 (when it has LEDs) and the headers after it.</summary>
    public static FakeController Mainboard(string key, int headers = 3, int onboardLeds = 4,
        string product = "Mainboard", bool refuseWrites = false, ushort pid = MainboardPid) =>
        new(key, pid, onboardLeds, headers, product, refuseWrites);

    /// <summary>An addressable controller: no fixed zone, header 1 is channel 0.</summary>
    public static FakeController Addressable(string key, int headers = 2, string product = "Addressable",
        bool refuseWrites = false, ushort pid = AddressablePid, int onboardLeds = 0) =>
        new(key, pid, onboardLeds, headers, product, refuseWrites);

    public AuraDevice Device { get; }

    /// <summary>Every report written so far, whole and in order.</summary>
    public List<byte[]> Reports()
    {
        byte[] all;
        using (var read = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            all = new byte[read.Length];
            read.ReadExactly(all);
        }

        var reports = new List<byte[]>();
        for (int at = 0; at + ReportLength <= all.Length; at += ReportLength)
        {
            reports.Add(all.AsSpan(at, ReportLength).ToArray());
        }

        return reports;
    }

    public void Dispose()
    {
        Device.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
