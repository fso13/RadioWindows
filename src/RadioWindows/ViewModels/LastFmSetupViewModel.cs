using RadioWindows.Core;
using RadioWindows.Services;

namespace RadioWindows.ViewModels;

/// <summary>Last.fm connect sheet: browser handshake and disconnect.</summary>
public sealed class LastFmSetupViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private string? _error;
    private bool _isConnecting;

    public LastFmSetupViewModel(AppServices services, ShellViewModel shell)
    {
        _services = services;
        _shell = shell;
        ConnectCommand = new RelayCommand(async () => await ConnectAsync());
        DisconnectCommand = new RelayCommand(() =>
        {
            _services.LastFm.Disconnect();
            RaiseAll();
        });
        CloseCommand = new RelayCommand(() => _shell.CloseModal());
        services.LastFm.PropertyChanged += OnLastFmChanged;
    }

    private void OnLastFmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RaiseAll();

    public void Dispose() => _services.LastFm.PropertyChanged -= OnLastFmChanged;

    public string Title => Loc.T("LastFm.Title");

    public string Subtitle => Loc.T("LastFm.Subtitle");

    public string Explanation => Loc.T("LastFm.Explanation");

    public string ConnectLabel => Loc.T("LastFm.Connect");

    public string DisconnectLabel => Loc.T("LastFm.Disconnect");

    public string WaitingLabel => Loc.T("LastFm.Waiting");

    public string KeysMissingLabel => Loc.T("LastFm.KeysMissing");

    public bool IsConnected => _services.LastFm.IsConnected;

    public bool IsConnecting => _isConnecting;

    public bool IsConfigured => _services.LastFm.KeysConfigured;

    public string UsernameLabel => _services.LastFm.Username is { } username
        ? Loc.Format("LastFm.Connected", username)
        : Loc.T("LastFm.Status.NeedsConnection");

    public string StatusText => !IsConfigured
        ? KeysMissingLabel
        : Loc.T(_services.LastFm.StatusKey);

    public string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    public RelayCommand ConnectCommand { get; }

    public RelayCommand DisconnectCommand { get; }

    public RelayCommand CloseCommand { get; }

    private async Task ConnectAsync()
    {
        if (IsConnecting)
        {
            return;
        }

        _isConnecting = true;
        Error = null;
        Raise(nameof(IsConnecting));
        var (ok, errorKey) = await _services.LastFm.ConnectAsync(CancellationToken.None);
        _isConnecting = false;
        Raise(nameof(IsConnecting));
        if (!ok)
        {
            Error = Loc.T(errorKey ?? "LastFm.Error.Unknown");
            Raise(nameof(StatusText));
            return;
        }

        RaiseAll();
        _services.Toasts.ShowSuccess(Loc.Format("LastFm.Status.Connected", _services.LastFm.Username ?? string.Empty));
    }

    private void RaiseAll()
    {
        Raise(nameof(IsConnected));
        Raise(nameof(UsernameLabel));
        Raise(nameof(StatusText));
        Raise(nameof(IsConfigured));
    }
}
