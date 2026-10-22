using System.Globalization;

namespace RadioWindows.Core.Services;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>Light/dark/system theme choice, persisted as <c>theme_preference</c>.</summary>
public sealed class ThemeService : ObservableObject
{
    private readonly Preferences _preferences;
    private ThemePreference _preference;

    public ThemeService(Preferences preferences)
    {
        _preferences = preferences;
        _preference = Parse(preferences.GetString("theme_preference"));
    }

    public ThemePreference Preference => _preference;

    public void SetPreference(ThemePreference value)
    {
        if (_preference == value)
        {
            return;
        }

        _preference = value;
        _preferences.Set("theme_preference", Serialize(value));
        Raise(nameof(Preference));
    }

    public static string Serialize(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => "light",
        ThemePreference.Dark => "dark",
        _ => "system",
    };

    public static ThemePreference Parse(string? raw) => raw?.Trim().ToLower(CultureInfo.InvariantCulture) switch
    {
        "light" => ThemePreference.Light,
        "dark" => ThemePreference.Dark,
        _ => ThemePreference.System,
    };
}
