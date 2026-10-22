using System.Globalization;

namespace RadioWindows.Core.Services;

/// <summary>Session sleep timer with a five-second fade-out.</summary>
public sealed class SleepTimerService : ObservableObject, IDisposable
{
    private readonly IPlaybackFade _playback;
    private readonly IUiDispatcher _dispatcher;
    private readonly Timer _ticker;
    private CancellationTokenSource? _fade;
    private DateTimeOffset? _endsAt;
    private TimeSpan _remaining;
    private bool _isFading;

    public SleepTimerService(IPlaybackFade playback, IUiDispatcher dispatcher)
    {
        _playback = playback;
        _dispatcher = dispatcher;
        _ticker = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public IReadOnlyList<int> Options { get; } = [15, 30, 60, 90];

    public DateTimeOffset? EndsAt => _endsAt;

    public TimeSpan Remaining => _remaining;

    public bool IsFading => _isFading;

    public bool IsActive => _endsAt is not null;

    public string RemainingText => IsActive ? Format(_remaining) : string.Empty;

    public static string Format(TimeSpan interval)
    {
        var total = Math.Max(0, (int)Math.Ceiling(interval.TotalSeconds));
        return string.Create(CultureInfo.InvariantCulture, $"{total / 60:00}:{total % 60:00}");
    }

    public void Start(int minutes)
    {
        Cancel();
        var duration = TimeSpan.FromMinutes(Math.Max(1, minutes));
        _endsAt = DateTimeOffset.Now.Add(duration);
        _remaining = duration;
        _isFading = false;
        _playback.SetFadeFactor(1);
        _ticker.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Raise(nameof(IsActive));
        Raise(nameof(EndsAt));
        Raise(nameof(Remaining));
        Raise(nameof(RemainingText));
    }

    public void Cancel()
    {
        _ticker.Change(Timeout.Infinite, Timeout.Infinite);
        _fade?.Cancel();
        _fade?.Dispose();
        _fade = null;
        _endsAt = null;
        _remaining = TimeSpan.Zero;
        _isFading = false;
        _playback.SetFadeFactor(1);
        Raise(nameof(IsActive));
        Raise(nameof(EndsAt));
        Raise(nameof(Remaining));
        Raise(nameof(RemainingText));
        Raise(nameof(IsFading));
    }

    public void Dispose()
    {
        _ticker.Dispose();
        _fade?.Cancel();
        _fade?.Dispose();
    }

    private void Tick()
    {
        if (_endsAt is not { } endsAt)
        {
            return;
        }

        var remaining = endsAt - DateTimeOffset.Now;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        _dispatcher.Post(() =>
        {
            _remaining = remaining;
            Raise(nameof(Remaining));
            Raise(nameof(RemainingText));

            if (remaining <= TimeSpan.FromSeconds(5) && !_isFading)
            {
                BeginFade();
            }

            if (remaining <= TimeSpan.Zero)
            {
                Finish();
            }
        });
    }

    private void BeginFade()
    {
        _isFading = true;
        Raise(nameof(IsFading));
        _fade = new CancellationTokenSource();
        var token = _fade.Token;

        _ = Task.Run(async () =>
        {
            const int steps = 50;
            for (var step = 0; step <= steps; step++)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                _playback.SetFadeFactor(1 - (double)step / steps);
                try
                {
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });
    }

    private void Finish()
    {
        _ticker.Change(Timeout.Infinite, Timeout.Infinite);
        _fade?.Cancel();
        _fade?.Dispose();
        _fade = null;
        _endsAt = null;
        _remaining = TimeSpan.Zero;
        _isFading = false;
        _playback.Pause();
        _playback.SetFadeFactor(1);
        Raise(nameof(IsActive));
        Raise(nameof(EndsAt));
        Raise(nameof(Remaining));
        Raise(nameof(RemainingText));
        Raise(nameof(IsFading));
    }
}
