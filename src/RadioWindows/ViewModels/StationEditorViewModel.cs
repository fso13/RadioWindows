using RadioWindows.Core;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using RadioWindows.Services;

namespace RadioWindows.ViewModels;

/// <summary>Create/edit sheet: stream URL, genre, group, artwork, loudness and EQ preset.</summary>
public sealed class StationEditorViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private readonly RadioStation? _existing;

    private string _name = string.Empty;
    private string _url = string.Empty;
    private string _genre = GenreCatalog.Pop;
    private string _comment = string.Empty;
    private string _group = string.Empty;
    private string _logoUrl = string.Empty;
    private double _volumeOffsetDb;
    private string _eqPresetId = string.Empty;
    private bool _autoplay;
    private string? _error;

    public StationEditorViewModel(AppServices services, ShellViewModel shell, RadioStation? existing)
    {
        _services = services;
        _shell = shell;
        _existing = existing;
        Genres = GenreCatalog.Presets
            .Concat(services.Library.Genres.Where(genre => !GenreCatalog.Presets.Contains(genre)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        EqPresets = EqualizerCatalog.Presets.ToList();
        EqOptions = new[] { new EqOptionViewModel(null, Loc.T("Editor.EqNone")) }
            .Concat(EqualizerCatalog.Presets.Select(preset => new EqOptionViewModel(preset.Id, Loc.T(preset.LabelKey))))
            .ToList();

        if (existing is not null)
        {
            _name = existing.Name;
            _url = existing.Url;
            _genre = existing.Genre;
            _comment = existing.Comment ?? string.Empty;
            _group = existing.Group ?? string.Empty;
            _logoUrl = existing.LogoUrl ?? string.Empty;
            _volumeOffsetDb = existing.VolumeOffsetDb;
            _eqPresetId = existing.EqPresetId ?? string.Empty;
            _autoplay = existing.Autoplay;
        }

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => _shell.CloseModal());
        DeleteCommand = new RelayCommand(Delete);
    }

    public string Title => _existing is null ? Loc.T("Editor.TitleNew") : Loc.T("Editor.TitleEdit");

    public string SaveLabel => _existing is null ? Loc.T("Common.Add") : Loc.T("Common.Save");

    public bool IsExisting => _existing is not null;

    public bool CanDelete => _existing is not null;

    public IReadOnlyList<string> Genres { get; }

    public IReadOnlyList<EqualizerPreset> EqPresets { get; }

    public IReadOnlyList<EqOptionViewModel> EqOptions { get; }

    public EqOptionViewModel SelectedEqOption
    {
        get => EqOptions.FirstOrDefault(option => option.Id == (string.IsNullOrEmpty(_eqPresetId) ? null : _eqPresetId))
               ?? EqOptions[0];
        set
        {
            if (value is null)
            {
                return;
            }

            EqPresetId = value.Id ?? string.Empty;
            Raise();
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value))
            {
                ClearError();
            }
        }
    }

    public string Url
    {
        get => _url;
        set
        {
            if (Set(ref _url, value))
            {
                ClearError();
            }
        }
    }

    public string Genre
    {
        get => _genre;
        set => Set(ref _genre, value);
    }

    public string Comment
    {
        get => _comment;
        set => Set(ref _comment, value);
    }

    public string Group
    {
        get => _group;
        set => Set(ref _group, value);
    }

    public string LogoUrl
    {
        get => _logoUrl;
        set => Set(ref _logoUrl, value);
    }

    public double VolumeOffsetDb
    {
        get => _volumeOffsetDb;
        set
        {
            if (Set(ref _volumeOffsetDb, Math.Clamp(value, -12, 12)))
            {
                Raise(nameof(VolumeOffsetText));
            }
        }
    }

    public string VolumeOffsetText => VolumeOffsetDb switch
    {
        > 0 => $"+{VolumeOffsetDb:0.#} dB",
        _ => $"{VolumeOffsetDb:0.#} dB",
    };

    public string EqPresetId
    {
        get => _eqPresetId;
        set
        {
            if (Set(ref _eqPresetId, value))
            {
                Raise(nameof(SelectedEqOption));
            }
        }
    }

    public bool Autoplay
    {
        get => _autoplay;
        set => Set(ref _autoplay, value);
    }

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

    public bool HasError => _error is not null;

    public RelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand DeleteCommand { get; }

    private void Save()
    {
        var name = _name.Trim();
        var url = _url.Trim();
        if (name.Length == 0)
        {
            Error = Loc.T("Editor.NameRequired");
            return;
        }

        if (RadioStation.TryParseStream(url) is null)
        {
            Error = Loc.T("Editor.UrlInvalid");
            return;
        }

        var eqPreset = string.IsNullOrWhiteSpace(_eqPresetId) ? null : _eqPresetId;
        if (_existing is not null)
        {
            var updated = _existing.Clone();
            updated.Name = name;
            updated.Url = url;
            updated.Genre = _genre;
            updated.Comment = _comment;
            updated.Group = _group;
            updated.LogoUrl = _logoUrl;
            updated.VolumeOffsetDb = _volumeOffsetDb;
            updated.EqPresetId = eqPreset;
            updated.Autoplay = _autoplay;
            _services.Library.Update(updated);
            _services.Toasts.ShowSuccess(Loc.T("Toast.StationSaved"));
        }
        else
        {
            var added = _services.Library.Add(
                name,
                url,
                _genre,
                _comment,
                _group,
                _logoUrl,
                volumeOffsetDb: _volumeOffsetDb,
                eqPresetId: eqPreset,
                autoplay: false);
            if (_autoplay)
            {
                _services.Library.SetAutoplayStation(added.Id);
            }

            _services.Toasts.ShowSuccess(Loc.T("Toast.StationAdded"));
        }

        if (_autoplay && _existing is not null)
        {
            _services.Library.SetAutoplayStation(_existing.Id);
        }

        _shell.CloseModal();
    }

    private void Delete()
    {
        if (_existing is null)
        {
            return;
        }

        var station = _existing;
        _shell.ShowModal(new ConfirmDialogViewModel(
            Loc.T("Row.DeleteTitle"),
            Loc.Format("Row.DeleteMessage", station.Name),
            Loc.T("Common.Delete"),
            isDestructive: true,
            () =>
            {
                if (_services.Player.Station?.Id == station.Id)
                {
                    _services.Player.Stop();
                }

                _services.Library.Remove(station.Id);
                _services.Toasts.ShowSuccess(Loc.T("Toast.StationDeleted"));
            },
            _shell));
    }

    private void ClearError()
    {
        if (_error is not null)
        {
            Error = null;
        }
    }
}

/// <summary>One entry of the per-station equalizer preset picker.</summary>
public sealed record EqOptionViewModel(string? Id, string Label);
