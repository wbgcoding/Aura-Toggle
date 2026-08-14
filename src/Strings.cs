using System.Globalization;
using System.Resources;

namespace AuraToggle;

/// <summary>
/// User facing text in German and English. The language follows the Windows display language,
/// English is the fallback for everything else.
/// </summary>
internal static class Strings
{
    private static readonly ResourceManager English = new("AuraToggle.Strings", typeof(Strings).Assembly);
    private static readonly ResourceManager German = new("AuraToggle.StringsDe", typeof(Strings).Assembly);

    /// <summary>Empty follows Windows; "de" or "en" force one language.</summary>
    public static string Override { get; set; } = "";

    private static ResourceManager Current =>
        (Override.Length > 0 ? Override : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) == "de"
            ? German
            : English;

    /// <summary>
    /// One specific language regardless of <see cref="Override"/> - the CLI's forgiving
    /// <c>-channel</c> matching accepts a channel's default name in either language, not just
    /// whichever one is currently active.
    /// </summary>
    internal static string InLanguage(string key, string language) =>
        (language == "de" ? German : English).GetString(key) ?? English.GetString(key) ?? key;

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
    public static string TrayUpdateInstall => Get("TrayUpdateInstall");
    public static string TrayUpdateOpenPage => Get("TrayUpdateOpenPage");
    public static string TrayUpdateAvailable => Get("TrayUpdateAvailable");
    public static string TrayUpdateFailed => Get("TrayUpdateFailed");
    public static string UpdateNoticeTitle => Get("UpdateNoticeTitle");
    public static string UpdateNoticeBody => Get("UpdateNoticeBody");
    public static string UpdateNoticeInstall => Get("UpdateNoticeInstall");
    public static string UpdateNoticeOpenPage => Get("UpdateNoticeOpenPage");
    public static string UpdateNoticeLater => Get("UpdateNoticeLater");

    public static string SettingLanguage => Get("SettingLanguage");

    public static string LanguageAuto => Get("LanguageAuto");

    public static string LanguageEnglish => Get("LanguageEnglish");

    public static string LanguageGerman => Get("LanguageGerman");

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
