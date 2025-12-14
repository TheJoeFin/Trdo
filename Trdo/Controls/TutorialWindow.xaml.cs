using WinUIEx;

namespace Trdo.Controls;
/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class TutorialWindow : WindowEx
{
    public TutorialWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(ModernTitlebar);
    }

    private void Button_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Close();

        if (App.Current is App currentApp)
            currentApp.TryShowFlyout();
    }
}
