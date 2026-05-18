using Microsoft.UI.Windowing;
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
        AppWindow.Resize(new Windows.Graphics.SizeInt32(WindowWidth, WindowHeight));
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
}
