using System;
using System.Linq;
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
    bool CloseAfterStart,
    bool Animate,
    bool AlwaysOnTop,
    string Language,
    bool HotkeyEnabled,
    int Hotkey,
    int? WindowX,
    int? WindowY,
    int? WindowWidth,
    bool CheckUpdates,
    string LastUpdateCheckUtc,
    string UpdateNoticeVersion)
{
    /// <summary>Leave the lighting untouched when the tool starts.</summary>
    public const string StartActionNone = "";

    /// <summary>Switch the lighting off when the tool starts.</summary>
    public const string StartActionOff = "off";

    /// <summary>
    /// Start action prefix for a custom preset, followed by the preset's exact name. Built-in
    /// effect keys never contain a colon, so the two can never be mistaken for each other.
    /// </summary>
    public const string StartActionCustomPrefix = "custom:";

    public static string CustomStartAction(string presetName) => StartActionCustomPrefix + presetName;

    /// <summary>The custom preset a start action names, or null for any other action.</summary>
    public static string? CustomStartPreset(string action) =>
        action.StartsWith(StartActionCustomPrefix, StringComparison.Ordinal)
            ? action[StartActionCustomPrefix.Length..]
            : null;

    /// <summary>Follow the Windows display language.</summary>
    public const string LanguageAuto = "";

    public static readonly AuraSettings Default = new(
        MinimiseOnClose: false,
        StartAction: StartActionNone,
        CloseAfterStart: false,
        Animate: true,
        AlwaysOnTop: false,
        Language: LanguageAuto,
        HotkeyEnabled: false,
        Hotkey: HotKey.Default,
        WindowX: null,
        WindowY: null,
        WindowWidth: null,
        CheckUpdates: true,
        LastUpdateCheckUtc: "",
        UpdateNoticeVersion: "");

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
                StartAction: ValidStartAction(AuraFiles.JsonText(root, "startAction", StartActionNone)),
                CloseAfterStart: AuraFiles.JsonFlag(root, "closeAfterStart", Default.CloseAfterStart),
                Animate: AuraFiles.JsonFlag(root, "animate", Default.Animate),
                AlwaysOnTop: AuraFiles.JsonFlag(root, "alwaysOnTop", Default.AlwaysOnTop),
                Language: AuraFiles.JsonText(root, "language", LanguageAuto),
                HotkeyEnabled: AuraFiles.JsonFlag(root, "hotkeyEnabled", Default.HotkeyEnabled),
                Hotkey: ValidHotkey(AuraFiles.JsonNumber(root, "hotkey", Default.Hotkey)),
                WindowX: AuraFiles.JsonNumberOrNull(root, "windowX"),
                WindowY: AuraFiles.JsonNumberOrNull(root, "windowY"),
                WindowWidth: AuraFiles.JsonNumberOrNull(root, "windowWidth"),
                CheckUpdates: AuraFiles.JsonFlag(root, "checkUpdates", Default.CheckUpdates),
                LastUpdateCheckUtc: AuraFiles.JsonText(root, "lastUpdateCheckUtc"),
                UpdateNoticeVersion: AuraFiles.JsonText(root, "updateNoticeVersion"));
        }
        catch (Exception ex) when (AuraFiles.IsExpected(ex))
        {
            return Default;
        }
    }

    /// <summary>
    /// The start action is applied at logon without anyone looking, so only what the settings
    /// panel could have stored counts: nothing, off, the exact key of a built-in effect, or a
    /// custom preset by name. Anything else (a typo, an old or hand-edited value) reads as
    /// "leave the lighting alone" instead of quietly matching a nearby effect by forgiving name.
    /// </summary>
    private static string ValidStartAction(string action) =>
        action == StartActionNone || action == StartActionOff ||
        action.StartsWith(StartActionCustomPrefix, StringComparison.Ordinal) ||
        AuraPresets.All.Any(preset => preset.Key == action)
            ? action
            : StartActionNone;

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
        writer.WriteBoolean("closeAfterStart", CloseAfterStart);
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

        writer.WriteBoolean("checkUpdates", CheckUpdates);
        writer.WriteString("lastUpdateCheckUtc", LastUpdateCheckUtc);
        writer.WriteString("updateNoticeVersion", UpdateNoticeVersion);
        writer.WriteEndObject();
    });

    /// <summary>
    /// Keeps a custom preset chosen as the start action pointing at the right preset: a rename
    /// carries the new name over, a delete (<paramref name="newName"/> null) falls back to leaving
    /// the lighting alone. Without this a renamed preset silently stopped applying at logon.
    /// </summary>
    public static void FollowPresetRename(string oldName, string? newName)
    {
        if (oldName == newName)
        {
            return;
        }

        using IDisposable guard = AuraFiles.Lock();

        AuraSettings settings = Load();
        if (CustomStartPreset(settings.StartAction) != oldName)
        {
            return;
        }

        (settings with { StartAction = newName == null ? StartActionNone : CustomStartAction(newName) }).Save();
    }

#if REVIEW
    private static bool _reviewAutoStart = true;
#endif

    /// <summary>
    /// Whether Windows starts the tool at logon. Backed by the per-user Run key, so no
    /// administrator rights and no scheduled task are involved, and removing it is one click.
    /// </summary>
    public static bool AutoStart
    {
        get
        {
#if REVIEW
            if (AuraFiles.ReviewStore)
            {
                return _reviewAutoStart;
            }
#endif

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);

                // A portable copy moved or deleted after the Run entry was written leaves the
                // entry itself behind - Windows starts nothing from it, but the old check read
                // "on" from its mere presence anyway. Compared against the path actually running
                // now instead: only a value this copy itself could have written counts as "on".
                if (key?.GetValue(RunValue) is not string value || Environment.ProcessPath is not string exe)
                {
                    return false;
                }

                // Hand-edited values do not start with a quote the way this type's own setter
                // below always writes one - treated as belonging to someone else, not matched.
                if (value.Length == 0 || value[0] != '"')
                {
                    return false;
                }

                int closingQuote = value.IndexOf('"', 1);
                if (closingQuote < 0)
                {
                    return false;
                }

                return string.Equals(value[1..closingQuote], exe, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (AuraFiles.IsExpected(ex))
            {
                return false;
            }
        }

        set
        {
#if REVIEW
            // The surface tool's switch flips for real but never reaches the registry.
            if (AuraFiles.ReviewStore)
            {
                _reviewAutoStart = value;
                return;
            }
#endif

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
