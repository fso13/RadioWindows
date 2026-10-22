using System.Reflection;

namespace RadioWindows.Core.Services;

/// <summary>
/// Last.fm application keys, injected at build/run time instead of being
/// committed to the repository.
///
/// Resolution order: environment variables
/// <c>RADIO_LASTFM_API_KEY</c> / <c>RADIO_LASTFM_API_SECRET</c>, then
/// <see cref="AssemblyMetadataAttribute"/> entries named <c>LastFmApiKey</c> /
/// <c>LastFmApiSecret</c> on the Core (or entry) assembly, otherwise empty.
/// </summary>
public static class LastFmConfig
{
    /// <summary>Last.fm 2.0 API root; every method is a signed POST here.</summary>
    public const string ApiRoot = "https://ws.audioscrobbler.com/2.0/";

    public const string ApiKeyVariable = "RADIO_LASTFM_API_KEY";

    public const string ApiSecretVariable = "RADIO_LASTFM_API_SECRET";

    public const string ApiKeyMetadata = "LastFmApiKey";

    public const string ApiSecretMetadata = "LastFmApiSecret";

    public static string ApiKey => Resolve(ApiKeyVariable, ApiKeyMetadata);

    public static string ApiSecret => Resolve(ApiSecretVariable, ApiSecretMetadata);

    public static bool IsConfigured => ApiKey.Length > 0 && ApiSecret.Length > 0;

    private static string Resolve(string variable, string metadataName) =>
        Normalize(Environment.GetEnvironmentVariable(variable)) ?? MetadataValue(metadataName) ?? string.Empty;

    private static string? MetadataValue(string name)
    {
        foreach (var assembly in new[] { Assembly.GetExecutingAssembly(), Assembly.GetEntryAssembly() })
        {
            if (assembly is null)
            {
                continue;
            }

            try
            {
                foreach (var attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
                {
                    if (string.Equals(attribute.Key, name, StringComparison.OrdinalIgnoreCase)
                        && Normalize(attribute.Value) is { } value)
                    {
                        return value;
                    }
                }
            }
            catch (Exception exception) when (exception is NotSupportedException or System.IO.FileNotFoundException
                                                  or System.IO.FileLoadException or InvalidOperationException)
            {
                // Metadata is optional; a broken attribute must not break playback.
            }
        }

        return null;
    }

    /// <summary>Trims a value and rejects blanks and unexpanded <c>$(...)</c> placeholders.</summary>
    private static string? Normalize(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("$(", StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed;
    }
}
