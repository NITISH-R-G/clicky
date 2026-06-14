using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using clicky_windows.Views;

namespace clicky_windows
{
    public partial class App : Application
    {
        private readonly List<OverlayWindow> _overlays = new();
        private SettingsWindow? _settingsWindow;
        private TrayIcon? _trayIcon;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                // 1. Initialize and Start Core Manager
                var manager = CompanionManager.Instance;
                manager.Start();

                // 2. Initialize Settings Window (hidden by default)
                _settingsWindow = new SettingsWindow();

                // 3. Initialize Overlay Windows for all screens using SettingsWindow's Screen list
                foreach (var screen in _settingsWindow.Screens.All)
                {
                    var overlay = new OverlayWindow(screen);
                    _overlays.Add(overlay);
                    if (manager.IsOverlayVisible)
                    {
                        overlay.Show();
                    }
                }

                // 4. Initialize Programmatic System Tray Icon
                InitializeTrayIcon();

                Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
                Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;

                desktop.Exit += (sender, args) =>
                {
                    Cleanup();
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void InitializeTrayIcon()
        {
            try
            {
                // Open the default logo icon from assets using Avalonia 11 static AssetLoader
                using var iconStream = AssetLoader.Open(new Uri("avares://clicky-windows/Assets/avalonia-logo.ico"));
                var windowIcon = new WindowIcon(iconStream);

                _trayIcon = new TrayIcon
                {
                    Icon = windowIcon,
                    ToolTipText = "Clicky Companion"
                };

                // Click event on tray icon toggles settings pop-up
                _trayIcon.Clicked += (sender, args) =>
                {
                    ToggleSettingsWindow();
                };

                // Build Tray Menu
                var menu = new NativeMenu();

                var toggleItem = new NativeMenuItem("Show Clicky Cursor");
                toggleItem.Click += (sender, args) =>
                {
                    CompanionManager.Instance.IsClickyCursorEnabled = !CompanionManager.Instance.IsClickyCursorEnabled;
                };

                var settingsItem = new NativeMenuItem("Settings...");
                settingsItem.Click += (sender, args) =>
                {
                    ShowSettingsWindow();
                };

                var separator = new NativeMenuItemSeparator();

                var quitItem = new NativeMenuItem("Quit Clicky");
                quitItem.Click += (sender, args) =>
                {
                    CompanionManager.Instance.Stop();
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown();
                    }
                };

                menu.Items.Add(toggleItem);
                menu.Items.Add(settingsItem);
                menu.Items.Add(separator);
                menu.Items.Add(quitItem);

                _trayIcon.Menu = menu;

                // Register TrayIcon with the application
                var trayIcons = TrayIcon.GetIcons(Application.Current!);
                trayIcons.Add(_trayIcon);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to initialize Tray Icon: {ex.Message}");
            }
        }

        private void ShowSettingsWindow()
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow();
            }
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        private void ToggleSettingsWindow()
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow();
            }

            if (_settingsWindow.IsVisible)
            {
                _settingsWindow.Hide();
            }
            else
            {
                ShowSettingsWindow();
            }
        }

        private void Cleanup()
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;

            CompanionManager.Instance.Stop();

            foreach (var overlay in _overlays)
            {
                overlay.Close();
            }
            _overlays.Clear();

            if (_settingsWindow != null)
            {
                _settingsWindow.Close();
                _settingsWindow = null;
            }

            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }

        private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
            {
                Console.WriteLine("🔌 System suspending. Suspending Clicky hooks.");
                CompanionManager.Instance.Stop();
            }
            else if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            {
                Console.WriteLine("🔌 System resumed. Re-initializing Clicky hooks.");
                CompanionManager.Instance.Start();
            }
        }

        private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
            {
                Console.WriteLine("🔒 Session locked. Suspending Clicky hooks.");
                CompanionManager.Instance.Stop();
            }
            else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
            {
                Console.WriteLine("🔓 Session unlocked. Re-initializing Clicky hooks.");
                CompanionManager.Instance.Start();
            }
        }
    }
}