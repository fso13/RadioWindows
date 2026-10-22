using Avalonia.Threading;
using RadioWindows.Core;

namespace RadioWindows.Services;

public enum ToastKind
{
    Info,
    Success,
    Error,
}

/// <summary>Transient message shown above the transport bar.</summary>
public sealed class ToastService : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private string? _message;
    private ToastKind _kind;

    public ToastService()
    {
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2.4), DispatcherPriority.Normal, (_, _) => Hide());
        _timer.Stop();
    }

    public string? Message => _message;

    public ToastKind Kind => _kind;

    public bool IsVisible => _message is not null;

    public void Show(string message, ToastKind kind = ToastKind.Info)
    {
        _message = message;
        _kind = kind;
        Raise(nameof(Message));
        Raise(nameof(Kind));
        Raise(nameof(IsVisible));
        _timer.Stop();
        _timer.Start();
    }

    public void ShowError(string message) => Show(message, ToastKind.Error);

    public void ShowSuccess(string message) => Show(message, ToastKind.Success);

    private void Hide()
    {
        _timer.Stop();
        _message = null;
        Raise(nameof(Message));
        Raise(nameof(IsVisible));
    }
}
