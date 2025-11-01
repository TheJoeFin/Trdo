using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Trdo.Services;

public class NavigationService : INotifyPropertyChanged
{
    private static NavigationService? _instance;
    private Frame? _frame;

    public static NavigationService Instance => _instance ??= new NavigationService();

    private NavigationService()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? NavigationChanged;

    public Frame? Frame
    {
        get => _frame;
        set
        {
            if (_frame == value) return;

            if (_frame != null)
            {
                _frame.Navigated -= OnNavigated;
            }

            _frame = value;

            if (_frame != null)
            {
                _frame.Navigated += OnNavigated;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanGoBack));
        }
    }

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    private void OnNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        OnPropertyChanged(nameof(CanGoBack));
        NavigationChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Navigate(Type pageType, object? parameter = null)
    {
        if (_frame == null) return false;
        // Don't navigate if we're already on the same page and no parameter is passed
        if (_frame.Content?.GetType() == pageType && parameter == null)
            return false;

        return _frame.Navigate(pageType, parameter);
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
        {
            _frame.GoBack();
        }
    }

    public void ClearBackStack()
    {
        if (_frame == null) return;

        _frame.BackStack.Clear();
        OnPropertyChanged(nameof(CanGoBack));
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
