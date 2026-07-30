using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace AuraToggle;

/// <summary>
/// User preferences. Kept next to the remembered lighting state, in
/// <c>%LOCALAPPDATA%\aura-toggle\settings.json</c>.
/// </summary>
internal sealed record AuraSettings(
    bool StartMinimised,
    bool MinimiseOnClose,
    string StartAction,
    bool Animate,
    string Language)
{
    /// <summary>Leave the lighting untouched when the tool starts.</summary>
    public const string StartActionNone = "";

    /// <summary>Switch the lighting off when the tool starts.</summary>
    public const string StartActionOff = "off";

    /// <summary>Follow the Windows display language.</summary>
    public const string LanguageAuto = "";

    /// <summary>The per-device selector's default: every controller is switched together.</summary>
    public const string ChannelAll = "all";

    public static readonly AuraSettings Default = new(
        StartMinimised: false,
        MinimiseOnClose: false,
        StartAction: StartActionNone,
        Animate: true,
        Language: LanguageAuto);

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "AuraToggle";

    /// <summary>
    /// Passed by the Run key entry. Only a start by Windows may open straight into the
    /// notification area; starting the tool by hand always shows the window.
    /// </summary>
    public const string AutoStartArgument = "-autostart";

    private const string FileName = "settings.json";

    public static AuraSettings Load()
    {
        // A damaged or unreadable settings file must not stop the tool from switching lights -
        // and this runs before anything else in Main, so throwing here means never starting.
        using JsonDocument? document = AuraFiles.Read(FileName, JsonValueKind.Object);
        if (document == null)
        {
            return Default;
        }

        try
        {
            JsonElement root = document.RootElement;

            return new AuraSettings(
                StartMinimised: Flag(root, "startMinimised", Default.StartMinimised),
                MinimiseOnClose: Flag(root, "minimiseOnClose", Default.MinimiseOnClose),
                StartAction: Text(root, "startAction", StartActionNone),
                Animate: Flag(root, "animate", Default.Animate),
                Language: Text(root, "language", LanguageAuto));
        }
        catch (Exception ex) when (AuraFiles.IsExpected(ex))
        {
            return Default;
        }
    }

    private static string Text(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool Flag(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    public void Save() => AuraFiles.Write(FileName, writer =>
    {
        writer.WriteStartObject();
        writer.WriteBoolean("startMinimised", StartMinimised);
        writer.WriteBoolean("minimiseOnClose", MinimiseOnClose);
        writer.WriteString("startAction", StartAction);
        writer.WriteBoolean("animate", Animate);
        writer.WriteString("language", Language);
        writer.WriteEndObject();
    });

    /// <summary>
    /// Whether Windows starts the tool at logon. Backed by the per-user Run key, so no
    /// administrator rights and no scheduled task are involved, and removing it is one click.
    /// </summary>
    public static bool AutoStart
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunValue) != null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }
        }

        set
        {
            try
            {
                // Without a path to point the Run entry at there is nothing to write, and
                // falling through to DeleteValue would silently turn autostart off instead.
                if (value && Environment.ProcessPath == null)
                {
                    return;
                }

                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value && Environment.ProcessPath is string exe)
                {
                    key.SetValue(RunValue, $"\"{exe}\" {AutoStartArgument}");
                }
                else
                {
                    key.DeleteValue(RunValue, throwOnMissingValue: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A locked down machine may forbid the Run key. The rest of the tool works.
            }
        }
    }
}
