using System.Runtime.InteropServices;
using System.Text;
using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>Parses BASS tag blocks (ICY/HTTP header series and StreamTitle metadata).</summary>
public static class BassTags
{
    /// <summary>Reads the series of null-terminated strings BASS returns for ICY/HTTP tags.</summary>
    public static IReadOnlyDictionary<string, string> ReadTagList(IntPtr pointer)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (pointer == IntPtr.Zero)
        {
            return result;
        }

        var offset = 0;
        while (true)
        {
            var line = Marshal.PtrToStringUTF8(IntPtr.Add(pointer, offset));
            if (string.IsNullOrEmpty(line))
            {
                break;
            }

            AddLine(result, line);
            offset += Encoding.UTF8.GetByteCount(line) + 1;
        }

        return result;
    }

    /// <summary>Parses the equivalent text form; used by tests and the ICY fallback.</summary>
    public static IReadOnlyDictionary<string, string> ParseTagLines(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            AddLine(result, line);
        }

        return result;
    }

    public static string? ReadMeta(IntPtr pointer) =>
        pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);

    public static string CodecFromChannelType(uint ctype, string? contentType, string? url)
    {
        if (ctype is BassNative.CtypeStreamMp3 or BassNative.CtypeStreamMp2 or BassNative.CtypeStreamMp1)
        {
            return "MP3";
        }

        if (ctype == BassNative.CtypeStreamVorbis)
        {
            return "OGG/Vorbis";
        }

        if (ctype == BassNative.CtypeStreamAac)
        {
            return "AAC";
        }

        if (ctype == BassNative.CtypeStreamMp4)
        {
            return "AAC/MP4";
        }

        if (ctype == BassNative.CtypeStreamOpus)
        {
            return "Opus";
        }

        if (ctype is BassNative.CtypeStreamFlac or BassNative.CtypeStreamFlacOgg)
        {
            return "FLAC";
        }

        if (ctype is BassNative.CtypeStreamCa or BassNative.CtypeStreamMf or BassNative.CtypeStreamAm)
        {
            return "AAC";
        }

        if ((ctype & 0xffff0000) == BassNative.CtypeStreamWav)
        {
            return "PCM";
        }

        var lowered = (contentType ?? string.Empty).ToLowerInvariant();
        if (lowered.Contains("mpegurl") || (url?.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return "HLS";
        }

        var fromType = StreamInfo.CodecNameFromToken(contentType);
        if (fromType is not ("—" or "AUDIO"))
        {
            return fromType;
        }

        return ctype == 0 ? "—" : "Audio";
    }

    public static string? ServerFromTags(
        IReadOnlyDictionary<string, string> http,
        IReadOnlyDictionary<string, string> icy,
        string? url)
    {
        if (http.TryGetValue("Server", out var server) && !string.IsNullOrWhiteSpace(server))
        {
            return server.Trim();
        }

        if (icy.TryGetValue("icy-server", out var icyServer) && !string.IsNullOrWhiteSpace(icyServer))
        {
            return icyServer.Trim();
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    public static int? BitrateFromTags(IReadOnlyDictionary<string, string> icy)
    {
        if (icy.TryGetValue("icy-br", out var raw)
            && int.TryParse(raw.Trim(), out var bitrate)
            && bitrate > 0)
        {
            return bitrate;
        }

        return null;
    }

    private static void AddLine(IDictionary<string, string> target, string line)
    {
        var colon = line.IndexOf(':');
        if (colon <= 0)
        {
            return;
        }

        var key = line[..colon].Trim();
        var value = line[(colon + 1)..].Trim();
        if (key.Length > 0 && value.Length > 0)
        {
            target[key] = value;
        }
    }
}
