using System;
using System.IO;
using System.Text.Json;

namespace clicky_windows.Models
{
    public class ClickySettings
    {
        public string AiProvider { get; set; } = "Anthropic";
        public string AiProviderPreset { get; set; } = "Anthropic (Claude)";
        public string AiApiKey { get; set; } = "";
        public string AiModel { get; set; } = "claude-3-5-sonnet-20241022";
        public string AiEndpoint { get; set; } = "https://api.anthropic.com/v1/messages";
        public string AiAuthType { get; set; } = "ApiKey"; // Bearer, ApiKey, None, CustomHeader
        public string AiFormat { get; set; } = "Anthropic"; // Anthropic, OpenAI
        public string AiCustomHeadersJson { get; set; } = "{}";
        public bool AiSupportsVision { get; set; } = true;
        public bool AiSupportsStreaming { get; set; } = false;

        public string SttProvider { get; set; } = "AssemblyAI";
        public string SttApiKey { get; set; } = "";
        // Blank means "direct AssemblyAI mode using SttApiKey" — AssemblyAIClient
        // resolves this to the canonical streaming.assemblyai.com/v3/token endpoint.
        // Set this to a Clicky Cloudflare Worker URL instead to keep the key
        // server-side (worker mode). Never set it to api.assemblyai.com/v2 — that
        // host has no /transcribe-token route and was the old broken default.
        public string SttEndpoint { get; set; } = "";

        public string TtsProvider { get; set; } = "ElevenLabs";
        public string TtsApiKey { get; set; } = "";
        public string TtsModel { get; set; } = "eleven_flash_v2_5";
        public string TtsVoiceId { get; set; } = "cgSgspJ2msm6clMC924e"; // Default voice ID
        public string TtsEndpoint { get; set; } = "https://api.elevenlabs.io/v1";

        public bool IsClickyCursorEnabled { get; set; } = true;
        public bool HasCompletedOnboarding { get; set; } = true;
    }

    public class SettingsManager
    {
        private static ClickySettings? _settings;
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "Clicky"
        );
        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        public static ClickySettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    Load();
                }
                return _settings!;
            }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    _settings = JsonSerializer.Deserialize<ClickySettings>(json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to load settings: {ex.Message}");
            }

            if (_settings == null)
            {
                _settings = new ClickySettings();
            }
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_settings ?? new ClickySettings(), options);
                File.WriteAllText(SettingsFile, json);
                Console.WriteLine($"🔑 Settings saved to {SettingsFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to save settings: {ex.Message}");
            }
        }
    }
}
