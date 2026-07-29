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

    public static string WindowTitle => Get("WindowTitle");

    public static string ButtonStateOn => Get("ButtonStateOn");

    public static string ButtonStateOff => Get("ButtonStateOff");

    public static string ButtonAccessibleName => Get("ButtonAccessibleName");

    public static string UsageLine => Get("UsageLine");

    public static string ErrorControllerNotFound => Get("ErrorControllerNotFound");

    public static string ErrorControllerBusy => Get("ErrorControllerBusy");

    public static string ErrorWriteFailed => Get("ErrorWriteFailed");

    public static string UsagePresets => Get("UsagePresets");

    public static string PresetAccessibleName => Get("PresetAccessibleName");

    public static string StatusChannels => Get("StatusChannels");

    public static string TrayOpen => Get("TrayOpen");

    public static string TrayExit => Get("TrayExit");

    public static string StatusControllerMissing => Get("StatusControllerMissing");

    public static string SettingsAccessibleName => Get("SettingsAccessibleName");

    public static string SettingAutoStart => Get("SettingAutoStart");

    public static string SettingStartMinimised => Get("SettingStartMinimised");

    public static string SettingMinimiseOnClose => Get("SettingMinimiseOnClose");

    public static string SettingStartAction => Get("SettingStartAction");

    public static string StartActionNone => Get("StartActionNone");

    public static string StartActionOff => Get("StartActionOff");

    public static string SettingAnimate => Get("SettingAnimate");

    public static string SettingLanguage => Get("SettingLanguage");

    public static string LanguageAuto => Get("LanguageAuto");

    public static string LanguageEnglish => Get("LanguageEnglish");

    public static string LanguageGerman => Get("LanguageGerman");

    public static string DeviceFallbackName => Get("DeviceFallbackName");

    public static string ChannelAll => Get("ChannelAll");

    public static string ChannelAccessibleName => Get("ChannelAccessibleName");

    public static string SettingCustomPresets => Get("SettingCustomPresets");

    public static string ButtonNewCustomPreset => Get("ButtonNewCustomPreset");

    public static string CustomPresetNamePlaceholder => Get("CustomPresetNamePlaceholder");

    public static string CustomPresetSave => Get("CustomPresetSave");

    public static string CustomPresetDelete => Get("CustomPresetDelete");

    public static string CustomPresetNew => Get("CustomPresetNew");

    public static string CustomPresetNoDevices => Get("CustomPresetNoDevices");

    public static string CustomPresetAccessibleName => Get("CustomPresetAccessibleName");

    /// <summary>Display name of a lighting effect.</summary>
    public static string Preset(string resourceKey) => Get(resourceKey);

    private static string Get(string key) =>
        Current.GetString(key) ?? English.GetString(key) ?? key;
}
