using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Runtime.InteropServices;
using Trdo.Services;
using WinRT.Interop;
using WinUIEx;

namespace Trdo.Controls;

/// <summary>
/// Transient, non-activating overlay window that briefly announces a song
/// change near the taskbar. A single instance is reused for the lifetime of
/// the app (see App.xaml.cs): repeated calls to <see cref="ShowSongChange"/>
/// update the content and restart the auto-hide timer, replaying the show
/// animation cleanly even if a previous show/hide animation is still in
/// flight.
/// </summary>
public sealed partial class SongChangePopupWindow : Window
{
    private const int WindowWidth = 600;
    private const int WindowHeight = 140;

    private static readonly System.TimeSpan AutoHideDelay = System.TimeSpan.FromMilliseconds(2500);
    private static readonly Duration ShowOpacityDuration = new(System.TimeSpan.FromMilliseconds(200));
    private static readonly Duration ShowSlideDuration = new(System.TimeSpan.FromMilliseconds(250));
    private static readonly Duration HideDuration = new(System.TimeSpan.FromMilliseconds(180));

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int SW_SHOWNA = 8;
    private const int SW_HIDE = 0;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    private readonly DispatcherQueueTimer _autoHideTimer;
    private TranslateTransform? _surfaceTransform;
    private Storyboard? _activeStoryboard;
    private nint _hwnd;
    private bool _isVisible;
    private bool _isConfigured;

    public SongChangePopupWindow()
    {
        InitializeComponent();

        // Makes the window itself see-through so only the rounded pill is
        // visible. A WinUI 3 window cannot be made transparent with the classic
        // DwmExtendFrameIntoClientArea trick — its content is composited over
        // the window's own backdrop, so the backdrop is what has to go.
        SystemBackdrop = new TransparentTintBackdrop();

        _autoHideTimer = DispatcherQueue.CreateTimer();
        _autoHideTimer.Interval = AutoHideDelay;
        _autoHideTimer.IsRepeating = false;
        _autoHideTimer.Tick += AutoHideTimer_Tick;

        Closed += OnWindowClosed;
    }

    /// <summary>
    /// Updates the displayed song text and (re)shows the popup near the
    /// taskbar, restarting the auto-hide timer. Safe to call repeatedly for
    /// consecutive song changes: reuses the same window and replays the show
    /// animation even if a hide animation is currently in flight.
    /// </summary>
    public void ShowSongChange(string displayText)
    {
        if (string.IsNullOrWhiteSpace(displayText))
            return;

        EnsureConfigured();

        SongText.Text = displayText;
        AutomationProperties.SetName(SurfaceBorder, $"Now playing: {displayText}");

        WindowPlacementService.PositionWindowBottomCenter(this, WindowWidth, WindowHeight);

        PlayShowAnimation();

        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }

    /// <summary>
    /// Hides the popup with the hide animation and stops the auto-hide timer.
    /// Safe to call when already hidden.
    /// </summary>
    public void HidePopup()
    {
        _autoHideTimer.Stop();

        if (!_isVisible)
            return;

        PlayHideAnimation();
    }

    /// <summary>
    /// Stops the auto-hide timer and any in-flight animation, and hides the
    /// native window immediately. Intended for the window's own Closed
    /// handler (invoked on the UI thread when the app shuts down and closes
    /// all tracked windows); never called from a finalizer.
    /// </summary>
    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _autoHideTimer.Stop();
        _autoHideTimer.Tick -= AutoHideTimer_Tick;
        _activeStoryboard?.Stop();
        _activeStoryboard = null;

        if (_hwnd != 0)
        {
            _ = ShowWindow(_hwnd, SW_HIDE);
        }
    }

    private void AutoHideTimer_Tick(DispatcherQueueTimer sender, object args) => HidePopup();

    private void EnsureConfigured()
    {
        if (_isConfigured)
            return;

        _isConfigured = true;

        _surfaceTransform = (TranslateTransform)SurfaceBorder.RenderTransform;
        _hwnd = WindowNative.GetWindowHandle(this);

        OverlappedPresenter presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        // No taskbar button, no Alt-Tab entry, never takes activation/focus, and
        // WS_EX_TRANSPARENT so the large see-through area around the pill does
        // not swallow clicks aimed at whatever is underneath. XAML's
        // IsHitTestVisible only covers XAML hit testing, not the HWND's.
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        _ = SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);

        // SetBorderAndTitleBar(false, false) can leave the caption/resize-frame
        // styles behind, which DWM then draws as a frame around the otherwise
        // invisible window — same problem TrayPopupWindow hit. Strip them along
        // with the 1px DWM border.
        int style = GetWindowLong(_hwnd, GWL_STYLE);
        _ = SetWindowLong(_hwnd, GWL_STYLE, style & ~(WS_CAPTION | WS_THICKFRAME));

        int borderColor = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));

        // WinUI only creates and renders the XAML content once the window has
        // been activated; SW_SHOWNA alone leaves it blank. Activating here is
        // safe because WS_EX_NOACTIVATE is already applied (no focus theft) and
        // the pill's own opacity is still 0, so nothing flashes on screen.
        Activate();
        _ = ShowWindow(_hwnd, SW_HIDE);
    }

    private void PlayShowAnimation()
    {
        _activeStoryboard?.Stop();
        _activeStoryboard = null;

        // Force a deterministic starting state before every show so replays
        // are clean regardless of where a previous hide animation left off.
        SurfaceBorder.Opacity = 0;
        _surfaceTransform!.Y = 24;

        CubicEase easeOut = new() { EasingMode = EasingMode.EaseOut };

        DoubleAnimation opacityAnim = new()
        {
            To = 1,
            Duration = ShowOpacityDuration,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(opacityAnim, SurfaceBorder);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        DoubleAnimation slideAnim = new()
        {
            To = 0,
            Duration = ShowSlideDuration,
            EasingFunction = easeOut,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(slideAnim, _surfaceTransform);
        Storyboard.SetTargetProperty(slideAnim, "Y");

        Storyboard sb = new();
        sb.Children.Add(opacityAnim);
        sb.Children.Add(slideAnim);

        _activeStoryboard = sb;
        _isVisible = true;

        // Show without activating/stealing focus, then animate in.
        _ = ShowWindow(_hwnd, SW_SHOWNA);
        sb.Begin();
    }

    private void PlayHideAnimation()
    {
        _activeStoryboard?.Stop();
        _activeStoryboard = null;

        CubicEase easeIn = new() { EasingMode = EasingMode.EaseIn };

        DoubleAnimation opacityAnim = new()
        {
            To = 0,
            Duration = HideDuration,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(opacityAnim, SurfaceBorder);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        DoubleAnimation slideAnim = new()
        {
            To = 12,
            Duration = HideDuration,
            EasingFunction = easeIn,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(slideAnim, _surfaceTransform);
        Storyboard.SetTargetProperty(slideAnim, "Y");

        Storyboard sb = new();
        sb.Children.Add(opacityAnim);
        sb.Children.Add(slideAnim);
        sb.Completed += (_, _) => OnHideAnimationCompleted(sb);

        _activeStoryboard = sb;
        sb.Begin();
    }

    private void OnHideAnimationCompleted(Storyboard sb)
    {
        // A newer Show/Hide call may have already replaced _activeStoryboard;
        // only act if this completion is still the current one.
        if (!ReferenceEquals(_activeStoryboard, sb))
            return;

        _isVisible = false;
        _activeStoryboard = null;

        if (_hwnd != 0)
        {
            _ = ShowWindow(_hwnd, SW_HIDE);
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}