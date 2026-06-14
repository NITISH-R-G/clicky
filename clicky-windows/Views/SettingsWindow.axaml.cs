using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace clicky_windows.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            
            // Set position to bottom right of screen
            var screen = Screens.Primary;
            if (screen != null)
            {
                var workArea = screen.WorkingArea;
                // Position above taskbar
                Position = new PixelPoint(
                    workArea.Right - (int)(Width * screen.Scaling),
                    workArea.Bottom - (int)(Height * screen.Scaling)
                );
            }

            // Sync with CompanionManager
            var manager = CompanionManager.Instance;
            ClickyCursorToggle.IsChecked = manager.IsClickyCursorEnabled;
            ClickyCursorToggle.IsCheckedChanged += (s, e) => manager.IsClickyCursorEnabled = ClickyCursorToggle.IsChecked == true;

            if (manager.SelectedModel == "claude-sonnet-4-6")
                SonnetRadio.IsChecked = true;
            else
                OpusRadio.IsChecked = true;

            SonnetRadio.IsCheckedChanged += (s, e) => { if (SonnetRadio.IsChecked == true) manager.SelectedModel = "claude-sonnet-4-6"; };
            OpusRadio.IsCheckedChanged += (s, e) => { if (OpusRadio.IsChecked == true) manager.SelectedModel = "claude-opus-4-6"; };

            // Listen to state changes
            manager.PropertyChanged += OnManagerPropertyChanged;
            UpdateStatusUI(manager.VoiceState);
        }

        private void OnManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CompanionManager.VoiceState))
            {
                Dispatcher.UIThread.Post(() => UpdateStatusUI(CompanionManager.Instance.VoiceState));
            }
        }

        private void UpdateStatusUI(CompanionVoiceState state)
        {
            switch (state)
            {
                case CompanionVoiceState.Idle:
                    StatusDot.Fill = new SolidColorBrush(Color.Parse("#10B981")); // Green
                    StatusText.Text = "Active";
                    break;
                case CompanionVoiceState.Listening:
                    StatusDot.Fill = new SolidColorBrush(Color.Parse("#3B82F6")); // Blue
                    StatusText.Text = "Listening";
                    break;
                case CompanionVoiceState.Processing:
                    StatusDot.Fill = new SolidColorBrush(Color.Parse("#F59E0B")); // Orange
                    StatusText.Text = "Processing";
                    break;
                case CompanionVoiceState.Responding:
                    StatusDot.Fill = new SolidColorBrush(Color.Parse("#8B5CF6")); // Purple
                    StatusText.Text = "Responding";
                    break;
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void OnQuitClick(object sender, RoutedEventArgs e)
        {
            CompanionManager.Instance.Stop();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }
}
