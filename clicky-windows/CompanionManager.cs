using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace clicky_windows
{
    public enum CompanionVoiceState
    {
        Idle,
        Listening,
        Processing,
        Responding
    }

    public class CompanionManager : INotifyPropertyChanged
    {
        private static CompanionManager? _instance;
        public static CompanionManager Instance => _instance ??= new CompanionManager();

        private CompanionVoiceState _voiceState = CompanionVoiceState.Idle;
        public CompanionVoiceState VoiceState
        {
            get => _voiceState;
            set => SetField(ref _voiceState, value);
        }

        private string? _lastTranscript;
        public string? LastTranscript
        {
            get => _lastTranscript;
            set => SetField(ref _lastTranscript, value);
        }

        private double _currentAudioPowerLevel;
        public double CurrentAudioPowerLevel
        {
            get => _currentAudioPowerLevel;
            set => SetField(ref _currentAudioPowerLevel, value);
        }

        private bool _isOverlayVisible;
        public bool IsOverlayVisible
        {
            get => _isOverlayVisible;
            set => SetField(ref _isOverlayVisible, value);
        }

        private string _selectedModel = "claude-sonnet-4-6";
        public string SelectedModel
        {
            get => _selectedModel;
            set => SetField(ref _selectedModel, value);
        }

        private bool _isClickyCursorEnabled = true;
        public bool IsClickyCursorEnabled
        {
            get => _isClickyCursorEnabled;
            set
            {
                if (SetField(ref _isClickyCursorEnabled, value))
                {
                    UpdateOverlayVisibility();
                }
            }
        }

        private bool _hasCompletedOnboarding = true; // Default to true on Windows for simpler onboarding
        public bool HasCompletedOnboarding
        {
            get => _hasCompletedOnboarding;
            set => SetField(ref _hasCompletedOnboarding, value);
        }

        private bool _hasSubmittedEmail = true;
        public bool HasSubmittedEmail
        {
            get => _hasSubmittedEmail;
            set => SetField(ref _hasSubmittedEmail, value);
        }

        // Target pointing state (translated to display points)
        private double? _pointX;
        public double? PointX
        {
            get => _pointX;
            set => SetField(ref _pointX, value);
        }

        private double? _pointY;
        public double? PointY
        {
            get => _pointY;
            set => SetField(ref _pointY, value);
        }

        private string? _pointLabel;
        public string? PointLabel
        {
            get => _pointLabel;
            set => SetField(ref _pointLabel, value);
        }

        private string _bubbleText = "";
        public string BubbleText
        {
            get => _bubbleText;
            set => SetField(ref _bubbleText, value);
        }

        // Change this to your deployed worker URL
        public string WorkerBaseURL { get; set; } = "https://your-worker-name.your-subdomain.workers.dev";

        public List<(string userTranscript, string assistantResponse)> ConversationHistory { get; } = new();

        // Subsystems
        private readonly GlobalHotkey _hotkey = new();
        private readonly AudioRecorder _audioRecorder = new();
        private readonly AssemblyAIClient _assemblyClient = new();
        private readonly ClaudeClient _claudeClient = new();
        private readonly ElevenLabsClient _ttsClient = new();

        private CancellationTokenSource? _pipelineCts;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private CompanionManager()
        {
            _hotkey.Pressed += OnHotkeyPressed;
            _hotkey.Released += OnHotkeyReleased;

            _audioRecorder.PowerLevelChanged += level =>
            {
                CurrentAudioPowerLevel = level;
            };

            _audioRecorder.RecordingStopped += ex =>
            {
                if (ex != null)
                {
                    Console.WriteLine($"🎙️ Audio recording stopped with error: {ex.Message}");
                    VoiceState = CompanionVoiceState.Idle;
                }
            };

            _audioRecorder.DataAvailable += async data =>
            {
                await _assemblyClient.SendAudioAsync(data);
            };

            _assemblyClient.TranscriptUpdated += text =>
            {
                LastTranscript = text;
            };

            _assemblyClient.FinalTranscriptReady += OnFinalTranscriptReceived;
            _assemblyClient.ErrorOccurred += ex =>
            {
                Console.WriteLine($"AssemblyAI Error: {ex.Message}");
                VoiceState = CompanionVoiceState.Idle;
            };
        }

        public void Start()
        {
            Console.WriteLine("🔑 Clicky start on Windows");
            _hotkey.Start();
            UpdateOverlayVisibility();
        }

        public void UpdateOverlayVisibility()
        {
            IsOverlayVisible = IsClickyCursorEnabled && HasCompletedOnboarding;
        }

        public void Stop()
        {
            Console.WriteLine("🔑 Clicky stop on Windows");
            _hotkey.Stop();
            _audioRecorder.Stop();
            _ttsClient.StopPlayback();
        }

        private void OnHotkeyPressed()
        {
            Console.WriteLine("⌨️ Hotkey Pressed (Ctrl+Alt) - Starting recording");
            _pipelineCts?.Cancel();
            _pipelineCts = new CancellationTokenSource();

            _ttsClient.StopPlayback();
            PointX = null;
            PointY = null;
            PointLabel = null;
            BubbleText = "";

            VoiceState = CompanionVoiceState.Listening;
            LastTranscript = "";

            _audioRecorder.Start();

            // Connect to AssemblyAI streaming
            Task.Run(async () =>
            {
                try
                {
                    await _assemblyClient.StartSessionAsync(WorkerBaseURL, new List<string>());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to connect to AssemblyAI: {ex.Message}");
                }
            });
        }

        private void OnHotkeyReleased()
        {
            Console.WriteLine("⌨️ Hotkey Released (Ctrl+Alt) - Finalizing recording");
            _audioRecorder.Stop();
            VoiceState = CompanionVoiceState.Processing;

            Task.Run(async () =>
            {
                await _assemblyClient.StopSessionAsync();
            });
        }

        private void OnFinalTranscriptReceived(string transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                Console.WriteLine("🗣️ Empty transcript received. Returning to idle.");
                VoiceState = CompanionVoiceState.Idle;
                return;
            }

            Console.WriteLine($"🗣️ Final transcript: \"{transcript}\"");
            LastTranscript = transcript;

            var cts = _pipelineCts;
            if (cts == null || cts.IsCancellationRequested) return;

            Task.Run(async () =>
            {
                try
                {
                    // 1. Capture screen
                    var captures = ScreenCapturer.CaptureAllScreens();
                    if (cts.IsCancellationRequested) return;

                    // System System Prompt
                    string systemPrompt = @"you're clicky, a friendly always-on companion that lives in the user's system tray. the user just spoke to you via push-to-talk and you can see their screen(s). your reply will be spoken aloud via text-to-speech, so write the way you'd actually talk. this is an ongoing conversation — you remember everything they've said before.

rules:
- default to one or two sentences. be direct and dense.
- all lowercase, casual, warm. no emojis.
- write for the ear, not the eye. short sentences. no lists, bullet points, markdown, or formatting — just natural speech.
- don't use abbreviations or symbols that sound weird read aloud. write ""for example"" not ""e.g."", spell out small numbers.
- if the user's question relates to what's on their screen, reference specific things you see.
- if the screenshot doesn't seem relevant to their question, just answer the question directly.
- you can help with anything — coding, writing, general knowledge, brainstorming.
- never say ""simply"" or ""just"".
- don't read out code verbatim. describe what the code does or what needs to change conversationally.

element pointing:
you have a small blue triangle cursor that can fly to and point at things on screen. use it whenever pointing would genuinely help the user — if they're asking how to do something, looking for a menu, trying to find a button, or need help navigating an app, point at the relevant element.

format: [POINT:x,y:label] where x,y are integer pixel coordinates in the screenshot's coordinate space, and label is a short 1-3 word description of the element (like ""search bar"" or ""save button""). if the element is on the cursor's screen you can omit the screen number. if the element is on a DIFFERENT screen, append :screenN where N is the screen number from the image label (e.g. :screen2).

if pointing wouldn't help, append [POINT:none].";

                    // 2. Query Claude
                    string response = await _claudeClient.AnalyzeImageAsync(
                        captures,
                        systemPrompt,
                        ConversationHistory,
                        transcript,
                        SelectedModel,
                        $"{WorkerBaseURL}/chat"
                    );

                    if (cts.IsCancellationRequested) return;

                    // 3. Parse pointing
                    var parseResult = ParsePointing(response);
                    Console.WriteLine($"🤖 Claude replied: \"{parseResult.SpokenText}\"");

                    if (parseResult.X.HasValue && parseResult.Y.HasValue)
                    {
                        // Match display and map coordinates
                        // Find monitor matching screen index or primary screen
                        ScreenCapture? targetScreen = null;
                        if (parseResult.ScreenIndex.HasValue && parseResult.ScreenIndex.Value > 0 && parseResult.ScreenIndex.Value <= captures.Count)
                        {
                            targetScreen = captures[parseResult.ScreenIndex.Value - 1];
                        }
                        else
                        {
                            targetScreen = captures.Find(c => c.IsPrimary) ?? (captures.Count > 0 ? captures[0] : null);
                        }

                        if (targetScreen != null)
                        {
                            // Map pixel coords from screenshot (top-left 0,0) to global screen coords
                            // GDI BitBlt captures in pixel space. On Windows, pixels match screen coordinates if no DPI scaling,
                            // but we can map targetScreen.X + X_coord.
                            PointX = targetScreen.X + parseResult.X.Value;
                            PointY = targetScreen.Y + parseResult.Y.Value;
                            PointLabel = parseResult.ElementLabel;
                        }
                    }

                    // Add history
                    ConversationHistory.Add((transcript, parseResult.SpokenText));
                    if (ConversationHistory.Count > 10) ConversationHistory.RemoveAt(0);

                    // 4. Play TTS
                    VoiceState = CompanionVoiceState.Responding;
                    await _ttsClient.SpeakTextAsync(parseResult.SpokenText, $"{WorkerBaseURL}/tts");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Response pipeline error: {ex.Message}");
                }
                finally
                {
                    VoiceState = CompanionVoiceState.Idle;
                }
            });
        }

        private struct PointParseResult
        {
            public string SpokenText;
            public double? X;
            public double? Y;
            public string? ElementLabel;
            public int? ScreenIndex;
        }

        private PointParseResult ParsePointing(string text)
        {
            var result = new PointParseResult { SpokenText = text };
            var pattern = @"\[POINT:(?:none|(\d+)\s*,\s*(\d+)(?::([^\]:\s][^\]:]*?))?(?::screen(\d+))?)\]\s*$";
            var match = Regex.Match(text, pattern);

            if (match.Success)
            {
                result.SpokenText = text.Substring(0, match.Index).Trim();
                if (match.Groups[1].Success && match.Groups[2].Success)
                {
                    if (double.TryParse(match.Groups[1].Value, out double x) &&
                        double.TryParse(match.Groups[2].Value, out double y))
                    {
                        result.X = x;
                        result.Y = y;
                        result.ElementLabel = match.Groups[3].Success ? match.Groups[3].Value : "element";
                        if (match.Groups[4].Success && int.TryParse(match.Groups[4].Value, out int screenIdx))
                        {
                            result.ScreenIndex = screenIdx;
                        }
                    }
                }
            }

            return result;
        }
    }
}
