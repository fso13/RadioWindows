using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace RadioWindows.Core;

/// <summary>
/// Tiny resource localiser: flat JSON dictionaries embedded in the assembly.
/// Unknown keys fall back to the key itself so missing text is visible, never fatal.
/// </summary>
public static class Loc
{
    private static readonly object Gate = new();
    private static Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private static string _language = "ru";

    public static IReadOnlyList<string> SupportedLanguages { get; } = ["ru", "en"];

    public static string Language => _language;

    public static void Initialize(string? language = null)
    {
        var resolved = Normalize(language ?? Environment.GetEnvironmentVariable("RADIOWINDOWS_LANG") ?? SystemLanguage());
        lock (Gate)
        {
            _language = resolved;
            _strings = Load(resolved);
        }
    }

    public static string T(string key)
    {
        lock (Gate)
        {
            return _strings.TryGetValue(key, out var value) ? value : key;
        }
    }

    public static string Format(string key, params object?[] args)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, T(key), args);
        }
        catch (FormatException)
        {
            return T(key);
        }
    }

    public static bool Has(string key)
    {
        lock (Gate)
        {
            return _strings.ContainsKey(key);
        }
    }

    private static string SystemLanguage() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    private static string Normalize(string? language)
    {
        var value = (language ?? string.Empty).Trim().ToLowerInvariant();
        if (value.StartsWith("ru", StringComparison.Ordinal))
        {
            return "ru";
        }

        if (value.StartsWith("en", StringComparison.Ordinal))
        {
            return "en";
        }

        // Russian-first app: Ukrainian, Belarusian and Kazakh read Russian better than English.
        return value is "uk" or "be" or "kk" or "ky" ? "ru" : "en";
    }

    private static Dictionary<string, string> Load(string language)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = Open(assembly, language) ?? Open(assembly, "ru");
        if (stream is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static Stream? Open(Assembly assembly, string language)
    {
        var name = $"RadioWindows.Core.Localization.Strings-{language}.json";
        return assembly.GetManifestResourceStream(name);
    }
}
