using RadioWindows.Core.Models;
using RadioWindows.Core.Services;

// BASS smoke test: plays a stream, prints metadata and exercises the equalizer.
// Usage: dotnet run --project tools/AudioProbe -- [url] [seconds] [eq] [--nosound]
// Returns exit code 1 when the stream never reaches Playing or reports an error.

var noSound = args.Contains("--nosound", StringComparer.OrdinalIgnoreCase);
var positional = args.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToArray();
var url = positional.Length > 0 ? positional[0] : "https://ice1.somafm.com/groovesalad-128-mp3";
var seconds = positional.Length > 1 && int.TryParse(positional[1], out var parsed) ? parsed : 12;
var useEq = positional.Length > 2 && positional[2].Equals("eq", StringComparison.OrdinalIgnoreCase);

var sawPlaying = false;
var failed = false;
var eqAttached = false;

using var engine = new BassAudioEngine(noSound);
engine.StateChanged += state =>
{
    if (state == PlaybackState.Playing)
    {
        sawPlaying = true;
    }

    if (state == PlaybackState.Error)
    {
        failed = true;
    }

    Console.WriteLine($"[state] {state}");
};
engine.MetadataChanged += title => Console.WriteLine($"[meta] {title}");

Console.WriteLine($"engine: {engine.Name} (no sound: {noSound})");
engine.SetVolume(0.6);
engine.Play(new Uri(url), 0.6);

if (useEq)
{
    await Task.Delay(2_000);
    engine.SetEqualizer(true, EqualizerCatalog.ById("bass").Gains);
    await Task.Delay(300);
    eqAttached = engine.EqualizerAttached;
    Console.WriteLine($"[eq] bass preset attached={eqAttached}");
}

var deadline = DateTime.UtcNow.AddSeconds(seconds);
var reported = 0;
while (DateTime.UtcNow < deadline)
{
    await Task.Delay(1_000);
    var bucket = (int)(DateTime.UtcNow < deadline ? (seconds - (deadline - DateTime.UtcNow).TotalSeconds) / 3 : 99);
    if (bucket != reported)
    {
        reported = bucket;
        var info = await engine.FetchStreamInfoAsync(CancellationToken.None);
        Console.WriteLine(info is null
            ? "[info] none"
            : $"[info] {info.Codec}, {info.Frequency} Hz, {info.Channels} ch, {info.BitrateKbps} kbps, server={info.Server}");
    }
}

engine.Stop();
Console.WriteLine($"done (playing={sawPlaying}, failed={failed}, eq={eqAttached})");
return failed || !sawPlaying || (useEq && !eqAttached) ? 1 : 0;
