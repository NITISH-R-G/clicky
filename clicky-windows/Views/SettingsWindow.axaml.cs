using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using clicky_windows.Models;

namespace clicky_windows.Views
{
    public partial class SettingsWindow : Window
    {
        private bool _isUpdatingFromPreset = false;

        public SettingsWindow()
        {
            InitializeComponent();
            
            // Set position to bottom right of screen
            var screen = Screens.Primary;
            if (screen != null)
            {
                var workArea = screen.WorkingArea;
                double w = double.IsNaN(Width) ? 500 : Width;
                double h = double.IsNaN(Height) ? 580 : Height;
                // Position above taskbar
                Position = new PixelPoint(
                    workArea.Right - (int)(w * screen.Scaling) - 40,
                    workArea.Bottom - (int)(h * screen.Scaling) - 60
                );
            }

            // Populate AI Provider Combo Box
            AiProviderCombo.Items.Clear();
            foreach (var preset in ProviderRegistry.GetPresets())
            {
                AiProviderCombo.Items.Add(preset.Name);
            }

            // Sync with SettingsManager
            LoadSettingsIntoUI();

            // Setup Event Handlers
            AiProviderCombo.SelectionChanged += OnAiProviderChanged;
            SttProviderCombo.SelectionChanged += OnSttProviderChanged;
            TtsProviderCombo.SelectionChanged += OnTtsProviderChanged;

            SaveButton.Click += OnSaveClick;
            CancelButton.Click += OnCancelClick;
            QuitButton.Click += OnQuitClick;

            // Listen to state changes for Status Dot
            CompanionManager.Instance.PropertyChanged += OnManagerPropertyChanged;
            UpdateStatusUI(CompanionManager.Instance.VoiceState);
        }

        private void LoadSettingsIntoUI()
        {
            var settings = SettingsManager.Settings;

            // General
            ClickyCursorToggle.IsChecked = settings.IsClickyCursorEnabled;

            // AI Model Tab
            SetComboStringValue(AiProviderCombo, settings.AiProviderPreset);
            AiModelText.Text = settings.AiModel;
            AiEndpointText.Text = settings.AiEndpoint;
            AiApiKeyText.Text = settings.AiApiKey;
            SetComboStringValue(AiFormatCombo, settings.AiFormat);
            SetComboStringValue(AiAuthCombo, settings.AiAuthType);
            AiCustomHeadersText.Text = settings.AiCustomHeadersJson;
            AiVisionToggle.IsChecked = settings.AiSupportsVision;
            AiStreamingToggle.IsChecked = settings.AiSupportsStreaming;

            // STT Tab
            SetComboStringValue(SttProviderCombo, settings.SttProvider);
            SttEndpointText.Text = settings.SttEndpoint;
            SttApiKeyText.Text = settings.SttApiKey;

            // TTS Tab
            SetComboStringValue(TtsProviderCombo, settings.TtsProvider);
            TtsVoiceIdText.Text = settings.TtsVoiceId;
            TtsModelText.Text = settings.TtsModel;
            TtsEndpointText.Text = settings.TtsEndpoint;
            TtsApiKeyText.Text = settings.TtsApiKey;

            UpdateFieldEnableStates(settings.AiProviderPreset);
        }

        private void OnAiProviderChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingFromPreset) return;

            if (AiProviderCombo.SelectedItem is string presetName)
            {
                var preset = ProviderRegistry.GetPreset(presetName);
                
                _isUpdatingFromPreset = true;
                try
                {
                    AiModelText.Text = preset.DefaultModel;
                    AiEndpointText.Text = preset.DefaultEndpoint;
                    SetComboStringValue(AiFormatCombo, preset.Format);
                    SetComboStringValue(AiAuthCombo, preset.AuthType);
                    AiCustomHeadersText.Text = preset.CustomHeadersJson;
                    AiVisionToggle.IsChecked = preset.SupportsVision;
                    AiStreamingToggle.IsChecked = preset.SupportsStreaming;
                }
                finally
                {
                    _isUpdatingFromPreset = false;
                }

                UpdateFieldEnableStates(presetName);
            }
        }

        private void UpdateFieldEnableStates(string presetName)
        {
            bool isCustom = presetName.Equals("Custom Provider", StringComparison.OrdinalIgnoreCase);

            // If not custom, lock down configurations to make it simple and beginner-friendly
            AiModelText.IsEnabled = isCustom;
            AiEndpointText.IsEnabled = isCustom;
            AiFormatCombo.IsEnabled = isCustom;
            AiAuthCombo.IsEnabled = isCustom;
            AiCustomHeadersText.IsEnabled = isCustom;
            AiVisionToggle.IsEnabled = isCustom;
            AiStreamingToggle.IsEnabled = isCustom;
            
            // API key is always unlocked
            AiApiKeyText.IsEnabled = true;
        }

        private void OnSttProviderChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (SttProviderCombo.SelectedItem is ComboBoxItem item)
            {
                string provider = (item.Content?.ToString() ?? "").ToLower();

                if (provider.Contains("assembly"))
                {
                    SttEndpointText.Text = "";
                }
                else if (provider.Contains("openai"))
                {
                    SttEndpointText.Text = "https://api.openai.com/v1";
                }
                else
                {
                    SttEndpointText.Text = "http://localhost:8000/v1";
                }
            }
        }

        private void OnTtsProviderChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (TtsProviderCombo.SelectedItem is ComboBoxItem item)
            {
                string provider = (item.Content?.ToString() ?? "").ToLower();

                if (provider.Contains("eleven"))
                {
                    TtsVoiceIdText.Text = "cgSgspJ2msm6clMC924e";
                    TtsModelText.Text = "eleven_flash_v2_5";
                    TtsEndpointText.Text = "https://api.elevenlabs.io/v1";
                }
                else if (provider.Contains("openai"))
                {
                    TtsVoiceIdText.Text = "alloy";
                    TtsModelText.Text = "tts-1";
                    TtsEndpointText.Text = "https://api.openai.com/v1";
                }
                else
                {
                    TtsVoiceIdText.Text = "";
                    TtsModelText.Text = "";
                    TtsEndpointText.Text = "";
                    TtsApiKeyText.Text = "";
                }
            }
        }

        private void OnSaveClick(object? sender, RoutedEventArgs e)
        {
            var settings = SettingsManager.Settings;

            // General
            settings.IsClickyCursorEnabled = ClickyCursorToggle.IsChecked == true;
            CompanionManager.Instance.IsClickyCursorEnabled = settings.IsClickyCursorEnabled;

            // AI Model Tab
            settings.AiProviderPreset = AiProviderCombo.SelectedItem as string ?? "Custom Provider";
            settings.AiProvider = settings.AiProviderPreset;
            settings.AiModel = AiModelText.Text ?? "";
            settings.AiEndpoint = AiEndpointText.Text ?? "";
            settings.AiApiKey = AiApiKeyText.Text ?? "";
            settings.AiFormat = GetComboStringValue(AiFormatCombo);
            settings.AiAuthType = GetComboStringValue(AiAuthCombo);
            settings.AiCustomHeadersJson = AiCustomHeadersText.Text ?? "{}";
            settings.AiSupportsVision = AiVisionToggle.IsChecked == true;
            settings.AiSupportsStreaming = AiStreamingToggle.IsChecked == true;
            
            CompanionManager.Instance.SelectedModel = settings.AiModel;

            // STT Tab
            settings.SttProvider = GetComboStringValue(SttProviderCombo);
            settings.SttEndpoint = SttEndpointText.Text ?? "";
            settings.SttApiKey = SttApiKeyText.Text ?? "";

            // TTS Tab
            settings.TtsProvider = GetComboStringValue(TtsProviderCombo);
            settings.TtsVoiceId = TtsVoiceIdText.Text ?? "";
            settings.TtsModel = TtsModelText.Text ?? "";
            settings.TtsEndpoint = TtsEndpointText.Text ?? "";
            settings.TtsApiKey = TtsApiKeyText.Text ?? "";

            SettingsManager.Save();
            Hide();
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            LoadSettingsIntoUI(); // Revert UI fields back to original values
            Hide();
        }

        private void OnQuitClick(object? sender, RoutedEventArgs e)
        {
            CompanionManager.Instance.Stop();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
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
                    StatusDot.Fill = new SolidColorBrush(Color.Parse("#30D158")); // Green
                    StatusText.Text = "Active";
                    break;
                case CompanionVoiceState.Listening:
                    StatusDot.Fill = new SolidColorBrush(Color.Parse("#0A84FF")); // Blue
                    StatusText.Text = "Listening";
                    break;
                case CompanionVoiceState.Processing:
                    StatusDot.Fill = new SolidColorBrush(Color.Parse("#FF9500")); // Orange
                    StatusText.Text = "Processing";
                    break;
                case CompanionVoiceState.Responding:
                    StatusDot.Fill = new SolidColorBrush(Color.Parse("#BF5AF2")); // Purple
                    StatusText.Text = "Responding";
                    break;
            }
        }

        private void SetComboStringValue(ComboBox combo, string text)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                string itemText = "";
                if (combo.Items[i] is ComboBoxItem item)
                {
                    itemText = item.Content?.ToString() ?? "";
                }
                else if (combo.Items[i] is string str)
                {
                    itemText = str;
                }

                if (itemText.Contains(text, StringComparison.OrdinalIgnoreCase) || text.Contains(itemText, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private string GetComboStringValue(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem item)
            {
                string text = item.Content?.ToString() ?? "";
                return ResolveProviderCodeName(text);
            }
            else if (combo.SelectedItem is string str)
            {
                return ResolveProviderCodeName(str);
            }
            return "";
        }

        private string ResolveProviderCodeName(string raw)
        {
            if (raw.Contains("Anthropic")) return "Anthropic";
            if (raw.Contains("OpenAI Whisper")) return "OpenAI";
            if (raw.Contains("OpenAI Speech")) return "OpenAI";
            if (raw.Contains("OpenAI")) return "OpenAI";
            if (raw.Contains("Gemini")) return "Gemini";
            if (raw.Contains("Ollama")) return "Ollama";
            if (raw.Contains("Groq")) return "Groq";
            if (raw.Contains("OpenRouter")) return "OpenRouter";
            if (raw.Contains("AssemblyAI")) return "AssemblyAI";
            if (raw.Contains("System Speech")) return "System";
            if (raw.Contains("System")) return "System";
            return raw;
        }
    }
}
