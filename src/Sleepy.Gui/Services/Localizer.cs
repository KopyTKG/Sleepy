using System.Globalization;
using Windows.ApplicationModel.Resources;
using Windows.Globalization;

namespace Sleepy.Gui.Services;

/// <summary>
/// Strings that are chosen at runtime rather than by <c>x:Uid</c> — status
/// messages, counts, summaries — plus the language override the picker sets.
/// </summary>
public static class Localizer
{
    private const string LanguageSettingKey = "language";

    private static ResourceLoader _loader = ResourceLoader.GetForViewIndependentUse();

    /// <summary>The languages offered in the picker, in the order shown.</summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } = ["en", "cs"];

    public static string Get(string key)
    {
        var value = _loader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    public static string Format(string key, params object[] arguments)
        => string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    /// <summary>
    /// Formats a counted phrase. Czech needs three forms (1, 2–4, everything
    /// else); languages that define only <c>.One</c> and <c>.Many</c> simply
    /// never hit the middle one.
    /// </summary>
    public static string Count(string key, int count)
    {
        var suffix = PluralSuffix(count);
        var value = _loader.GetString($"{key}.{suffix}");

        if (string.IsNullOrEmpty(value) && suffix != "Many")
        {
            value = _loader.GetString($"{key}.Many");
        }

        return string.IsNullOrEmpty(value)
            ? count.ToString(CultureInfo.CurrentCulture)
            : string.Format(CultureInfo.CurrentCulture, value, count);
    }

    private static string PluralSuffix(int count) => count switch
    {
        1 => "One",
        >= 2 and <= 4 => "Few",
        _ => "Many",
    };

    /// <summary>The language the app is pinned to, or an empty string for "follow the system".</summary>
    public static string CurrentOverride
    {
        get
        {
            try
            {
                return ApplicationLanguages.PrimaryLanguageOverride ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Pins the app to a language (or clears the pin when passed an empty
    /// string) and reloads the resources so new lookups use it.
    /// </summary>
    public static void SetLanguage(string language)
    {
        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = language;
            CultureInfo.CurrentUICulture = language.Length == 0
                ? CultureInfo.InstalledUICulture
                : new CultureInfo(language);
            _loader = ResourceLoader.GetForViewIndependentUse();
            SaveSetting(language);
        }
        catch (Exception)
        {
            // A host that cannot switch languages keeps the one it started with.
        }
    }

    /// <summary>Applies the language chosen in an earlier session, if any.</summary>
    public static void RestoreSavedLanguage()
    {
        try
        {
            if (Windows.Storage.ApplicationData.Current.LocalSettings.Values
                    .TryGetValue(LanguageSettingKey, out var saved)
                && saved is string language
                && language.Length > 0)
            {
                SetLanguage(language);
            }
        }
        catch (Exception)
        {
            // No settings store: fall back to the system language.
        }
    }

    private static void SaveSetting(string language)
    {
        try
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.Values[LanguageSettingKey] = language;
        }
        catch (Exception)
        {
            // Not being able to remember the choice is not worth surfacing.
        }
    }
}
