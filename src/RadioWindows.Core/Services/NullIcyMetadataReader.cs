namespace RadioWindows.Core.Services;

/// <summary>ICY reader that never connects; used by tests and headless tooling.</summary>
public sealed class NullIcyMetadataReader : IIcyMetadataReader
{
    public event Action<string>? TitleChanged;

    public Task RunAsync(Uri uri, CancellationToken cancellationToken)
    {
        _ = TitleChanged;
        return Task.CompletedTask;
    }
}
