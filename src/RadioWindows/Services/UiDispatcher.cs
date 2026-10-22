using Avalonia.Threading;
using RadioWindows.Core;

namespace RadioWindows.Services;

/// <summary>Posts service callbacks onto the Avalonia UI thread.</summary>
public sealed class AvaloniaDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
