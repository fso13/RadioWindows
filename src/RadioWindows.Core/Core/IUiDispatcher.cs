namespace RadioWindows.Core;

/// <summary>Marshals callbacks onto the UI thread without depending on Avalonia.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>Runs callbacks inline; used by tests and console tools.</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
