using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using clicky_windows.Models;

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
        private readonly System.Collections.Generic.List<Control> _drawings = new();

        public OverlayWindow()
        {
            InitializeComponent();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _timer.Tick += OnAnimationTick;

            CompanionManager.Instance.PropertyChanged += OnManagerPropertyChanged;
            CompanionManager.Instance.DrawRequested += OnDrawRequested;
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

            CursorCompanion.IsVisible = CompanionManager.Instance.IsOverlayVisible;
            _timer.Start();
        }

        private void OnManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CompanionManager.IsOverlayVisible))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    bool visible = CompanionManager.Instance.IsOverlayVisible;
                    CursorCompanion.IsVisible = visible;
                    if (visible)
                    {
                        Show();
                    }
                    else
                    {
                        Hide();
                        ClearAllDrawings();
                    }
                });
            }
        }

        private void ClearAllDrawings()
        {
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var control in _drawings)
                {
                    MainCanvas.Children.Remove(control);
                }
                _drawings.Clear();
            });
        }

        private void OnDrawRequested(AnnotationInstruction instruction)
        {
            if (instruction == null || _targetScreen == null) return;

            if (instruction.Shape == "CLEAR")
            {
                ClearAllDrawings();
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                // Translate global pixel coords to screen-local bounds
                double screenPixelX = _targetScreen.Bounds.X * _targetScreen.Scaling;
                double screenPixelY = _targetScreen.Bounds.Y * _targetScreen.Scaling;

                double lx1 = (instruction.X1 - screenPixelX) / _targetScreen.Scaling;
                double ly1 = (instruction.Y1 - screenPixelY) / _targetScreen.Scaling;
                double lx2 = (instruction.X2 - screenPixelX) / _targetScreen.Scaling;
                double ly2 = (instruction.Y2 - screenPixelY) / _targetScreen.Scaling;
                double lRadius = instruction.Radius / _targetScreen.Scaling;

                Control? shapeControl = null;

                if (instruction.Shape == "ARROW")
                {
                    double angle = Math.Atan2(ly2 - ly1, lx2 - lx1);
                    double arrowLength = 15;
                    double arrowAngle = Math.PI / 6;

                    var path = new Path
                    {
                        Stroke = new SolidColorBrush(Color.Parse("#FF9500")), // Apple Orange
                        StrokeThickness = 3.5,
                        StrokeJoin = PenLineJoin.Round,
                        StrokeLineCap = PenLineCap.Round,
                        Opacity = 0
                    };
                    shapeControl = path;

                    MainCanvas.Children.Add(path);
                    _drawings.Add(path);

                    Task.Run(async () =>
                    {
                        int steps = 15;
                        for (int i = 1; i <= steps; i++)
                        {
                            double t = (double)i / steps;
                            double cx2 = lx1 + (lx2 - lx1) * t;
                            double cy2 = ly1 + (ly2 - ly1) * t;
                            
                            double cAngle = Math.Atan2(cy2 - ly1, cx2 - lx1);
                            double cx3 = cx2 - arrowLength * Math.Cos(cAngle - arrowAngle);
                            double cy3 = cy2 - arrowLength * Math.Sin(cAngle - arrowAngle);
                            double cx4 = cx2 - arrowLength * Math.Cos(cAngle + arrowAngle);
                            double cy4 = cy2 - arrowLength * Math.Sin(cAngle + arrowAngle);
                            
                            string geom = $"M {lx1} {ly1} L {cx2} {cy2}";
                            if (i == steps)
                            {
                                geom += $" M {cx3} {cy3} L {cx2} {cy2} L {cx4} {cy4}";
                            }
                            
                            Dispatcher.UIThread.Post(() =>
                            {
                                path.Data = Geometry.Parse(geom);
                                path.Opacity = t;
                            });
                            await Task.Delay(20);
                        }
                        StartFadeOut(path, instruction.DurationMs);
                    });
                }
                else if (instruction.Shape == "LINE")
                {
                    var path = new Path
                    {
                        Stroke = new SolidColorBrush(Color.Parse("#FF3B30")), // Apple Red
                        StrokeThickness = 3,
                        StrokeJoin = PenLineJoin.Round,
                        StrokeLineCap = PenLineCap.Round,
                        Opacity = 0
                    };
                    shapeControl = path;

                    MainCanvas.Children.Add(path);
                    _drawings.Add(path);

                    Task.Run(async () =>
                    {
                        int steps = 15;
                        for (int i = 1; i <= steps; i++)
                        {
                            double t = (double)i / steps;
                            double cx2 = lx1 + (lx2 - lx1) * t;
                            double cy2 = ly1 + (ly2 - ly1) * t;
                            Dispatcher.UIThread.Post(() =>
                            {
                                path.Data = Geometry.Parse($"M {lx1} {ly1} L {cx2} {cy2}");
                                path.Opacity = t;
                            });
                            await Task.Delay(20);
                        }
                        StartFadeOut(path, instruction.DurationMs);
                    });
                }
                else if (instruction.Shape == "CIRCLE")
                {
                    var ellipse = new Ellipse
                    {
                        Width = lRadius * 2,
                        Height = lRadius * 2,
                        Stroke = new SolidColorBrush(Color.Parse("#0A84FF")), // Apple Blue
                        StrokeThickness = 3,
                        Opacity = 0
                    };
                    shapeControl = ellipse;
                    Canvas.SetLeft(ellipse, lx1 - lRadius);
                    Canvas.SetTop(ellipse, ly1 - lRadius);
                    MainCanvas.Children.Add(ellipse);
                    _drawings.Add(ellipse);

                    Task.Run(async () =>
                    {
                        int steps = 15;
                        for (int i = 1; i <= steps; i++)
                        {
                            double t = (double)i / steps;
                            Dispatcher.UIThread.Post(() =>
                            {
                                ellipse.Opacity = t;
                                double scale = 0.8 + 0.2 * t;
                                ellipse.Width = lRadius * 2 * scale;
                                ellipse.Height = lRadius * 2 * scale;
                                Canvas.SetLeft(ellipse, lx1 - (lRadius * scale));
                                Canvas.SetTop(ellipse, ly1 - (lRadius * scale));
                            });
                            await Task.Delay(20);
                        }
                        StartFadeOut(ellipse, instruction.DurationMs);
                    });
                }
                else if (instruction.Shape == "RECTANGLE")
                {
                    var rect = new Border
                    {
                        Width = lx2,
                        Height = ly2,
                        BorderBrush = new SolidColorBrush(Color.Parse("#30D158")), // Apple Green
                        BorderThickness = new Thickness(3),
                        CornerRadius = new CornerRadius(8),
                        Opacity = 0
                    };
                    shapeControl = rect;
                    Canvas.SetLeft(rect, lx1);
                    Canvas.SetTop(rect, ly1);
                    MainCanvas.Children.Add(rect);
                    _drawings.Add(rect);

                    Task.Run(async () =>
                    {
                        int steps = 15;
                        for (int i = 1; i <= steps; i++)
                        {
                            double t = (double)i / steps;
                            Dispatcher.UIThread.Post(() => rect.Opacity = t);
                            await Task.Delay(20);
                        }
                        StartFadeOut(rect, instruction.DurationMs);
                    });
                }
                else if (instruction.Shape == "HIGHLIGHT")
                {
                    var highlight = new Border
                    {
                        Width = lx2,
                        Height = ly2,
                        Background = new SolidColorBrush(Color.Parse("#FFCC00")), // Yellow/Amber
                        Opacity = 0,
                        CornerRadius = new CornerRadius(4)
                    };
                    shapeControl = highlight;
                    Canvas.SetLeft(highlight, lx1);
                    Canvas.SetTop(highlight, ly1);
                    MainCanvas.Children.Add(highlight);
                    _drawings.Add(highlight);

                    Task.Run(async () =>
                    {
                        int steps = 15;
                        for (int i = 1; i <= steps; i++)
                        {
                            double t = (double)i / steps;
                            Dispatcher.UIThread.Post(() => highlight.Opacity = t * 0.25);
                            await Task.Delay(20);
                        }
                        await Task.Delay(instruction.DurationMs);
                        for (int i = 10; i >= 0; i--)
                        {
                            await Task.Delay(30);
                            Dispatcher.UIThread.Post(() => highlight.Opacity = (i / 10.0) * 0.25);
                        }
                        Dispatcher.UIThread.Post(() =>
                        {
                            MainCanvas.Children.Remove(highlight);
                            _drawings.Remove(highlight);
                        });
                    });
                }
                else if (instruction.Shape == "BADGE")
                {
                    var badge = new Border
                    {
                        Width = 28,
                        Height = 28,
                        Background = new SolidColorBrush(Color.Parse("#BF5AF2")), // Apple Purple
                        CornerRadius = new CornerRadius(14),
                        Opacity = 0,
                        Child = new TextBlock
                        {
                            Text = instruction.Text,
                            Foreground = Brushes.White,
                            FontSize = 14,
                            FontWeight = FontWeight.Bold,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        }
                    };
                    shapeControl = badge;
                    Canvas.SetLeft(badge, lx1 - 14);
                    Canvas.SetTop(badge, ly1 - 14);
                    MainCanvas.Children.Add(badge);
                    _drawings.Add(badge);

                    Task.Run(async () =>
                    {
                        int steps = 15;
                        for (int i = 1; i <= steps; i++)
                        {
                            double t = (double)i / steps;
                            Dispatcher.UIThread.Post(() => badge.Opacity = t);
                            await Task.Delay(20);
                        }
                        StartFadeOut(badge, instruction.DurationMs);
                    });
                }
                else if (instruction.Shape == "TEXT")
                {
                    var txt = new TextBlock
                    {
                        Text = instruction.Text,
                        Foreground = Brushes.White,
                        Background = new SolidColorBrush(Color.Parse("#1C1C1E")) { Opacity = 0.85 },
                        Padding = new Thickness(6, 4),
                        FontSize = 13,
                        FontWeight = FontWeight.Medium,
                        Opacity = 0
                    };
                    shapeControl = txt;
                    Canvas.SetLeft(txt, lx1);
                    Canvas.SetTop(txt, ly1);
                    MainCanvas.Children.Add(txt);
                    _drawings.Add(txt);

                    Task.Run(async () =>
                    {
                        int steps = 15;
                        for (int i = 1; i <= steps; i++)
                        {
                            double t = (double)i / steps;
                            Dispatcher.UIThread.Post(() => txt.Opacity = t);
                            await Task.Delay(20);
                        }
                        StartFadeOut(txt, instruction.DurationMs);
                    });
                }
                else if (instruction.Shape == "SVG" && !string.IsNullOrWhiteSpace(instruction.PathData))
                {
                    try
                    {
                        var path = new Path
                        {
                            Data = Geometry.Parse(instruction.PathData),
                            Stroke = new SolidColorBrush(Color.Parse("#FF9500")), // Orange
                            StrokeThickness = 3,
                            StrokeJoin = PenLineJoin.Round,
                            StrokeLineCap = PenLineCap.Round,
                            Opacity = 0
                        };
                        shapeControl = path;

                        if (lx2 > 0 && ly2 > 0)
                        {
                            path.Width = lx2;
                            path.Height = ly2;
                            path.Stretch = Stretch.Uniform;
                        }

                        Canvas.SetLeft(path, lx1);
                        Canvas.SetTop(path, ly1);
                        MainCanvas.Children.Add(path);
                        _drawings.Add(path);

                        Task.Run(async () =>
                        {
                            int steps = 15;
                            for (int i = 1; i <= steps; i++)
                            {
                                double t = (double)i / steps;
                                Dispatcher.UIThread.Post(() => path.Opacity = t);
                                await Task.Delay(20);
                            }
                            StartFadeOut(path, instruction.DurationMs);
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ SVG parse error in visual engine: {ex.Message}");
                    }
                }
            });
        }

        private async void StartFadeOut(Control control, int delayMs)
        {
            await Task.Delay(delayMs);
            for (int i = 10; i >= 0; i--)
            {
                await Task.Delay(30);
                Dispatcher.UIThread.Post(() => control.Opacity = i / 10.0);
            }
            Dispatcher.UIThread.Post(() =>
            {
                MainCanvas.Children.Remove(control);
                _drawings.Remove(control);
            });
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
            CompanionManager.Instance.DrawRequested -= OnDrawRequested;
            base.OnClosing(e);
        }
    }
}
