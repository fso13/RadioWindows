using System.Runtime.InteropServices;
using Avalonia.Controls;
using RadioWindows.Core;
using RadioWindows.Core.Models;

namespace RadioWindows.Services;

/// <summary>
/// Global shortcuts via RegisterHotKey on Windows (Ctrl+Alt+P / Ctrl+Alt+→ by
/// default). The service subclasses the main window procedure, so shortcuts keep
/// working while the panel is hidden in the tray.
/// </summary>
public sealed class GlobalHotKeyService : ObservableObject, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int GwlWndProc = -4;
    private const int IdPlayPause = 1;
    private const int IdNext = 2;

    private readonly Preferences _preferences;
    private HotKeyCombo _playPause;
    private HotKeyCombo _nextStation;
    private string? _errorText;
    private IntPtr _handle;
    private IntPtr _oldWndProc;
    private WndProcDelegate? _wndProc;
    private bool _disposed;

    public GlobalHotKeyService(Preferences preferences)
    {
        _preferences = preferences;
        _playPause = HotKeyCombo.Parse(preferences.GetString("global_hotkey_play_pause"), HotKeyCombo.DefaultPlayPause);
        _nextStation = HotKeyCombo.Parse(preferences.GetString("global_hotkey_next"), HotKeyCombo.DefaultNext);
    }

    public static bool IsSupported => OperatingSystem.IsWindows();

    public HotKeyCombo PlayPause => _playPause;

    public HotKeyCombo NextStation => _nextStation;

    public string? ErrorText => _errorText;

    public IReadOnlyList<HotKeyKey> Keys { get; } = Enum.GetValues<HotKeyKey>();

    public IReadOnlyList<HotKeyModifiers> ModifierChoices => HotKeyCombo.ModifierChoices;

    public event Action? PlayPausePressed;

    public event Action? NextPressed;

    public void Attach(Window window)
    {
        if (!IsSupported || _handle != IntPtr.Zero)
        {
            return;
        }

        var platform = window.TryGetPlatformHandle();
        if (platform is null)
        {
            return;
        }

        _handle = platform.Handle;
        _wndProc = WindowProc;
        _oldWndProc = SetWindowLongPtr(_handle, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_wndProc));
        RegisterAll();
    }

    public void SetPlayPause(HotKeyCombo comboWhenConfirmed)
    {
        _playPause = comboWhenConfirmed;
        _preferences.Set("global_hotkey_play_pause", comboWhenConfirmed.ToStorage());
        Raise(nameof(PlayPause));
        RegisterAll();
    }

    public void SetNext(HotKeyCombo comboWhenConfirmed)
    {
        _nextStation = comboWhenConfirmed;
        _preferences.Set("global_hotkey_next", comboWhenConfirmed.ToStorage());
        Raise(nameof(NextStation));
        RegisterAll();
    }

    public void RegisterAll()
    {
        if (!IsSupported)
        {
            SetError(Loc.T("HotKey.Unsupported"));
            return;
        }

        if (_handle == IntPtr.Zero)
        {
            return;
        }

        UnregisterHotKey(_handle, IdPlayPause);
        UnregisterHotKey(_handle, IdNext);
        SetError(null);

        if (!TryRegister(IdPlayPause, _playPause))
        {
            SetError(_playPause.ConflictReason is { } reason ? Loc.T(reason) : Loc.T("HotKey.InUse"));
            return;
        }

        if (!TryRegister(IdNext, _nextStation))
        {
            SetError(_nextStation.ConflictReason is { } reason ? Loc.T(reason) : Loc.T("HotKey.InUse"));
        }
    }

    private bool TryRegister(int id, HotKeyCombo combo)
    {
        var modifiers = 0u;
        if (combo.Modifiers.HasFlag(HotKeyModifiers.Alt))
        {
            modifiers |= 0x0001;
        }

        if (combo.Modifiers.HasFlag(HotKeyModifiers.Control))
        {
            modifiers |= 0x0002;
        }

        if (combo.Modifiers.HasFlag(HotKeyModifiers.Shift))
        {
            modifiers |= 0x0004;
        }

        if (combo.Modifiers.HasFlag(HotKeyModifiers.Win))
        {
            modifiers |= 0x0008;
        }

        return RegisterHotKey(_handle, id, modifiers, (uint)combo.Key);
    }

    private void SetError(string? message)
    {
        _errorText = message;
        Raise(nameof(ErrorText));
    }

    private IntPtr WindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmHotkey)
        {
            switch (wParam.ToInt64())
            {
                case IdPlayPause:
                    PlayPausePressed?.Invoke();
                    return IntPtr.Zero;
                case IdNext:
                    NextPressed?.Invoke();
                    return IntPtr.Zero;
            }
        }

        return CallWindowProc(_oldWndProc, hWnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            UnregisterHotKey(_handle, IdPlayPause);
            UnregisterHotKey(_handle, IdNext);
            SetWindowLongPtr(_handle, GwlWndProc, _oldWndProc);
            _handle = IntPtr.Zero;
        }
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
