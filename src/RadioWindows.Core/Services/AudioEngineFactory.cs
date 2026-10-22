namespace RadioWindows.Core.Services;

/// <summary>Creates the BASS engine, falling back to silence when its native library is missing.</summary>
public static class AudioEngineFactory
{
    public static IAudioEngine Create(Action<string>? warn = null)
    {
        try
        {
            return new BassAudioEngine();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
                                           or BadImageFormatException or InvalidOperationException)
        {
            warn?.Invoke($"BASS is unavailable ({exception.GetType().Name}: {exception.Message}); audio is disabled.");
            return new NullAudioEngine();
        }
    }
}
