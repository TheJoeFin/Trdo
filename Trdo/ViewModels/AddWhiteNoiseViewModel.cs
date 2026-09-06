using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Trdo.Models;
using Trdo.Services;

namespace Trdo.ViewModels;

/// <summary>
/// Backs the page that creates or edits a white noise "station" - one that plays generated
/// noise locally instead of connecting to a stream. See <see cref="RadioStation.SourceKind"/>.
/// </summary>
public sealed class AddWhiteNoiseViewModel : INotifyPropertyChanged
{
    private string _stationName = LocalizationService.GetString("AddWhiteNoise_DefaultName", "White Noise");
    private WhiteNoiseColor _noiseColor = WhiteNoiseColor.White;
    private double _volumePercent = 100;
    private string _pageTitle = LocalizationService.GetString("AddWhiteNoise_AddPageTitle", "Add White Noise");
    private PlayerViewModel? _playerViewModel;
    private RadioStation? _editingStation;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetPlayerViewModel(PlayerViewModel playerViewModel) => _playerViewModel = playerViewModel;

    public void LoadStationForEdit(RadioStation station)
    {
        _editingStation = station;
        StationName = station.Name;
        NoiseColor = station.WhiteNoiseColor;
        VolumePercent = station.Volume * 100;
        PageTitle = LocalizationService.GetString("AddWhiteNoise_EditPageTitle", "Edit White Noise");
    }

    public string PageTitle
    {
        get => _pageTitle;
        private set
        {
            if (value == _pageTitle) return;
            _pageTitle = value;
            OnPropertyChanged();
        }
    }

    public string StationName
    {
        get => _stationName;
        set
        {
            if (value == _stationName) return;
            _stationName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public WhiteNoiseColor NoiseColor
    {
        get => _noiseColor;
        set
        {
            if (value == _noiseColor) return;
            _noiseColor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWhiteSelected));
            OnPropertyChanged(nameof(IsPinkSelected));
        }
    }

    /// <summary>Bound to the "White" radio button, since x:Bind cannot two-way bind an enum directly.</summary>
    public bool IsWhiteSelected
    {
        get => _noiseColor == WhiteNoiseColor.White;
        set
        {
            if (value)
                NoiseColor = WhiteNoiseColor.White;
        }
    }

    /// <summary>Bound to the "Pink" radio button, since x:Bind cannot two-way bind an enum directly.</summary>
    public bool IsPinkSelected
    {
        get => _noiseColor == WhiteNoiseColor.Pink;
        set
        {
            if (value)
                NoiseColor = WhiteNoiseColor.Pink;
        }
    }

    /// <summary>Playback volume as a percentage, matching the range of the player's own volume control.</summary>
    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            value = Math.Clamp(value, 0, 200);
            if (value == _volumePercent) return;
            _volumePercent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeDescription));
        }
    }

    public string VolumeDescription => $"{_volumePercent:0}%";

    public bool CanSave => !string.IsNullOrWhiteSpace(StationName);

    public bool Save()
    {
        if (!CanSave)
            return false;

        if (_editingStation != null)
        {
            _editingStation.Name = StationName.Trim();
            _editingStation.WhiteNoiseColor = NoiseColor;
            _editingStation.Volume = VolumePercent / 100;

            _playerViewModel?.SaveStations();
        }
        else
        {
            RadioStation newStation = new()
            {
                Name = StationName.Trim(),
                StreamUrl = RadioStation.WhiteNoiseStreamUrl,
                SourceKind = AudioSourceKind.WhiteNoise,
                WhiteNoiseColor = NoiseColor,
                Volume = VolumePercent / 100,
            };

            _playerViewModel?.AddStation(newStation);
        }

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
