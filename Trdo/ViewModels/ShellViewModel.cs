using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Trdo.Models;
using Trdo.Pages;
using Trdo.Services;

namespace Trdo.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly NavigationService _navigationService;

    public ShellViewModel()
    {
        _navigationService = NavigationService.Instance;
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

    [RelayCommand]
    public void NavigateToPlayingPage()
    {
        _navigationService.Navigate(typeof(PlayingPage));
    }

    [RelayCommand]
    public void NavigateToSettingsPage()
    {
        _navigationService.Navigate(typeof(SettingsPage));
    }

    [RelayCommand]
    public void NavigateToSearchStationPage()
    {
        _navigationService.Navigate(typeof(SearchStation));
    }

    [RelayCommand]
    public void NavigateToAddStationPage(RadioStation? stationToEdit = null)
    {
        _navigationService.Navigate(typeof(AddStation), stationToEdit);
    }

    public void NavigateToAddStationPage(RadioBrowserStation? searchResult)
    {
        _navigationService.Navigate(typeof(AddStation), searchResult);
    }

    [RelayCommand]
    public void NavigateToAboutPage()
    {
        _navigationService.Navigate(typeof(AboutPage));
    }

    [RelayCommand]
    public void NavigateToNowPlayingPage()
    {
        _navigationService.Navigate(typeof(NowPlayingPage));
    }

    [RelayCommand]
    public void GoBack()
    {
        _navigationService.GoBack();
    }
}
