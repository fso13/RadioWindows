namespace RadioWindows.Core.Services;

/// <summary>Resolver that never finds artwork; used in tests and as a fallback.</summary>
public sealed class NullLogoResolver : ILogoResolver
{
    public Task<string?> ResolveAsync(
        string streamUrl,
        string? homepage,
        string stationName,
        CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}
