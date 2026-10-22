using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>
/// Silent engine used in tests, on non-Windows developer machines and whenever
/// libVLC cannot start. Playback state changes are simulated so the UI can be
/// exercised end to end.
/// </summary>
public sealed class NullAudioEngine : IAudioEngine
{
    private CancellationTokenSource? _pending;

    public string Name => "Null";

    public event Action<PlaybackState>? StateChanged;

    /// <summary>When set, the next <see cref="Play"/> reports an error.</summary>
    public bool FailOnPlay { get; set; }

    /// <summary>Delay before reporting Playing (lets tests observe Loading).</summary>
    public TimeSpan StartDelay { get; set; } = TimeSpan.FromMilliseconds(120);

    public void Play(Uri url, double volume)
    {
        _pending?.Cancel();
        StateChanged?.Invoke(PlaybackState.Loading);
        if (FailOnPlay)
        {
            StateChanged?.Invoke(PlaybackState.Error);
            return;
        }

        var cancellation = new CancellationTokenSource();
        _pending = cancellation;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(StartDelay, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            StateChanged?.Invoke(PlaybackState.Playing);
        });
    }

    public void Pause() => StateChanged?.Invoke(PlaybackState.Paused);

    public void Resume() => StateChanged?.Invoke(PlaybackState.Playing);

    public void Stop(bool emitIdle = true)
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
        if (emitIdle)
        {
            StateChanged?.Invoke(PlaybackState.Idle);
        }
    }

    public void SetVolume(double volume)
    {
    }

    public void SetEqualizer(bool enabled, IReadOnlyList<double> gains)
    {
    }

    public Task<StreamInfo?> FetchStreamInfoAsync(CancellationToken cancellationToken) =>
        Task.FromResult<StreamInfo?>(null);

    public void Dispose()
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
    }
}
