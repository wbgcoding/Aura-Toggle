using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace AuraToggle.Tests;

/// <summary>
/// Base for the stored-file tests. On top of an empty folder it forgets which files the app
/// last saw as damaged: that memory is static, and a mark left by one test would make the next
/// test's first save keep a copy it knows nothing about.
/// </summary>
public abstract class PersistenceTest : DataFolderTest, IDisposable
{
    private static readonly string[] StoredNames =
    {
        AuraState.FileName, AuraSettings.FileName, AuraChannelNames.FileName,
        AuraChannelStates.FileName, AuraCustomPresets.FileName,
    };

    protected PersistenceTest() => ForgetDamage();

    public new void Dispose()
    {
        ForgetDamage();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Reading a name whose file is missing clears the app's damaged mark for it.</summary>
    private static void ForgetDamage()
    {
        TestData.Clear();
        foreach (string name in StoredNames)
        {
            AuraFiles.Read(name, JsonValueKind.Object)?.Dispose();
        }
    }

    /// <summary>Every file in the data folder that is a left-over temporary one.</summary>
    protected static string[] TempFiles() =>
        Directory.GetFiles(AuraFiles.Folder, "*.tmp", SearchOption.AllDirectories);

    /// <summary>Every <c>&lt;stem&gt;.bad-*.json</c> copy in the data folder, oldest name first.</summary>
    protected static string[] BadCopies(string stem) =>
        Directory.GetFiles(AuraFiles.Folder, stem + ".bad-*.json")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Parses JSON text into an element that stays usable without a document to dispose.</summary>
    protected static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>The current thread's culture is swapped for the duration of a body, then put back.</summary>
    protected static void WithCulture(string name, Action body)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}

/// <summary>
/// Holds a lock on its own thread until released: a mutex belongs to the thread that took it, so
/// it has to be released there as well.
/// </summary>
internal sealed class LockHolder : IDisposable
{
    private readonly ManualResetEventSlim _release = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;

    /// <summary>How many times the holder takes the lock before it reports ready.</summary>
    public LockHolder(Func<IDisposable?> acquire, int times = 1)
    {
        _thread = new Thread(() =>
        {
            var held = new List<IDisposable?>();
            for (int i = 0; i < times; i++)
            {
                held.Add(acquire());
            }

            _ready.Set();
            _release.Wait();
            foreach (IDisposable? lockHeld in held)
            {
                lockHeld?.Dispose();
            }
        })
        {
            IsBackground = true,
        };
        _thread.Start();
        Assert.True(_ready.Wait(TimeSpan.FromSeconds(60)), "the lock was never taken");
    }

    /// <summary>Gives every hold back, on the thread that took them.</summary>
    public void Dispose()
    {
        _release.Set();
        _thread.Join(TimeSpan.FromSeconds(60));
        _release.Dispose();
        _ready.Dispose();
    }
}

internal static class Poll
{
    /// <summary>Waits for a condition by polling, never by sleeping a fixed time.</summary>
    public static bool Until(Func<bool> condition, int timeoutMs = 20000)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > end)
            {
                return false;
            }

            Thread.Sleep(2);
        }

        return true;
    }
}
