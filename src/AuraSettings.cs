using System;
using System.Text.Json;
using Microsoft.Win32;

namespace AuraToggle;

/// <summary>
/// User preferences. Kept next to the remembered lighting state, in
/// <c>%LOCALAPPDATA%\aura-toggle\settings.json</c>.
/// </summary>
internal sealed record AuraSettings(
    bool MinimiseOnClose,
    string StartAction,
    bool Animate,
    bool AlwaysOnTop,
    string Language,
    bool HotkeyEnabled,
    int Hotkey,
    int? WindowX,
    int? WindowY,
    int? WindowWidth)
{
    /// <summary>Leave the lighting untouched when the tool starts.</summary>
    public const string StartActionNone = "";

    /// <summary>Switch the lighting off when the tool starts.</summary>
    public const string StartActionOff = "off";

    /// <summary>Follow the Windows display language.</summary>
    public const string LanguageAuto = "";

    public static readonly AuraSettings Default = new(
        MinimiseOnClose: false,
        StartAction: StartActionNone,
        Animate: true,
        AlwaysOnTop: false,
        Language: LanguageAuto,
        HotkeyEnabled: false,
        Hotkey: HotKey.Default,
        WindowX: null,
        WindowY: null,
        WindowWidth: null);

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "AuraToggle";

    /// <summary>
    /// Passed by the Run key entry. Only a start by Windows may open straight into the
    /// notification area; starting the tool by hand always shows the window.
    /// </summary>
    public const string AutoStartArgument = "-autostart";

    internal const string FileName = "settings.json";

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

            // "startMinimised" from before autostart always went to the tray on its own is read
            // by nobody any more - an old file with the key just falls through to Default here.
            return new AuraSettings(
                MinimiseOnClose: AuraFiles.JsonFlag(root, "minimiseOnClose", Default.MinimiseOnClose),
                StartAction: AuraFiles.JsonText(root, "startAction", StartActionNone),
                Animate: AuraFiles.JsonFlag(root, "animate", Default.Animate),
                AlwaysOnTop: AuraFiles.JsonFlag(root, "alwaysOnTop", Default.AlwaysOnTop),
                Language: AuraFiles.JsonText(root, "language", LanguageAuto),
                HotkeyEnabled: AuraFiles.JsonFlag(root, "hotkeyEnabled", Default.HotkeyEnabled),
                Hotkey: ValidHotkey(AuraFiles.JsonNumber(root, "hotkey", Default.Hotkey)),
                WindowX: AuraFiles.JsonNumberOrNull(root, "windowX"),
                WindowY: AuraFiles.JsonNumberOrNull(root, "windowY"),
                WindowWidth: AuraFiles.JsonNumberOrNull(root, "windowWidth"));
        }
        catch (Exception ex) when (AuraFiles.IsExpected(ex))
        {
            return Default;
        }
    }

    /// <summary>
    /// A hand-edited or corrupted <c>settings.json</c> could carry a packed hotkey the recorder
    /// itself would never produce (<see cref="SettingsPopup"/> insists on at least one modifier
    /// and a real key). Registering a bare key globally would swallow ordinary typing system-wide,
    /// and registering no key at all fails outright and switches the setting back off with no
    /// explanation - so anything malformed falls back to the default combination instead.
    /// </summary>
    private static int ValidHotkey(int packed)
    {
        const int knownModifiers = HotKey.ModControl | HotKey.ModAlt | HotKey.ModShift | HotKey.ModWin;
        int modifiers = HotKey.Modifiers(packed) & knownModifiers;
        int key = HotKey.VirtualKey(packed);

        return modifiers == 0 || !HotKey.IsUsableKey(key) ? HotKey.Default : HotKey.Pack(modifiers, key);
    }

    public void Save() => AuraFiles.Write(FileName, writer =>
    {
        writer.WriteStartObject();
        writer.WriteBoolean("minimiseOnClose", MinimiseOnClose);
        writer.WriteString("startAction", StartAction);
        writer.WriteBoolean("animate", Animate);
        writer.WriteBoolean("alwaysOnTop", AlwaysOnTop);
        writer.WriteString("language", Language);
        writer.WriteBoolean("hotkeyEnabled", HotkeyEnabled);
        writer.WriteNumber("hotkey", Hotkey);

        if (WindowX.HasValue && WindowY.HasValue)
        {
            writer.WriteNumber("windowX", WindowX.Value);
            writer.WriteNumber("windowY", WindowY.Value);
        }

        if (WindowWidth.HasValue)
        {
            writer.WriteNumber("windowWidth", WindowWidth.Value);
        }

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
            catch (Exception ex) when (AuraFiles.IsExpected(ex))
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
            catch (Exception ex) when (AuraFiles.IsExpected(ex))
            {
                // A locked down machine may forbid the Run key. The rest of the tool works.
            }
        }
    }
}
