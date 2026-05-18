using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Trdo.ViewModels;
using WinUIEx;

namespace Trdo.Controls;

public sealed partial class MiniPlayerWindow : WindowEx
{
    private const int WindowWidth = 360;
    private const int WindowHeight = 220;

    public PlayerViewModel ViewModel { get; }

    public MiniPlayerWindow()
    {
        InitializeComponent();

        ViewModel = PlayerViewModel.Shared;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(ModernTitlebar);
        AppWindow.SetIcon("Assets\\Radio.ico");
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

        CompactOverlayPresenter presenter = CompactOverlayPresenter.Create();
        presenter.InitialSize = CompactOverlaySize.Medium;
        AppWindow.SetPresenter(presenter);
        // AppWindow.Resize(new Windows.Graphics.SizeInt32(WindowWidth, WindowHeight));
    }

    private void PlayPauseButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.Toggle();
    }

    private void PauseAndCloseButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.Pause();
        Close();
    }

    private void WindowLayout_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType is Microsoft.UI.Input.PointerDeviceType.Mouse)
        {
            HoverPlayPauseButton.Visibility = Visibility.Visible;
        }
    }

    private void WindowLayout_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
        {
            HoverPlayPauseButton.Visibility = Visibility.Collapsed;
        }
    }
}
