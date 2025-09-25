using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Tradio.ViewModels;
using Microsoft.UI.Windowing;

namespace Tradio
{
    public sealed partial class MainWindow : Window
    {
        private readonly PlayerViewModel _vm = new();

        public MainWindow()
        {
            InitializeComponent();
            WindowHelper.Track(this);

            // Intercept window closing to just hide instead of exiting the app
            try
            {
                AppWindow.Closing += OnAppWindowClosing;
            }
            catch { /* AppWindow may not be available in some environments */ }

            UpdatePlayPauseButton();
            VolumeSlider.Value = _vm.Volume;
            VolumeValue.Text = ((int)(_vm.Volume * 100)).ToString();

            _vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PlayerViewModel.IsPlaying))
                {
                    UpdatePlayPauseButton();
                }
                else if (args.PropertyName == nameof(PlayerViewModel.Volume))
                {
                    if (Math.Abs(VolumeSlider.Value - _vm.Volume) > 0.0001)
                        VolumeSlider.Value = _vm.Volume;
                    VolumeValue.Text = ((int)(_vm.Volume * 100)).ToString();
                }
            };
        }

        private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // Cancel the close and just hide the window so playback continues from tray
            args.Cancel = true;
            try { sender.Hide(); } catch { }
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            _vm.Toggle();
            UpdatePlayPauseButton();
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            _vm.Volume = e.NewValue;
            VolumeValue.Text = ((int)(_vm.Volume * 100)).ToString();
        }

        private void UpdatePlayPauseButton()
        {
            if (PlayPauseButton is not null)
            {
                PlayPauseButton.Content = _vm.IsPlaying ? "Pause" : "Play";
            }
        }
    }
}
