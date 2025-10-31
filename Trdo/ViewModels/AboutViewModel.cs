using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.System;

namespace Trdo.ViewModels;

public partial class AboutViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string AppName => "Trdo";
    public string AppDescription => "A simple, elegant internet radio player for Windows";
    public string Version
    {
        get
        {
            PackageVersion version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public string GitHubUrl => "https://github.com/TheJoeFin/Trdo";
    public string GitHubDisplayText => "github.com/TheJoeFin/Trdo";

    public string DeveloperName => "Joe Finney (TheJoeFin)";
    public string DeveloperGitHub => "https://github.com/TheJoeFin";

    public async Task OpenGitHub()
    {
        await Launcher.LaunchUriAsync(new Uri(GitHubUrl));
    }

    public async Task OpenDeveloperGitHub()
    {
        await Launcher.LaunchUriAsync(new Uri(DeveloperGitHub));
    }

    public async Task OpenRatingWindow()
    {
        _ = await Launcher.LaunchUriAsync(new Uri("ms-windows-store://review/?ProductId=9NXT4TGJVHVV"));
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
