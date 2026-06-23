using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia.Threading;
using clicky_windows.Models;
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

        private string _selectedModel = "claude-3-5-sonnet-20241022";
        public string SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (SetField(ref _selectedModel, value))
                {
                    SettingsManager.Settings.AiModel = value;
                    SettingsManager.Save();
                }
            }
        }

        private bool _isClickyCursorEnabled = true;
        public bool IsClickyCursorEnabled
        {
            get => _isClickyCursorEnabled;
            set
            {
                if (SetField(ref _isClickyCursorEnabled, value))
                {
                    SettingsManager.Settings.IsClickyCursorEnabled = value;
                    SettingsManager.Save();
                    UpdateOverlayVisibility();
                }
            }
        }

        private bool _hasCompletedOnboarding = true; // Default to true on Windows for simpler onboarding
        public bool HasCompletedOnboarding
        {
            get => _hasCompletedOnboarding;
            set
            {
                if (SetField(ref _hasCompletedOnboarding, value))
                {
                    SettingsManager.Settings.HasCompletedOnboarding = value;
                    SettingsManager.Save();
                }
            }
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
        private readonly SpeechToTextProvider _sttProvider = new();
        private readonly GenericAiClient _aiClient = new();
        private readonly TextToSpeechProvider _ttsProvider = new();

        private CancellationTokenSource? _pipelineCts;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raises PropertyChanged on the UI thread. OverlayWindow binds to these
        /// properties from the UI thread (DispatcherTimer tick + PropertyChanged
        /// handler), but several mutations happen from the pipeline's Task.Run
        /// (OnFinalTranscriptReceived sets VoiceState/PointX/PointY/... from a
        /// thread-pool thread). Routing every notification through the dispatcher
        /// makes SetField safe to call from any thread without per-site marshalling.
        /// PropertyChanged?.Invoke(...) is cheap when no handlers need switching.
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler == null) return;

            // Avoid the dispatcher hop entirely when already on the UI thread, which is
            // the common case (hotkey callbacks, UI-driven settings changes).
            if (Dispatcher.UIThread.CheckAccess())
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
            else
            {
                Dispatcher.UIThread.Post(() => handler(this, new PropertyChangedEventArgs(propertyName)));
            }
        }

        /// <summary>
        /// Runs an action on the UI thread, blocking if already there. Used for the
        /// few direct side-effect calls (not property sets) that must touch UI state.
        /// </summary>
        protected void RunOnUi(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
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
            // Sync initial properties with SettingsManager
            var settings = SettingsManager.Settings;
            _isClickyCursorEnabled = settings.IsClickyCursorEnabled;
            _hasCompletedOnboarding = settings.HasCompletedOnboarding;
            _selectedModel = settings.AiModel;

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
                await _sttProvider.SendAudioAsync(data);
            };

            _sttProvider.TranscriptUpdated += text =>
            {
                LastTranscript = text;
            };

            _sttProvider.FinalTranscriptReady += OnFinalTranscriptReceived;
            _sttProvider.ErrorOccurred += ex =>
            {
                Console.WriteLine($"Transcription Error: {ex.Message}");
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

            // Cancel any in-flight response pipeline (screenshot -> AI -> TTS) so a
            // suspend/lock or shutdown doesn't leave a response running in the
            // background. Previously Stop() only tore down the hotkey/audio/TTS,
            // leaving the pipeline Task.Run running until it finished on its own.
            // Cancel before disposing so pipeline code observing cts.IsCancellationRequested
            // sees the cancellation, then dispose to release the CTS itself.
            var pipelineCts = _pipelineCts;
            _pipelineCts = null;
            if (pipelineCts != null)
            {
                pipelineCts.Cancel();
                pipelineCts.Dispose();
            }

            _hotkey.Stop();
            _audioRecorder.Stop();
            _ttsProvider.StopPlayback();

            // Reset visible state so the overlay doesn't show a stale
            // Listening/Processing/Responding indicator after teardown.
            VoiceState = CompanionVoiceState.Idle;
            PointX = null;
            PointY = null;
            PointLabel = null;
        }

        private void OnHotkeyPressed()
        {
            Console.WriteLine("⌨️ Hotkey Pressed (Ctrl+Alt) - Starting recording");
            _pipelineCts?.Cancel();
            _pipelineCts = new CancellationTokenSource();

            _ttsProvider.StopPlayback();
            PointX = null;
            PointY = null;
            PointLabel = null;
            BubbleText = "";

            VoiceState = CompanionVoiceState.Listening;
            LastTranscript = "";

            _audioRecorder.Start();

            // Connect to STT session
            Task.Run(async () =>
            {
                try
                {
                    await _sttProvider.StartSessionAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to start STT session: {ex.Message}");
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
                await _sttProvider.StopSessionAsync();
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

                    // System Prompt
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

risk-aware autonomy:
behave like a trusted human assistant. classify actions by risk level:
- low risk actions: execute immediately without asking for permission or showing confirmation prompt (examples: teaching, explaining, drawing diagrams, highlighting screen, moving clicky cursor, speaking, answering questions, opening documentation, playing/pausing music, adjusting clicky's own settings, scrolling, zooming, navigating within an application, pointing to UI elements, replaying lessons, switching teaching modes).
- medium risk actions: briefly inform the user before acting (e.g. state what you are doing in speech) but do not interrupt/stop the flow or block to ask for permission (examples: opening another application, launching a browser, downloading a file, connecting to an AI provider, accessing the clipboard, reading a document, opening folders).
- high risk actions: you MUST require explicit user approval. clearly explain what you intend to do and ask for confirmation before executing (examples: deleting files, moving files, renaming files, editing user documents, sending emails, sending messages, making purchases, executing shell commands, installing software, uninstalling software, changing operating system settings, changing security settings, modifying registry values, automation that affects external systems, anything irreversible).

natural and adaptive communication:
- do not narrate tiny actions like ""I'm moving my cursor"" or ""I'm clicking this button"" unless the user is learning or specifically asks for detail. instead, communicate intent naturally (e.g., ""I'm opening the mixer because that's where audio routing is configured"").
- adjust explanation detail based on context:
  * when teaching: explain thoroughly.
  * when debugging: explain important reasoning.
  * when casually chatting: keep responses concise.
  * when performing routine actions: minimize interruptions.
  * user experience: if the user appears experienced, avoid explaining obvious concepts. if they appear confused, automatically increase the level of detail.
- don't overexplain, overconfirm, overapologize, or seek permission unnecessarily. be proactive and efficient.

visual teaching and drawings:
you are a visual teacher and mentor. whenever the user asks for explanations, tutorials, coding help, software guides, science/math concepts, or diagrams, you must automatically teach using synchronized visual annotations directly on their screen using pointing and drawing directives.

supported pointing command:
- [POINT:x,y:label] where x,y are integer pixel coordinates relative to the screen image, and label is a short 1-3 word description of the element (like ""search bar"" or ""save button""). if the element is on a DIFFERENT screen, append :screenN where N is the screen number from the image label (e.g. :screen2). if pointing wouldn't help, write [POINT:none].

supported drawing commands (always output these to illustrate concepts, sketch diagrams, point out buttons, or highlight code/controls):
- [DRAW:RECTANGLE:x,y,w,h:label] - draw a rectangle around a coordinate region.
- [DRAW:CIRCLE:cx,cy,r:label] - draw a circle at center cx,cy with radius r.
- [DRAW:ARROW:x1,y1,x2,y2:label] - draw an arrow pointing from x1,y1 to x2,y2.
- [DRAW:LINE:x1,y1,x2,y2:label] - draw a line from x1,y1 to x2,y2.
- [DRAW:HIGHLIGHT:x,y,w,h] - highlight/shade a region.
- [DRAW:BADGE:x,y:step_number] - draw a numbered badge/step indicator.
- [DRAW:TEXT:x,y:text_to_display] - draw text labels/math equations directly on screen.
- [DRAW:SVG:x,y,w,h,svg_path_data:label] - draw a custom SVG path fitting within w,h placed at x,y. Use this to sketch math functions, force vectors, circuits, structures, networks, CPU layouts, tree structures etc.
- [DRAW:CLEAR] - clear all previous drawings.

timeline and synchronization:
always structure your response as a natural teaching sequence. speak a statement, then output the [POINT:...] and [DRAW:...] tags corresponding to that statement, then write the next statement. clicky will speak each statement and perform the drawings in perfect sync.";

                    // 2. Query Pluggable AI Endpoint
                    string response = await _aiClient.AnalyzeImageAsync(
                        captures,
                        systemPrompt,
                        ConversationHistory,
                        transcript
                    );

                    if (cts.IsCancellationRequested) return;

                    // 3. Parse and execute synchronized timeline
                    var steps = ParseTeachingTimeline(response);
                    Console.WriteLine($"🤖 AI response parsed into {steps.Count} teaching steps.");

                    // Add history
                    // Extract clean spoken text for history (no tags)
                    string cleanSpokenText = Regex.Replace(response, @"\[(?:POINT|DRAW):[^\]]+\]", "").Trim();
                    ConversationHistory.Add((transcript, cleanSpokenText));
                    if (ConversationHistory.Count > 10) ConversationHistory.RemoveAt(0);

                    // Clear previous drawings first
                    DrawRequested?.Invoke(new AnnotationInstruction { Shape = "CLEAR" });

                    foreach (var step in steps)
                    {
                        if (cts.IsCancellationRequested) return;

                        // Apply drawings for this step
                        foreach (var drawing in step.Drawings)
                        {
                            ScreenCapture? targetScreen = null;
                            if (drawing.ScreenIndex > 0 && drawing.ScreenIndex <= captures.Count)
                            {
                                targetScreen = captures[drawing.ScreenIndex - 1];
                            }
                            else
                            {
                                targetScreen = captures.Find(c => c.IsPrimary) ?? (captures.Count > 0 ? captures[0] : null);
                            }

                            if (targetScreen != null)
                            {
                                drawing.X1 += targetScreen.X;
                                drawing.Y1 += targetScreen.Y;
                                if (drawing.Shape == "ARROW" || drawing.Shape == "LINE" || drawing.Shape == "SVG")
                                {
                                    drawing.X2 += targetScreen.X;
                                    drawing.Y2 += targetScreen.Y;
                                }
                            }
                            DrawRequested?.Invoke(drawing);
                        }

                        // Apply pointing for this step
                        if (step.Point != null)
                        {
                            ScreenCapture? targetScreen = null;
                            if (step.Point.ScreenIndex.HasValue && step.Point.ScreenIndex.Value > 0 && step.Point.ScreenIndex.Value <= captures.Count)
                            {
                                targetScreen = captures[step.Point.ScreenIndex.Value - 1];
                            }
                            else
                            {
                                targetScreen = captures.Find(c => c.IsPrimary) ?? (captures.Count > 0 ? captures[0] : null);
                            }

                            if (targetScreen != null)
                            {
                                PointX = targetScreen.X + step.Point.X;
                                PointY = targetScreen.Y + step.Point.Y;
                                PointLabel = step.Point.Label;
                            }
                        }
                        else
                        {
                            PointX = null;
                            PointY = null;
                            PointLabel = null;
                        }

                        // Speak statement
                        if (!string.IsNullOrWhiteSpace(step.SpokenText))
                        {
                            VoiceState = CompanionVoiceState.Responding;
                            LastTranscript = step.SpokenText;
                            
                            await _ttsProvider.SpeakTextAsync(step.SpokenText);
                            
                            // Synchronized delay: wait until active statement finishes playing
                            while (_ttsProvider.IsPlaying && !cts.IsCancellationRequested)
                            {
                                await Task.Delay(100);
                            }
                            
                            // Visual/verbal parsing pause
                            await Task.Delay(500);
                        }
                    }
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

        public event Action<AnnotationInstruction>? DrawRequested;

        public class TeachingStep
        {
            public string SpokenText { get; set; } = "";
            public List<AnnotationInstruction> Drawings { get; } = new();
            public PointAction? Point { get; set; }
        }

        public class PointAction
        {
            public double X { get; set; }
            public double Y { get; set; }
            public string Label { get; set; } = "";
            public int? ScreenIndex { get; set; }
        }

        private List<TeachingStep> ParseTeachingTimeline(string responseText)
        {
            var steps = new List<TeachingStep>();
            var tagRegex = new Regex(@"(\[POINT:[^\]]+\]|\[DRAW:[^\]]+\])", RegexOptions.IgnoreCase);
            var parts = tagRegex.Split(responseText);
            
            var currentStep = new TeachingStep();
            
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                
                if (part.StartsWith("[POINT:", StringComparison.OrdinalIgnoreCase))
                {
                    currentStep.Point = ParsePointTag(part);
                }
                else if (part.StartsWith("[DRAW:", StringComparison.OrdinalIgnoreCase))
                {
                    var draw = ParseDrawTag(part);
                    if (draw != null)
                    {
                        currentStep.Drawings.Add(draw);
                    }
                }
                else
                {
                    string text = part.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // If the current step already has annotations, start a new one to keep visual timing sequential
                        if (currentStep.Drawings.Count > 0 || currentStep.Point != null)
                        {
                            steps.Add(currentStep);
                            currentStep = new TeachingStep();
                        }
                        currentStep.SpokenText = (currentStep.SpokenText + " " + text).Trim();
                    }
                }
            }
            
            if (currentStep.Drawings.Count > 0 || currentStep.Point != null || !string.IsNullOrWhiteSpace(currentStep.SpokenText))
            {
                steps.Add(currentStep);
            }
            
            return steps;
        }

        private PointAction? ParsePointTag(string tag)
        {
            if (tag.Contains("POINT:none", StringComparison.OrdinalIgnoreCase)) return null;
            var match = Regex.Match(tag, @"\[POINT:(\d+)\s*,\s*(\d+)(?::([^\]:\s][^\]:]*?))?(?::screen(\d+))?\]", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var pt = new PointAction();
                if (double.TryParse(match.Groups[1].Value, out double x) &&
                    double.TryParse(match.Groups[2].Value, out double y))
                {
                    pt.X = x;
                    pt.Y = y;
                    pt.Label = match.Groups[3].Success ? match.Groups[3].Value : "element";
                    if (match.Groups[4].Success && int.TryParse(match.Groups[4].Value, out int sIdx))
                    {
                        pt.ScreenIndex = sIdx;
                    }
                    return pt;
                }
            }
            return null;
        }

        private AnnotationInstruction? ParseDrawTag(string tag)
        {
            var parts = tag.Trim('[', ']').Split(':');
            if (parts.Length < 3) return null;
            
            string shape = parts[1].ToUpper();
            string paramStr = parts[2];
            string label = parts.Length > 3 ? parts[3] : "";
            
            var annotation = new AnnotationInstruction { Shape = shape, Label = label };
            var paramParts = paramStr.Split(',');

            if (shape == "ARROW" || shape == "LINE")
            {
                if (paramParts.Length >= 4 &&
                    double.TryParse(paramParts[0], out double x1) &&
                    double.TryParse(paramParts[1], out double y1) &&
                    double.TryParse(paramParts[2], out double x2) &&
                    double.TryParse(paramParts[3], out double y2))
                {
                    annotation.X1 = x1;
                    annotation.Y1 = y1;
                    annotation.X2 = x2;
                    annotation.Y2 = y2;
                }
            }
            else if (shape == "CIRCLE")
            {
                if (paramParts.Length >= 3 &&
                    double.TryParse(paramParts[0], out double cx) &&
                    double.TryParse(paramParts[1], out double cy) &&
                    double.TryParse(paramParts[2], out double r))
                {
                    annotation.X1 = cx;
                    annotation.Y1 = cy;
                    annotation.Radius = r;
                }
            }
            else if (shape == "RECTANGLE" || shape == "HIGHLIGHT")
            {
                if (paramParts.Length >= 4 &&
                    double.TryParse(paramParts[0], out double rx) &&
                    double.TryParse(paramParts[1], out double ry) &&
                    double.TryParse(paramParts[2], out double rw) &&
                    double.TryParse(paramParts[3], out double rh))
                {
                    annotation.X1 = rx;
                    annotation.Y1 = ry;
                    annotation.X2 = rw;
                    annotation.Y2 = rh;
                }
            }
            else if (shape == "TEXT" || shape == "BADGE")
            {
                if (paramParts.Length >= 2 &&
                    double.TryParse(paramParts[0], out double tx) &&
                    double.TryParse(paramParts[1], out double ty))
                {
                    annotation.X1 = tx;
                    annotation.Y1 = ty;
                    annotation.Text = paramParts.Length > 2 ? string.Join(",", paramParts, 2, paramParts.Length - 2) : label;
                }
            }
            else if (shape == "SVG")
            {
                if (paramParts.Length >= 5 &&
                    double.TryParse(paramParts[0], out double sx) &&
                    double.TryParse(paramParts[1], out double sy) &&
                    double.TryParse(paramParts[2], out double sw) &&
                    double.TryParse(paramParts[3], out double sh))
                {
                    annotation.X1 = sx;
                    annotation.Y1 = sy;
                    annotation.X2 = sw;
                    annotation.Y2 = sh;
                    annotation.PathData = string.Join(",", paramParts, 4, paramParts.Length - 4);
                }
            }
            else if (shape == "CLEAR")
            {
                // cleared shape marker
            }
            
            return annotation;
        }
    }
}
