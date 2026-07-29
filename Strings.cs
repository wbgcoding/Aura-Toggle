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

    private static ResourceManager Current =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? German : English;

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

    /// <summary>Display name of a lighting effect.</summary>
    public static string Preset(string resourceKey) => Get(resourceKey);

    private static string Get(string key) =>
        Current.GetString(key) ?? English.GetString(key) ?? key;
}
