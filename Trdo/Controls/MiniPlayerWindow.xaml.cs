using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.ComponentModel;
using Trdo.Services;
using Trdo.ViewModels;
using Windows.Foundation;
using WinUIEx;

namespace Trdo.Controls;

public sealed partial class MiniPlayerWindow : WindowEx
{
    private static readonly Duration OverlayFadeDuration = new(TimeSpan.FromMilliseconds(180));
    private static readonly Duration MorphDuration = new(TimeSpan.FromMilliseconds(280));
    private static readonly TimeSpan TouchOverlayDuration = TimeSpan.FromSeconds(1);
    private readonly DispatcherQueueTimer _touchOverlayTimer;
    private Storyboard? _hoverControlsStoryboard;
    private Storyboard? _morphStoryboard;
    private bool? _lastContentState;
    private bool _isClosed;

    private const double LargeLogoSize = 72.0;
    private const double SmallIconSize = 24.0;
    private const double SmallToLargeScale = SmallIconSize / LargeLogoSize;

    public PlayerViewModel ViewModel { get; }

    public MiniPlayerWindow()
    {
        InitializeComponent();

        ViewModel = PlayerViewModel.Shared;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(ModernTitlebar);
        AppWindow.SetIcon("Assets\\Radio.ico");
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

        AppWindow.IsShownInSwitchers = false;

        _touchOverlayTimer = DispatcherQueue.CreateTimer();
        _touchOverlayTimer.Interval = TouchOverlayDuration;
        _touchOverlayTimer.IsRepeating = false;
        _touchOverlayTimer.Tick += TouchOverlayTimer_Tick;

        // Apply saved visualizer preference.
        bool visualizerEnabled = SettingsService.IsMiniPlayerVisualizerEnabled;
        VisualizerToggleMenuItem.IsChecked = visualizerEnabled;
        SpectrumVisualizer.Visibility = visualizerEnabled ? Visibility.Visible : Visibility.Collapsed;

        // Set initial content state and subscribe to future changes.
        ApplyContentState(ViewModel.IsPlaybackActive, animate: false);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _isClosed = true;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _touchOverlayTimer.Stop();
        _touchOverlayTimer.Tick -= TouchOverlayTimer_Tick;
        _morphStoryboard?.Stop();
        _morphStoryboard = null;
        _hoverControlsStoryboard?.Stop();
        _hoverControlsStoryboard = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayerViewModel.MiniPlayerActiveContentVisibility)) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isClosed) ApplyContentState(ViewModel.IsPlaybackActive);
        });
    }

    private void ApplyContentState(bool isActive, bool animate = true)
    {
        // Skip if the visual state already matches — prevents spurious animations during buffering.
        if (_lastContentState.HasValue && _lastContentState.Value == isActive) return;
        _lastContentState = isActive;

        if (!animate)
        {
            ActiveContentGrid.Opacity = isActive ? 1 : 0;
            ActiveContentGrid.IsHitTestVisible = isActive;
            IdleContentGrid.Opacity = isActive ? 0 : 1;
            IdleContentGrid.IsHitTestVisible = !isActive;
            return;
        }

        // Compute the translation that maps IdleStationLogoGrid's center to ActiveStationIconGrid's center.
        Point idlePt = IdleStationLogoGrid.TransformToVisual(WindowLayout).TransformPoint(new Point(0, 0));
        Point activePt = ActiveStationIconGrid.TransformToVisual(WindowLayout).TransformPoint(new Point(0, 0));
        double offsetX = (activePt.X + SmallIconSize / 2) - (idlePt.X + LargeLogoSize / 2);
        double offsetY = (activePt.Y + SmallIconSize / 2) - (idlePt.Y + LargeLogoSize / 2);

        _morphStoryboard?.Stop();

        if (isActive)
        {
            // Idle → Active: large logo shrinks and flies to station row icon.
            ActiveContentGrid.Opacity = 1;
            ActiveContentGrid.IsHitTestVisible = true;
            IdleContentGrid.IsHitTestVisible = false;

            CompositeTransform transform = new() { CenterX = LargeLogoSize / 2, CenterY = LargeLogoSize / 2 };
            IdleStationLogoGrid.RenderTransform = transform;

            Storyboard sb = BuildMorphStoryboard(transform,
                fromScale: 1.0, toScale: SmallToLargeScale,
                fromTx: 0, toTx: offsetX,
                fromTy: 0, toTy: offsetY,
                easeMode: EasingMode.EaseIn);
            sb.Completed += (_, _) =>
            {
                if (_isClosed) return;
                IdleContentGrid.Opacity = 0;
                IdleStationLogoGrid.RenderTransform = null;
            };
            _morphStoryboard = sb;
            sb.Begin();
        }
        else
        {
            // Active → Idle: station row icon grows and expands to center logo.
            CompositeTransform transform = new()
            {
                CenterX = LargeLogoSize / 2,
                CenterY = LargeLogoSize / 2,
                ScaleX = SmallToLargeScale,
                ScaleY = SmallToLargeScale,
                TranslateX = offsetX,
                TranslateY = offsetY
            };
            IdleStationLogoGrid.RenderTransform = transform;

            IdleContentGrid.Opacity = 1;
            IdleContentGrid.IsHitTestVisible = true;
            ActiveContentGrid.IsHitTestVisible = false;

            Storyboard sb = BuildMorphStoryboard(transform,
                fromScale: SmallToLargeScale, toScale: 1.0,
                fromTx: offsetX, toTx: 0,
                fromTy: offsetY, toTy: 0,
                easeMode: EasingMode.EaseOut);

            // Fold the active-content fade into the same storyboard so Stop() cancels both atomically.
            DoubleAnimation fadeAnim = new() { To = 0, Duration = MorphDuration, EnableDependentAnimation = true };
            sb.Children.Add(fadeAnim);
            Storyboard.SetTarget(fadeAnim, ActiveContentGrid);
            Storyboard.SetTargetProperty(fadeAnim, "Opacity");
            sb.Completed += (_, _) =>
            {
                if (_isClosed) return;
                IdleStationLogoGrid.RenderTransform = null;
                ActiveContentGrid.Opacity = 0;
            };
            _morphStoryboard = sb;
            sb.Begin();
        }
    }

    private static Storyboard BuildMorphStoryboard(
        CompositeTransform target,
        double fromScale, double toScale,
        double fromTx, double toTx,
        double fromTy, double toTy,
        EasingMode easeMode)
    {
        CubicEase easing = new() { EasingMode = easeMode };
        Storyboard sb = new();

        void Anim(string prop, double from, double to)
        {
            DoubleAnimation da = new()
            {
                From = from,
                To = to,
                Duration = MorphDuration,
                EasingFunction = easing,
                EnableDependentAnimation = true
            };
            sb.Children.Add(da);
            Storyboard.SetTarget(da, target);
            Storyboard.SetTargetProperty(da, prop);
        }

        Anim("ScaleX", fromScale, toScale);
        Anim("ScaleY", fromScale, toScale);
        Anim("TranslateX", fromTx, toTx);
        Anim("TranslateY", fromTy, toTy);
        return sb;
    }

    private void VisualizerToggleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = VisualizerToggleMenuItem.IsChecked;
        SpectrumVisualizer.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        SettingsService.IsMiniPlayerVisualizerEnabled = enabled;
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Toggle();
    }

    private void PauseAndCloseButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Pause();
        Close();
    }

    private void FavoriteTrackButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleCurrentTrackFavorite();
    }

    private void WindowLayout_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_isClosed) return;
        if (e.Pointer.PointerDeviceType is Microsoft.UI.Input.PointerDeviceType.Mouse)
        {
            ShowOverlayControls();
        }
    }

    private void WindowLayout_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_isClosed) return;
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
        {
            HideOverlayControls();
        }
    }

    private void WindowLayout_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_isClosed) return;
        if (e.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse)
        {
            ShowOverlayControls();
            _touchOverlayTimer.Stop();
            _touchOverlayTimer.Start();
        }
    }

    private void TouchOverlayTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_isClosed) return;
        _touchOverlayTimer.Stop();
        HideOverlayControls();
    }

    private void ShowOverlayControls()
    {
        AnimateOverlayControls(1);
        HoverControlsOverlay.IsHitTestVisible = true;
    }

    private void HideOverlayControls()
    {
        _touchOverlayTimer.Stop();
        AnimateOverlayControls(0);
    }

    private void AnimateOverlayControls(double targetOpacity)
    {
        _hoverControlsStoryboard?.Stop();

        DoubleAnimation animation = new()
        {
            To = targetOpacity,
            Duration = OverlayFadeDuration,
            EnableDependentAnimation = true
        };

        Storyboard storyboard = new();
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, HoverControlsOverlay);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Completed += (_, _) =>
        {
            if (_isClosed) return;
            if (targetOpacity == 0)
            {
                HoverControlsOverlay.IsHitTestVisible = false;
            }
        };

        _hoverControlsStoryboard = storyboard;
        storyboard.Begin();
    }
}
