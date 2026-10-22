using Avalonia;
using Avalonia.Styling;
using RadioWindows.Core;
using RadioWindows.Core.Services;
using RadioWindows.Services;

namespace RadioWindows.ViewModels;

/// <summary>Owns the current page, the modal layer and window-level actions.</summary>
public sealed class ShellViewModel : ObservableObject
{
    private object? _page;
    private object? _modal;

    public ShellViewModel(AppServices services)
    {
        Services = services;
        Main = new MainPageViewModel(services, this);
        _page = Main;
        services.Theme.PropertyChanged += (_, _) => ApplyTheme();
        ApplyTheme();
    }

    public AppServices Services { get; }

    public MainPageViewModel Main { get; }

    public ToastService Toasts => Services.Toasts;

    public object? Page
    {
        get => _page;
        private set
        {
            var previous = _page;
            if (Set(ref _page, value))
            {
                (previous as IDisposable)?.Dispose();
            }
        }
    }

    public object? Modal
    {
        get => _modal;
        private set
        {
            var previous = _modal;
            if (Set(ref _modal, value))
            {
                (previous as IDisposable)?.Dispose();
                Raise(nameof(HasModal));
            }
        }
    }

    public bool HasModal => _modal is not null;

    public event Action? HideRequested;

    public event Action? QuitRequested;

    public void RequestHide() => HideRequested?.Invoke();

    public void RequestQuit() => QuitRequested?.Invoke();

    public void ShowMain()
    {
        Modal = null;
        Page = Main;
    }

    public void ShowSettings()
    {
        Modal = null;
        Page = new SettingsPageViewModel(Services, this);
    }

    public void ShowModal(object modal) => Modal = modal;

    public void CloseModal() => Modal = null;

    public void ApplyTheme()
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = Services.Theme.Preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
