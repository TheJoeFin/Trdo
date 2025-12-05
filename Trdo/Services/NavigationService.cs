using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;

namespace Trdo.Services;

public partial class NavigationService : ObservableObject
{
    private static readonly Lazy<NavigationService> _instance = new(() => new NavigationService());
    private Frame? _frame;

    public static NavigationService Instance => _instance.Value;

    private NavigationService()
    {
    }

    public event EventHandler? NavigationChanged;

    public Frame? Frame
    {
        get => _frame;
        set
        {
            if (_frame == value)
                return;

            if (_frame is not null)
                _frame.Navigated -= OnNavigated;

            _frame = value;

            if (_frame is not null)
                _frame.Navigated += OnNavigated;

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
        if (_frame is null)
            return false;

        // Don't navigate if we're already on the same page and no parameter is passed
        if (_frame.Content?.GetType() == pageType && parameter == null)
            return false;

        return _frame.Navigate(pageType, parameter);
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack is true)
            _frame.GoBack();
    }

    public void ClearBackStack()
    {
        if (_frame is null)
            return;

        _frame.BackStack.Clear();
        OnPropertyChanged(nameof(CanGoBack));
        Debug.WriteLine($"CanGoBack: {CanGoBack}");
    }
}
