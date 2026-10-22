namespace RadioWindows.Core.Models;

/// <summary>Audio properties of the current stream, as reported by the engine or an HTTP probe.</summary>
public sealed record StreamInfo(
    string Codec,
    int Frequency,
    int Channels,
    int? BitrateKbps,
    int BufferMilliseconds,
    string? Server = null)
{
    /// <summary>Human-readable codec name for libVLC four-character codes.</summary>
    public static string CodecNameFromFourCc(uint fourCc)
    {
        var text = string.Join(
            string.Empty,
            new[]
            {
                (byte)(fourCc >> 24), (byte)(fourCc >> 16), (byte)(fourCc >> 8), (byte)fourCc,
            }.Where(b => b is >= 32 and < 127).Select(b => (char)b));

        return CodecNameFromToken(text);
    }

    /// <summary>Normalises mime types, container names and fourcc tokens into short labels.</summary>
    public static string CodecNameFromToken(string? token)
    {
        var value = token?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
        {
            return "—";
        }

        if (value.Contains("mpeg") && value.Contains("layer 3") || value.Contains("mp3"))
        {
            return "MP3";
        }

        if (value.Contains("aac") || value.Contains("mp4a"))
        {
            return "AAC";
        }

        if (value.Contains("opus"))
        {
            return "Opus";
        }

        if (value.Contains("vorbis") || value.Contains("ogg"))
        {
            return "OGG/Vorbis";
        }

        if (value.Contains("flac"))
        {
            return "FLAC";
        }

        if (value.Contains("alac"))
        {
            return "ALAC";
        }

        if (value.Contains("ac-3") || value.Contains("ac3") || value.Contains("eac3"))
        {
            return "AC-3";
        }

        if (value.Contains("hls") || value.Contains("m3u8"))
        {
            return "HLS";
        }

        if (value.Contains("pcm") || value.Contains("wav"))
        {
            return "PCM";
        }

        return value.Length <= 12 ? value.ToUpperInvariant() : "Audio";
    }
}
