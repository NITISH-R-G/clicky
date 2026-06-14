using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace clicky_windows.Views
{
    public partial class OverlayWindow : Window
    {
        // Win32 API constants
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private Screen? _targetScreen;
        private readonly DispatcherTimer _timer;
        private double _currentX = 0;
        private double _currentY = 0;
        private readonly Random _random = new();

        public OverlayWindow()
        {
            InitializeComponent();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _timer.Tick += OnAnimationTick;

            CompanionManager.Instance.PropertyChanged += OnManagerPropertyChanged;
        }

        public OverlayWindow(Screen screen) : this()
        {
            _targetScreen = screen;

            // Position to cover the screen
            var bounds = screen.Bounds;
            Position = bounds.Position;
            Width = bounds.Width / screen.Scaling;
            Height = bounds.Height / screen.Scaling;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // Make window click-through (Win32 specific)
            var handle = TryGetPlatformHandle();
            if (handle != null)
            {
                int initialStyle = GetWindowLong(handle.Handle, GWL_EXSTYLE);
                SetWindowLong(handle.Handle, GWL_EXSTYLE, initialStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
            }

            _timer.Start();
        }

        private void OnManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CompanionManager.IsOverlayVisible))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (CompanionManager.Instance.IsOverlayVisible)
                        Show();
                    else
                        Hide();
                });
            }
        }

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            var manager = CompanionManager.Instance;
            if (!manager.IsOverlayVisible || _targetScreen == null) return;

            // Determine target position
            double targetX, targetY;

            if (manager.PointX.HasValue && manager.PointY.HasValue)
            {
                // Flight/Pointing Mode: fly to target coordinate
                // Map global coords to screen-local coords (doing the subtraction in pixel space)
                double screenPixelX = _targetScreen.Bounds.X * _targetScreen.Scaling;
                double screenPixelY = _targetScreen.Bounds.Y * _targetScreen.Scaling;
                targetX = (manager.PointX.Value - screenPixelX) / _targetScreen.Scaling;
                targetY = (manager.PointY.Value - screenPixelY) / _targetScreen.Scaling;

                // Show speech bubble
                SpeechBubble.IsVisible = !string.IsNullOrWhiteSpace(manager.PointLabel);
                BubbleText.Text = manager.PointLabel ?? "";
            }
            else
            {
                // Mouse-following Mode (doing the subtraction in pixel space)
                GetCursorPos(out var mousePoint);
                double screenPixelX = _targetScreen.Bounds.X * _targetScreen.Scaling;
                double screenPixelY = _targetScreen.Bounds.Y * _targetScreen.Scaling;
                targetX = (mousePoint.X - screenPixelX) / _targetScreen.Scaling;
                targetY = (mousePoint.Y - screenPixelY) / _targetScreen.Scaling;

                // Adjust slightly so it floats next to the pointer
                targetX += 16;
                targetY += 16;

                // Speech bubble shows Claude's response text if responding, or empty
                if (manager.VoiceState == CompanionVoiceState.Responding && !string.IsNullOrWhiteSpace(manager.LastTranscript))
                {
                    SpeechBubble.IsVisible = true;
                    // Limit text length in bubble
                    BubbleText.Text = manager.LastTranscript.Length > 60 ? manager.LastTranscript.Substring(0, 57) + "..." : manager.LastTranscript;
                }
                else
                {
                    SpeechBubble.IsVisible = false;
                }
            }

            // Smooth Interpolation (lerp)
            double easing = 0.15; // smooth factor
            _currentX += (targetX - _currentX) * easing;
            _currentY += (targetY - _currentY) * easing;

            // Set positions on Canvas
            Canvas.SetLeft(CursorCompanion, _currentX - (CursorCompanion.Width / 2));
            Canvas.SetTop(CursorCompanion, _currentY - 30); // Offset to center triangle at the coordinate

            // Waveform animation
            if (manager.VoiceState == CompanionVoiceState.Listening || manager.VoiceState == CompanionVoiceState.Processing)
            {
                WaveformPanel.IsVisible = true;
                double power = manager.CurrentAudioPowerLevel;

                foreach (var child in WaveformPanel.Children)
                {
                    if (child is Rectangle rect)
                    {
                        // Animate height based on audio level + organic noise
                        double targetHeight = 4 + (power * 24 * (_random.NextDouble() * 0.6 + 0.4));
                        rect.Height += (targetHeight - rect.Height) * 0.3;
                    }
                }
            }
            else
            {
                WaveformPanel.IsVisible = false;
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _timer.Stop();
            CompanionManager.Instance.PropertyChanged -= OnManagerPropertyChanged;
            base.OnClosing(e);
        }
    }
}
