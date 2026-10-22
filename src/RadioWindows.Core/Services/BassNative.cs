using System.Runtime.InteropServices;

namespace RadioWindows.Core.Services;

/// <summary>Thin P/Invoke surface for BASS 2.4, BASS_FX and BASS plugins.</summary>
internal static class BassNative
{
    public const uint ConfigNetTimeout = 11;
    public const uint ConfigNetBuffer = 12;
    public const uint ConfigNetPrebuf = 15;
    public const uint ConfigNetPlaylist = 21;

    public const uint StreamBlock = 0x00100000;
    public const uint StreamStatus = 0x00800000;
    public const uint Unicode = 0x80000000;

    public const uint AttribFreq = 1;
    public const uint AttribVolume = 2;
    public const uint AttribBitrate = 12;
    public const uint AttribBuffer = 13;

    public const uint SyncEnd = 2;
    public const uint SyncMeta = 4;
    public const uint SyncOggChange = 12;

    public const uint TagHttp = 3;
    public const uint TagIcy = 4;
    public const uint TagMeta = 5;

    public const uint ChannelActiveStopped = 0;
    public const uint ChannelActivePlaying = 1;
    public const uint ChannelActiveStalled = 2;
    public const uint ChannelActivePaused = 3;

    public const uint CtypeStreamVorbis = 0x10002;
    public const uint CtypeStreamMp1 = 0x10003;
    public const uint CtypeStreamMp2 = 0x10004;
    public const uint CtypeStreamMp3 = 0x10005;
    public const uint CtypeStreamAiff = 0x10006;
    public const uint CtypeStreamCa = 0x10007;
    public const uint CtypeStreamMf = 0x10008;
    public const uint CtypeStreamAm = 0x10009;
    public const uint CtypeStreamSample = 0x1000a;
    public const uint CtypeStreamAac = 0x10b00;
    public const uint CtypeStreamMp4 = 0x10b01;
    public const uint CtypeStreamOpus = 0x11200;
    public const uint CtypeStreamFlac = 0x10900;
    public const uint CtypeStreamFlacOgg = 0x10901;
    public const uint CtypeStreamWav = 0x40000;

    public const int BfxPeakEq = 0x10004;
    public const int BfxChannelAll = -1;

    public const int ErrorSsl = 10;
    public const int ErrorTimeout = 40;
    public const int ErrorFileForm = 41;
    public const int ErrorCodec = 44;
    public const int ErrorProtocol = 48;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void DownloadProc(IntPtr buffer, uint length, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void SyncProc(int handle, int channel, int data, IntPtr user);

    [StructLayout(LayoutKind.Sequential)]
    public struct ChannelInfo
    {
        public uint Freq;
        public uint Chans;
        public uint Flags;
        public uint Ctype;
        public uint OrigRes;
        public IntPtr Plugin;
        public IntPtr Sample;
        public IntPtr FileName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PeakEqParameters
    {
        public int Band;
        public float Bandwidth;
        public float Q;
        public float Center;
        public float Gain;
        public int Channel;
    }

    [DllImport("bass", EntryPoint = "BASS_Init")]
    public static extern bool Init(int device, uint freq, uint flags, IntPtr win, IntPtr clsid);

    [DllImport("bass", EntryPoint = "BASS_Free")]
    public static extern bool Free();

    [DllImport("bass", EntryPoint = "BASS_SetConfig")]
    public static extern bool SetConfig(uint option, uint value);

    [DllImport("bass", EntryPoint = "BASS_ErrorGetCode")]
    public static extern int ErrorGetCode();

    [DllImport("bass", EntryPoint = "BASS_StreamCreateURL", CharSet = CharSet.Ansi)]
    private static extern int StreamCreateUrlA(string url, uint offset, uint flags, DownloadProc? proc, IntPtr user);

    [DllImport("bass", EntryPoint = "BASS_StreamCreateURL", CharSet = CharSet.Unicode)]
    private static extern int StreamCreateUrlW(string url, uint offset, uint flags, DownloadProc? proc, IntPtr user);

    [DllImport("bass", EntryPoint = "BASS_StreamFree")]
    public static extern bool StreamFree(int handle);

    [DllImport("bass", EntryPoint = "BASS_ChannelPlay")]
    public static extern bool ChannelPlay(int handle, bool restart);

    [DllImport("bass", EntryPoint = "BASS_ChannelPause")]
    public static extern bool ChannelPause(int handle);

    [DllImport("bass", EntryPoint = "BASS_ChannelStop")]
    public static extern bool ChannelStop(int handle);

    [DllImport("bass", EntryPoint = "BASS_ChannelIsActive")]
    public static extern uint ChannelIsActive(int handle);

    [DllImport("bass", EntryPoint = "BASS_ChannelSetAttribute")]
    public static extern bool ChannelSetAttribute(int handle, uint attrib, float value);

    [DllImport("bass", EntryPoint = "BASS_ChannelGetAttribute")]
    public static extern bool ChannelGetAttribute(int handle, uint attrib, out float value);

    [DllImport("bass", EntryPoint = "BASS_ChannelGetInfo")]
    public static extern bool ChannelGetInfo(int handle, ref ChannelInfo info);

    [DllImport("bass", EntryPoint = "BASS_ChannelGetTags")]
    public static extern IntPtr ChannelGetTags(int handle, uint tags);

    [DllImport("bass", EntryPoint = "BASS_ChannelSetSync")]
    public static extern int ChannelSetSync(int handle, uint type, long param, SyncProc proc, IntPtr user);

    [DllImport("bass", EntryPoint = "BASS_ChannelRemoveSync")]
    public static extern bool ChannelRemoveSync(int handle, int sync);

    [DllImport("bass", EntryPoint = "BASS_ChannelSetFX")]
    public static extern int ChannelSetFx(int handle, int type, int priority);

    [DllImport("bass", EntryPoint = "BASS_ChannelRemoveFX")]
    public static extern bool ChannelRemoveFx(int handle, int fx);

    [DllImport("bass", EntryPoint = "BASS_PluginLoad", CharSet = CharSet.Ansi)]
    private static extern int PluginLoadA(string file, uint flags);

    [DllImport("bass", EntryPoint = "BASS_PluginLoad", CharSet = CharSet.Unicode)]
    private static extern int PluginLoadW(string file, uint flags);

    [DllImport("bass", EntryPoint = "BASS_PluginFree")]
    public static extern bool PluginFree(int plugin);

    [DllImport("bass_fx", EntryPoint = "BASS_FX_GetVersion")]
    public static extern uint FxGetVersion();

    // BASS_FXSetParameters/BASS_FXGetParameters live in bass.dll itself; loading
    // bass_fx.dll only registers the extra effect types it provides.
    [DllImport("bass", EntryPoint = "BASS_FXSetParameters")]
    public static extern bool FxSetParameters(int handle, ref PeakEqParameters parameters);

    [DllImport("bass", EntryPoint = "BASS_FXGetParameters")]
    public static extern bool FxGetParameters(int handle, ref PeakEqParameters parameters);

    public static int StreamCreateUrl(string url, uint flags, DownloadProc? proc, IntPtr user) =>
        OperatingSystem.IsWindows()
            ? StreamCreateUrlW(url, 0, flags | Unicode, proc, user)
            : StreamCreateUrlA(url, 0, flags, proc, user);

    public static int PluginLoad(string file) =>
        OperatingSystem.IsWindows() ? PluginLoadW(file, Unicode) : PluginLoadA(file, 0);

    public static string ErrorName(int code) => code switch
    {
        1 => "memory error",
        2 => "file open error",
        3 => "device driver error",
        5 => "invalid handle",
        6 => "unsupported format",
        8 => "BASS_Init not called",
        9 => "BASS_Start not called",
        10 => "SSL/HTTPS not available",
        14 => "already initialized",
        17 => "not audio",
        18 => "no free channel",
        20 => "illegal parameter",
        23 => "invalid device",
        24 => "not playing",
        32 => "no internet connection",
        33 => "cannot create file",
        34 => "effects unavailable",
        37 => "not available",
        40 => "connection timed out",
        41 => "unsupported file format",
        43 => "invalid BASS version",
        44 => "codec unavailable",
        45 => "stream ended",
        46 => "device busy",
        47 => "unstreamable",
        48 => "unsupported protocol",
        _ => $"BASS error {code}",
    };
}
