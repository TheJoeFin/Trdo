using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Globalization;

namespace Trdo.Services;

/// <summary>
/// Resource lookups for strings that cannot be declared with <c>x:Uid</c>, such as text built in
/// code-behind, view model state, and window titles. XAML should use <c>x:Uid</c> instead so MRT
/// resolves the string while the tree loads.
/// </summary>
public static class LocalizationService
{
    public const string DefaultLanguage = "en-US";

    /// <summary>
    /// UI languages that ship with a Strings\&lt;tag&gt;\Resources.resw file, in the same order as
    /// the language picker on the settings page.
    /// </summary>
    public static string[] SupportedLanguages { get; } = ["en-US", "es-ES"];

    private static readonly ResourceLoader _loader = new();

    /// <summary>
    /// Looks up <paramref name="key"/> in Resources.resw, returning <paramref name="fallback"/>
    /// when the resource is missing so a bad key degrades to English instead of empty UI.
    /// </summary>
    public static string GetString(string key, string fallback = "")
    {
        try
        {
            string value = _loader.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Applies the saved UI language for the process. Must run before any XAML loads, because
    /// x:Uid resources are resolved once while the element tree is created.
    /// </summary>
    public static void ApplyLanguage(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            languageTag = DefaultLanguage;
        }

        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = languageTag;
        }
        catch
        {
            // Ignore invalid culture tags and fall back to the default language.
        }
    }
}
