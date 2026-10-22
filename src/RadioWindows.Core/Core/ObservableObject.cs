using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RadioWindows.Core;

/// <summary>Minimal <see cref="INotifyPropertyChanged"/> base for services and view models.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <summary>
    /// Global marshaller for change notifications. The app installs an Avalonia
    /// dispatcher so background services can raise properties safely; tests use
    /// the inline default.
    /// </summary>
    public static IUiDispatcher UiDispatcher { get; set; } = new ImmediateDispatcher();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }

    protected void Raise([CallerMemberName] string? propertyName = null)
    {
        var handler = PropertyChanged;
        if (handler is null)
        {
            return;
        }

        UiDispatcher.Post(() => handler(this, new PropertyChangedEventArgs(propertyName)));
    }
}
