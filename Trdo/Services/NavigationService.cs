using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
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

    /// <summary>
    /// Navigates with an explicit transition, so a page whose elements carry a
    /// <c>Connected.Key</c> can suppress the default slide-in - it would otherwise
    /// play at the same time as the connected animation and fight it.
    /// </summary>
    public bool Navigate(Type pageType, object? parameter, NavigationTransitionInfo transitionInfo)
    {
        if (_frame is null)
            return false;

        if (_frame.Content?.GetType() == pageType && parameter == null)
            return false;

        return _frame.Navigate(pageType, parameter, transitionInfo);
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack is true)
            _frame.GoBack();
    }

    /// <summary>
    /// Goes back with an explicit transition - see the <see cref="Navigate(Type,object?,NavigationTransitionInfo)"/>
    /// overload above for why a page with a connected animation needs this.
    /// </summary>
    public void GoBack(NavigationTransitionInfo transitionInfo)
    {
        if (_frame?.CanGoBack is true)
            _frame.GoBack(transitionInfo);
    }

    /// <summary>
    /// Navigates to <paramref name="pageType"/> as a fresh root, leaving no
    /// history behind it. The clear has to happen *after* the navigation:
    /// navigating pushes the page being left onto the back stack, so clearing
    /// first would immediately re-arm the back button.
    /// </summary>
    public void ResetTo(Type pageType, object? parameter = null)
    {
        Navigate(pageType, parameter);
        ClearBackStack();
    }

    public void ClearBackStack()
    {
        if (_frame is null)
            return;

        _frame.BackStack.Clear();
        _frame.ForwardStack.Clear();
        OnPropertyChanged(nameof(CanGoBack));
        Debug.WriteLine($"CanGoBack: {CanGoBack}");
    }
}
