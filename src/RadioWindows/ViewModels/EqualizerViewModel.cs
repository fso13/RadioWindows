using System.Collections.ObjectModel;
using RadioWindows.Controls;
using RadioWindows.Core;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using RadioWindows.Services;

namespace RadioWindows.ViewModels;

public sealed class EqBandViewModel : ObservableObject
{
    private readonly EqualizerService _service;

    public EqBandViewModel(int index, EqualizerService service)
    {
        Index = index;
        _service = service;
        Label = EqualizerCatalog.LabelKeyForFrequency(EqualizerCatalog.Frequencies[index]);
        Value = service.Gains[index];
        _service.PropertyChanged += (_, _) => Value = _service.Gains[Index];
    }

    public int Index { get; }

    public string Label { get; }

    public double Value
    {
        get => _service.Gains[Index];
        set
        {
            if (Math.Abs(_service.Gains[Index] - value) < 0.05)
            {
                return;
            }

            _service.SetBand(Index, value);
            Raise();
            Raise(nameof(ValueText));
        }
    }

    public string ValueText => Value switch
    {
        > 0 => $"+{Value:0.#}",
        _ => Value.ToString("0.#"),
    };
}

/// <summary>Ten-band equalizer sheet with presets and a live custom curve.</summary>
public sealed class EqualizerViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;

    public EqualizerViewModel(AppServices services, ShellViewModel shell)
    {
        _services = services;
        _shell = shell;
        foreach (var index in Enumerable.Range(0, EqualizerCatalog.BandCount))
        {
            Bands.Add(new EqBandViewModel(index, services.Equalizer));
        }

        foreach (var preset in EqualizerCatalog.Presets)
        {
            Presets.Add(new EqPresetItemViewModel(preset, this));
        }

        CloseCommand = new RelayCommand(() => _shell.CloseModal());
        ResetCommand = new RelayCommand(() => services.Equalizer.ResetFlat());
        EnableCommand = new RelayCommand(() => services.Equalizer.SetEnabled(!services.Equalizer.Enabled));
        _onEqualizerChanged = (_, _) =>
        {
            Raise(nameof(IsEnabled));
            Raise(nameof(IsCustom));
            Raise(nameof(SelectedPresetId));
            Raise(nameof(Subtitle));
            foreach (var preset in Presets)
            {
                preset.Refresh();
            }
        };
        services.Equalizer.PropertyChanged += _onEqualizerChanged;
    }

    private readonly System.ComponentModel.PropertyChangedEventHandler _onEqualizerChanged;

    public void Dispose() => _services.Equalizer.PropertyChanged -= _onEqualizerChanged;

    public ObservableCollection<EqBandViewModel> Bands { get; } = [];

    public ObservableCollection<EqPresetItemViewModel> Presets { get; } = [];

    public bool IsEnabled
    {
        get => _services.Equalizer.Enabled;
        set => _services.Equalizer.SetEnabled(value);
    }

    public bool IsCustom => _services.Equalizer.IsCustom;

    public string SelectedPresetId => _services.Equalizer.PresetId;

    public string Title => Loc.T("Eq.Title");

    public string Subtitle => Loc.T("Eq.Subtitle");

    public string EnableLabel => Loc.T("Eq.Enable");

    public string ResetLabel => Loc.T("Eq.ResetFlat");

    public string PresetLabel => Loc.T("Eq.Preset");

    public string GainLabel => Loc.T("Eq.Gain");

    public RelayCommand CloseCommand { get; }

    public RelayCommand ResetCommand { get; }

    public RelayCommand EnableCommand { get; }

    public void ApplyPreset(string id) => _services.Equalizer.ApplyPreset(id);
}

/// <summary>One preset chip in the equalizer sheet.</summary>
public sealed class EqPresetItemViewModel : ObservableObject
{
    private readonly EqualizerViewModel _owner;
    private readonly EqualizerPreset _preset;

    public EqPresetItemViewModel(EqualizerPreset preset, EqualizerViewModel owner)
    {
        _preset = preset;
        _owner = owner;
        Command = new RelayCommand(() => owner.ApplyPreset(preset.Id));
    }

    public string Id => _preset.Id;

    public string Label => Loc.T(_preset.LabelKey);

    public RelayCommand Command { get; }

    public bool IsSelected => _owner.SelectedPresetId == _preset.Id;

    public void Refresh() => Raise(nameof(IsSelected));
}
