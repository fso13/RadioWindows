using RadioWindows.Core;

namespace RadioWindows.ViewModels;

/// <summary>Small modal question: delete a station, restore the catalog, quit.</summary>
public sealed class ConfirmDialogViewModel : ObservableObject
{
    private readonly Action _onConfirm;
    private readonly ShellViewModel _shell;

    public ConfirmDialogViewModel(
        string title,
        string message,
        string confirmLabel,
        bool isDestructive,
        Action onConfirm,
        ShellViewModel? shell = null)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        IsDestructive = isDestructive;
        _onConfirm = onConfirm;
        _shell = shell!;
        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Cancel);
    }

    public string Title { get; }

    public string Message { get; }

    public string ConfirmLabel { get; }

    public bool IsDestructive { get; }

    public string CancelLabel => Loc.T("Common.Cancel");

    public RelayCommand ConfirmCommand { get; }

    public RelayCommand CancelCommand { get; }

    private void Confirm()
    {
        _shell.CloseModal();
        _onConfirm();
    }

    private void Cancel() => _shell.CloseModal();
}
