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

    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "aura-toggle");

    public static string PathTo(string name) => Path.Combine(Folder, name);

    /// <summary>The user's own profile folder, cached: read once rather than on every logged
    /// exception.</summary>
    private static readonly string ProfileFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Replaces the user's profile path with a placeholder wherever it appears - a stack trace
    /// or an I/O exception routinely carries the full path this ran under
    /// (<c>C:\Users\&lt;name&gt;\...</c>), and that text ends up both in <c>log.txt</c> and in
    /// the error dialog's "Copy details" button, which is meant for pasting into a public
    /// GitHub issue.
    /// </summary>
    public static string Redact(string text) => ProfileFolder.Length > 0
        ? text.Replace(ProfileFolder, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
        : text;

    /// <summary>
    /// The key a channel is stored/looked up under in <c>channel-names.json</c> and
    /// <c>channel-state.json</c> - both keyed the same way, by the device's HID path plus its
    /// channel index.
    /// </summary>
    public static string ChannelKey(string deviceKey, int channel) => $"{deviceKey}|{channel}";

    /// <summary>Opens the settings folder in Explorer, creating it first if nothing has written to it yet.</summary>
    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (IsExpected(ex) || ex is System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>
    /// Deletes every stored preference and remembered state, so the app comes back to first-run
    /// defaults on its next read without a restart. Custom presets are the user's own created
    /// content, not a preference with a default to fall back to, so they survive a reset - same
    /// reasoning as leaving the log out of this list. The log is deliberately not one of the
    /// four either - resetting is itself worth a line in it.
    /// </summary>
    public static void ResetAll()
    {
        using IDisposable held = Lock();

        foreach (string name in new[]
        {
            AuraState.FileName, AuraSettings.FileName,
            AuraChannelNames.FileName, AuraChannelStates.FileName,
        })
        {
            try
            {
                File.Delete(PathTo(name));
            }
            catch (Exception ex) when (IsExpected(ex))
            {
            }
        }
    }

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

    // The four element readers below are used by every kind-checked JSON file this tool has -
    // one place to get "wrong type in a hand-edited file falls back instead of throwing" right,
    // rather than the same four checks re-typed per file.

    public static string JsonText(JsonElement element, string name, string fallback = "") =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    public static byte JsonByte(JsonElement element, string name, byte fallback = 0) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetByte(out byte parsed) ? parsed : fallback;

    public static bool JsonFlag(JsonElement element, string name, bool fallback = false) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    public static int JsonNumber(JsonElement element, string name, int fallback = 0) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int number)
            ? number
            : fallback;

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

    private static void WriteRaw(string name, Action<FileStream> body)
    {
        string temp = PathTo(name) + ".tmp";

        try
        {
            Directory.CreateDirectory(Folder);

            using (FileStream stream = File.Create(temp))
            {
                body(stream);
            }

            File.Move(temp, PathTo(name), overwrite: true);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            // Failing to store a preference is not worth aborting a switch that already reached
            // the hardware - but the half-written temp file must not linger forever either.
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanupEx) when (IsExpected(cleanupEx))
            {
            }
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
            // "Global\" rather than a plain name: %LOCALAPPDATA% is the same folder for every
            // session the same account is logged into (a physical logon plus a Remote Desktop
            // one, say), but a plain named mutex lives in the caller's own Terminal Services
            // session - two sessions would each get their own lock and race the same files.
            // Falls back to the old session-local name if the Global one is denied outright
            // (rare, locked-down policy) rather than running with no lock at all.
            (Mutex, bool)? acquired = TryAcquire(@"Global\AuraToggle.Files") ?? TryAcquire("AuraToggle.Files");
            _mutex = acquired?.Item1;
            _held = acquired?.Item2 ?? false;
        }

        private static (Mutex, bool)? TryAcquire(string name)
        {
            Mutex? mutex = null;

            try
            {
                mutex = new Mutex(initiallyOwned: false, name);
                return (mutex, mutex.WaitOne(LockTimeoutMs));
            }
            catch (AbandonedMutexException)
            {
                // The previous owner died holding it. The files are still consistent, because
                // every write is atomic; carry on as the new owner. Taken from the handle opened
                // above rather than from the exception, whose own Mutex property is documented as
                // possibly null - dereferencing it would turn a recoverable case into a crash.
                return mutex == null ? null : (mutex, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or System.Security.SecurityException or NotSupportedException
                or WaitHandleCannotBeOpenedException)
            {
                mutex?.Dispose();
                return null;
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
