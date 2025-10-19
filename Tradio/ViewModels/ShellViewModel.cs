using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Tradio.Pages;
using Tradio.Services;

namespace Tradio.ViewModels;

public class ShellViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly NavigationService _navigationService;

    public ShellViewModel()
    {
        _navigationService = new NavigationService();
        _navigationService.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(NavigationService.CanGoBack))
            {
                OnPropertyChanged(nameof(CanGoBack));
            }
        };
    }

    public NavigationService NavigationService => _navigationService;

    public Frame? ContentFrame
    {
        get => _navigationService.Frame;
        set => _navigationService.Frame = value;
    }

    public bool CanGoBack => _navigationService.CanGoBack;

    public void NavigateToPlayingPage()
    {
        _navigationService.Navigate(typeof(PlayingPage));
    }

    public void NavigateToSettingsPage()
    {
        _navigationService.Navigate(typeof(SettingsPage));
    }

    public void NavigateToAddStationPage(Models.RadioStation? stationToEdit = null)
    {
        _navigationService.Navigate(typeof(AddStation), stationToEdit);
    }

    public void GoBack()
    {
        _navigationService.GoBack();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
