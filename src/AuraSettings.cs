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

    public static readonly AuraSettings Default = new(
        StartMinimised: false,
        MinimiseOnClose: false,
        StartAction: StartActionNone,
        Animate: true,
        Language: LanguageAuto);

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "AuraToggle";

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "aura-toggle", "settings.json");

    public static AuraSettings Load()
    {
        string path = FilePath;
        if (!File.Exists(path))
        {
            return Default;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            JsonElement root = document.RootElement;

            return new AuraSettings(
                StartMinimised: Flag(root, "startMinimised", Default.StartMinimised),
                MinimiseOnClose: Flag(root, "minimiseOnClose", Default.MinimiseOnClose),
                StartAction: Text(root, "startAction", StartActionNone),
                Animate: Flag(root, "animate", Default.Animate),
                Language: Text(root, "language", LanguageAuto));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged or unreadable settings file must not stop the tool from switching lights.
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

    public void Save()
    {
        try
        {
            SaveCore();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void SaveCore()
    {
        string path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string json = "{" +
            $"\"startMinimised\":{(StartMinimised ? "true" : "false")}," +
            $"\"minimiseOnClose\":{(MinimiseOnClose ? "true" : "false")}," +
            $"\"startAction\":{JsonSerializer.Serialize(StartAction)}," +
            $"\"animate\":{(Animate ? "true" : "false")}," +
            $"\"language\":{JsonSerializer.Serialize(Language)}" +
            "}";

        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

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
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value && Environment.ProcessPath is string exe)
                {
                    key.SetValue(RunValue, $"\"{exe}\"");
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
