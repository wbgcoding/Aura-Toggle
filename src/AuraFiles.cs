using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace AuraToggle;

/// <summary>
/// The one place that touches <c>%LOCALAPPDATA%\aura-toggle</c>. Every stored file goes through
/// here so that reading a damaged one, writing one, and doing both at once from two processes
/// all behave the same way.
/// </summary>
internal static class AuraFiles
{
    /// <summary>
    /// Long enough that a slow disk still gets its turn, short enough that a wedged process
    /// cannot hang a switch: losing one update is better than never switching the lights again.
    /// </summary>
    private const int LockTimeoutMs = 2000;

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "aura-toggle");

    public static string PathTo(string name) => Path.Combine(Folder, name);

    /// <summary>
    /// Everything a missing, damaged, locked or hostile file can throw. Note
    /// <see cref="InvalidOperationException"/>: <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/>
    /// and the Enumerate methods throw it when the JSON parses but holds the wrong kind, which
    /// is exactly what a hand-edited file looks like.
    /// </summary>
    public static bool IsExpected(Exception ex) =>
        ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException
            or NotSupportedException or System.Security.SecurityException or ArgumentException;

    /// <summary>
    /// Parses a stored file, or returns null when it is missing, unreadable, not valid JSON, or
    /// does not hold <paramref name="expectedRoot"/> at its root. The caller disposes.
    /// </summary>
    public static JsonDocument? Read(string name, JsonValueKind expectedRoot)
    {
        try
        {
            string path = PathTo(name);
            if (!File.Exists(path))
            {
                return null;
            }

            JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (document.RootElement.ValueKind == expectedRoot)
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Writes JSON through a temporary file and moves it into place, so a crash or a full disk
    /// mid-write leaves the previous file intact instead of an empty or half-written one.
    /// </summary>
    public static void Write(string name, Action<Utf8JsonWriter> body)
    {
        WriteRaw(name, stream =>
        {
            using var writer = new Utf8JsonWriter(stream);
            body(writer);
        });
    }

    /// <summary>Same guarantee for the two files that are assembled as text.</summary>
    public static void WriteText(string name, string content)
    {
        WriteRaw(name, stream =>
        {
            byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        });
    }

    private static void WriteRaw(string name, Action<FileStream> body)
    {
        try
        {
            Directory.CreateDirectory(Folder);

            string path = PathTo(name);
            string temp = path + ".tmp";

            using (FileStream stream = File.Create(temp))
            {
                body(stream);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            // Failing to store a preference is not worth aborting a switch that already reached
            // the hardware.
        }
    }

    /// <summary>
    /// Held around a read-modify-write, so the window and a command line invocation running at
    /// the same time cannot each read, change and save a different copy of the same file.
    /// </summary>
    public static IDisposable Lock() => new FileLock();

    private sealed class FileLock : IDisposable
    {
        private readonly Mutex? _mutex;
        private readonly bool _held;

        public FileLock()
        {
            try
            {
                _mutex = new Mutex(initiallyOwned: false, "AuraToggle.Files");
                _held = _mutex.WaitOne(LockTimeoutMs);
            }
            catch (AbandonedMutexException)
            {
                // The previous owner died holding it. The files are still consistent, because
                // every write is atomic; carry on as the new owner.
                _held = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or System.Security.SecurityException or NotSupportedException)
            {
                _mutex = null;
            }
        }

        public void Dispose()
        {
            if (_mutex == null)
            {
                return;
            }

            if (_held)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
            }

            _mutex.Dispose();
        }
    }
}
