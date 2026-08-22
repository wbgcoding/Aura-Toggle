using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;

namespace AuraToggle;

/// <summary>
/// User facing text in the ten interface languages. The language follows the Windows display
/// language, English is the fallback for everything else.
/// </summary>
internal static class Strings
{
    /// <summary>
    /// Every language the interface exists in, keyed by the two-letter code Windows reports for
    /// the display language - which is also what <c>settings.json</c> stores, so one lookup serves
    /// both. Two of the ten are regional variants of a wider language: Brazilian Portuguese answers
    /// for "pt" and Simplified Chinese for "zh", the two by far the most Windows installations are
    /// set to. The order here is the order the settings list shows.
    /// </summary>
    public static readonly (string Code, string Bundle)[] Offered =
    {
        ("en", "Strings"),
        ("de", "StringsDe"),
        ("es", "StringsEs"),
        ("pt", "StringsPtBr"),
        ("it", "StringsIt"),
        ("nl", "StringsNl"),
        ("pl", "StringsPl"),
        ("tr", "StringsTr"),
        ("ja", "StringsJa"),
        ("zh", "StringsZh"),
    };

    private static readonly Dictionary<string, ResourceManager> All = Offered.ToDictionary(
        language => language.Code,
        language => new ResourceManager("AuraToggle." + language.Bundle, typeof(Strings).Assembly));

    private static readonly ResourceManager English = All["en"];

    /// <summary>Empty follows Windows; a two-letter code from <see cref="Codes"/> forces one
    /// language.</summary>
    public static string Override { get; set; } = "";

    /// <summary>The two-letter codes of every language on offer, in display order.</summary>
    public static IEnumerable<string> Codes => All.Keys;

    private static ResourceManager Current =>
        Bundle(Override.Length > 0 ? Override : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    /// <summary>Falls back to English for anything not translated - an unknown code, a hand-edited
    /// settings file, or a Windows display language the tool does not speak.</summary>
    private static ResourceManager Bundle(string language) =>
        All.TryGetValue(language, out ResourceManager? found) ? found : English;

    /// <summary>
    /// One specific language regardless of <see cref="Override"/> - the CLI's forgiving
    /// <c>-preset</c> and <c>-channel</c> matching accepts a name in any of the ten languages, not
    /// just whichever one is currently active.
    /// </summary>
    internal static string InLanguage(string key, string language) =>
        Bundle(language).GetString(key) ?? English.GetString(key) ?? key;

    public static string WindowTitle => Get("WindowTitle");

    public static string ButtonStateOn => Get("ButtonStateOn");

    public static string ButtonStateOff => Get("ButtonStateOff");

    public static string ButtonAccessibleName => Get("ButtonAccessibleName");

    public static string UsageLine => Get("UsageLine");

    public static string ErrorControllerNotFound => Get("ErrorControllerNotFound");

    public static string ErrorControllerBusy => Get("ErrorControllerBusy");

    public static string ErrorWriteFailed => Get("ErrorWriteFailed");

    public static string ErrorWriteBusy => Get("ErrorWriteBusy");

    public static string ErrorWriteTimeout => Get("ErrorWriteTimeout");

    public static string ErrorWriteGeneric => Get("ErrorWriteGeneric");

    public static string ErrorDeviceNotFound => Get("ErrorDeviceNotFound");

    public static string ErrorChannelNotFound => Get("ErrorChannelNotFound");

    public static string UsagePresets => Get("UsagePresets");

    public static string PresetAccessibleName => Get("PresetAccessibleName");

    public static string TrayOpen => Get("TrayOpen");

    public static string TrayExit => Get("TrayExit");

    public static string StatusControllerMissing => Get("StatusControllerMissing");

    public static string SettingsAccessibleName => Get("SettingsAccessibleName");

    public static string SettingAutoStart => Get("SettingAutoStart");

    public static string SettingMinimiseOnClose => Get("SettingMinimiseOnClose");

    public static string SettingStartAction => Get("SettingStartAction");

    public static string StartActionNone => Get("StartActionNone");

    public static string StartActionOff => Get("StartActionOff");

    public static string SettingAnimate => Get("SettingAnimate");
    public static string SettingAlwaysOnTop => Get("SettingAlwaysOnTop");

    public static string SettingLanguage => Get("SettingLanguage");

    public static string LanguageAuto => Get("LanguageAuto");

    public static string DeviceFallbackName => Get("DeviceFallbackName");

    public static string ChannelAll => Get("ChannelAll");

    public static string ChannelAccessibleName => Get("ChannelAccessibleName");

    public static string ChannelOnboard => Get("ChannelOnboard");

    /// <summary>"ARGB {0}" - the header number.</summary>
    public static string ChannelHeader => Get("ChannelHeader");

    /// <summary>"{0} - {1}" - controller name and channel name, on multi-controller boards.</summary>
    public static string ChannelQualified => Get("ChannelQualified");

    /// <summary>
    /// Standing reminder shown under the effect list while a single channel is selected: the
    /// firmware-driven effects still apply to every channel of that controller at once, even
    /// though all nine effects stay selectable regardless of the selection.
    /// </summary>
    public static string ChannelEffectHint => Get("ChannelEffectHint");

    public static string ChannelRenameSave => Get("ChannelRenameSave");

    public static string ChannelRenameReset => Get("ChannelRenameReset");

    public static string ChannelRenameResetConfirm => Get("ChannelRenameResetConfirm");

    public static string ChannelRenameAccessibleName => Get("ChannelRenameAccessibleName");

    public static string SettingBrightness => Get("SettingBrightness");

    public static string ColourAccessibleName => Get("ColourAccessibleName");

    public static string ColourHexAccessibleName => Get("ColourHexAccessibleName");

    /// <summary>"{0} %" - the brightness read-out.</summary>
    public static string BrightnessValue => Get("BrightnessValue");

    public static string ButtonNewCustomPreset => Get("ButtonNewCustomPreset");

    public static string CustomPresetNamePlaceholder => Get("CustomPresetNamePlaceholder");

    public static string CustomPresetSave => Get("CustomPresetSave");

    public static string CustomPresetCreate => Get("CustomPresetCreate");

    public static string CustomPresetReplace => Get("CustomPresetReplace");

    public static string CustomPresetDelete => Get("CustomPresetDelete");

    public static string CustomPresetConfirmDelete => Get("CustomPresetConfirmDelete");

    public static string CustomPresetDiscard => Get("CustomPresetDiscard");

    public static string CustomPresetNew => Get("CustomPresetNew");

    public static string CustomPresetEdit => Get("CustomPresetEdit");

    public static string CustomPresetNoDevices => Get("CustomPresetNoDevices");

    public static string CustomPresetAccessibleName => Get("CustomPresetAccessibleName");
    public static string CustomPresetExport => Get("CustomPresetExport");
    public static string CustomPresetImport => Get("CustomPresetImport");
    public static string CustomPresetExportError => Get("CustomPresetExportError");
    public static string CustomPresetImportError => Get("CustomPresetImportError");

    /// <summary>Display name of a lighting effect.</summary>
    public static string Preset(string resourceKey) => Get(resourceKey);

    public static string ErrorTitle => Get("ErrorTitle");

    public static string ErrorUnexpected => Get("ErrorUnexpected");

    public static string ErrorDetails => Get("ErrorDetails");

    public static string ErrorCopyDetails => Get("ErrorCopyDetails");

    public static string ErrorOpenLog => Get("ErrorOpenLog");

    public static string ErrorClose => Get("ErrorClose");

    public static string SettingReset => Get("SettingReset");

    public static string SettingResetConfirm => Get("SettingResetConfirm");

    public static string SettingHotkey => Get("SettingHotkey");

    public static string SettingHotkeyConflict => Get("SettingHotkeyConflict");
    public static string PresetShortcutHint => Get("PresetShortcutHint");

    public static string HotkeyRecordPrompt => Get("HotkeyRecordPrompt");

    public static string HotkeyModifierControl => Get("HotkeyModifierControl");

    public static string HotkeyModifierAlt => Get("HotkeyModifierAlt");

    public static string HotkeyModifierShift => Get("HotkeyModifierShift");

    public static string HotkeyModifierWin => Get("HotkeyModifierWin");

    private static string Get(string key) =>
        Current.GetString(key) ?? English.GetString(key) ?? key;
}
