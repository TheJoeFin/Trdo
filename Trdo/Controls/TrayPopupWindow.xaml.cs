using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Runtime.InteropServices;
using System.Text;
using Trdo.Pages;
using Trdo.Services;
using WinRT.Interop;
using WinUIEx;

namespace Trdo.Controls;

/// <summary>
/// Borderless popup window shown near the tray icon / click position,
/// replacing the WinUIEx tray flyout. Light-dismisses when focus leaves.
/// </summary>
public sealed partial class TrayPopupWindow : WindowEx
{
    private const int PopupWidth = 320;
    private const int PopupHeight = 500;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint GA_ROOT = 2;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    /// <summary>
    /// A tray click that arrives right after a light-dismiss hide is the same
    /// click that dismissed the popup — treat it as "close" instead of re-showing.
    /// </summary>
    private static readonly TimeSpan RecentDismissWindow = TimeSpan.FromMilliseconds(300);

    private readonly DispatcherQueueTimer _hideTimer;
    private ShellPage? _shellPage;
    private bool _isShowing;
    private bool _isPopupVisible;
    private DateTime _lastDismissedAtUtc = DateTime.MinValue;

    public TrayPopupWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon("Assets\\Radio.ico");
        ConfigurePresenter();
        // ConfigureToolWindow();

        Activated += OnWindowActivated;

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(150);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => HideIfFocusLeftWindow();
    }

    public bool IsPopupVisible => _isPopupVisible;

    /// <summary>
    /// Shows the popup near the last captured pointer anchor, or hides it when
    /// it is already visible (or was just light-dismissed by the same click).
    /// </summary>
    public void ToggleNearAnchor()
    {
        if (_isPopupVisible)
        {
            HidePopup();
            return;
        }

        if (DateTime.UtcNow - _lastDismissedAtUtc < RecentDismissWindow)
            return;

        ShowNearAnchor();
    }

    public void ShowNearAnchor()
    {
        EnsureShellPage();

        // Reset navigation on each open, matching the old flyout behavior.
        NavigationService.Instance.ClearBackStack();
        _shellPage?.ViewModel.NavigateToPlayingPage();

        // Position before showing so the window never flashes at a stale location.
        WindowPlacementService.PositionWindowNearAnchor(this, PopupWidth, PopupHeight);

        _hideTimer.Stop();
        _isShowing = true;
        _isPopupVisible = true;

        this.Show();
        Activate();
        SetForegroundWindow(WindowNative.GetWindowHandle(this));
    }

    public void HidePopup()
    {
        _hideTimer.Stop();
        _isShowing = false;

        if (!_isPopupVisible)
            return;

        _isPopupVisible = false;
        _lastDismissedAtUtc = DateTime.UtcNow;
        this.Hide();
    }

    private void EnsureShellPage()
    {
        // One ShellPage kept alive across show/hide: reopening is instant and the
        // XamlRoot stays valid for ContentDialogs opened from hosted pages.
        if (_shellPage is null)
        {
            _shellPage = new ShellPage();
            ShellHost.Content = _shellPage;
        }
    }

    private void ConfigurePresenter()
    {
        OverlappedPresenter presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = true;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        nint hwnd = WindowNative.GetWindowHandle(this);
        int cornerPreference = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
    }

    private void ConfigureToolWindow()
    {
        // No taskbar button, no Alt-Tab entry.
        nint hwnd = WindowNative.GetWindowHandle(this);
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        _ = SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

        // The presenter's SetBorderAndTitleBar(false, false) can leave the
        // caption/resize-frame styles behind, which DWM renders as a visible
        // frame around the popup. Strip them and the 1px DWM border directly.
        int style = GetWindowLong(hwnd, GWL_STYLE);
        _ = SetWindowLong(hwnd, GWL_STYLE, style & ~(WS_CAPTION | WS_THICKFRAME));

        int borderColor = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _isShowing = false;
            _hideTimer.Stop();
            return;
        }

        if (_isShowing || !_isPopupVisible)
            return;

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideIfFocusLeftWindow()
    {
        _hideTimer.Stop();

        if (!_isPopupVisible)
            return;

        nint foreground = GetForegroundWindow();
        if (ShouldRemainVisible(foreground))
            return;

        HidePopup();
    }

    private bool ShouldRemainVisible(nint foregroundWindow)
    {
        if (foregroundWindow == 0)
            return false;

        nint windowHandle = WindowNative.GetWindowHandle(this);
        if (foregroundWindow == windowHandle)
            return true;

        if (GetAncestor(foregroundWindow, GA_ROOT) == windowHandle)
            return true;

        // WinUI MenuFlyouts/ComboBox dropdowns open in separate popup HWNDs;
        // classic context menus use the #32768 class. Neither should dismiss us.
        return IsTransientPopupWindow(foregroundWindow);
    }

    private static bool IsTransientPopupWindow(nint hwnd)
    {
        StringBuilder className = new(64);
        if (GetClassName(hwnd, className, className.Capacity) == 0)
            return false;

        string name = className.ToString();
        return string.Equals(name, "#32768", StringComparison.Ordinal)
            || string.Equals(name, "Xaml_WindowedPopupClass", StringComparison.Ordinal);
    }

    private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        HidePopup();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hWnd, uint gaFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder className, int maxCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
