using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AuraToggle;

/// <summary>
/// Best-effort text log at <c>%LOCALAPPDATA%\aura-toggle\log.txt</c>: start, version, and errors
/// only. A failed write is silently dropped - a switch must never fail because logging did.
/// </summary>
internal static class AuraLog
{
    private const string LogName = "log.txt";
    private const string OldLogName = "log.old.txt";
    private const long RotateAtBytes = 200 * 1024;

    public static void Info(string message) => Write("INFO", message);

    /// <summary>Something worth knowing about when a report comes in, but not a failure.</summary>
    public static void Warn(string message) => Write("WARN", message);

    /// <summary>
    /// The exception's type and its inner exception go in as well as the message. A bare
    /// "Das Handle ist ungültig" says nothing about what failed; the type and the inner cause are
    /// usually the whole answer, and this file is meant to be pasted into a bug report.
    /// </summary>
    public static void Error(string context, Exception ex)
    {
        var text = $"{context}: {ex.GetType().Name}: {ex.Message}";

        for (Exception? inner = ex.InnerException; inner != null; inner = inner.InnerException)
        {
            text += $" <- {inner.GetType().Name}: {inner.Message}";
        }

        Write("ERROR", text);
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(AuraFiles.Folder);

            string path = AuraFiles.PathTo(LogName);
            if (File.Exists(path) && new FileInfo(path).Length > RotateAtBytes)
            {
                File.Move(path, AuraFiles.PathTo(OldLogName), overwrite: true);
            }

            // Invariant: the separators in a custom format string are the culture's own, so the
            // same log would read "14:03:27" here and "14.03.27" on a machine set to another
            // language - and this file is meant to be pasted into an issue and compared.
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string line = $"{stamp}  {level}  {AuraFiles.Redact(message)}{Environment.NewLine}";
            File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (AuraFiles.IsExpected(ex))
        {
        }
    }
}
