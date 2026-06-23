using System.Text.Json;
using clicky_windows.Models;
using Xunit;

namespace clicky_windows_tests.Models
{
    /// <summary>
    /// Tests for the ClickySettings JSON serialization contract. Every provider
    /// client (GenericAiClient, SpeechToTextProvider, TextToSpeechProvider) reads
    /// these fields after deserialization, so a round-trip must preserve all of
    /// them and the defaults must be internally consistent.
    /// </summary>
    public class ClickySettingsSerializationTests
    {
        [Fact]
        public void Defaults_AreInternallyConsistent()
        {
            var settings = new ClickySettings();

            // The default AI preset, format, and endpoint must agree with each
            // other — GenericAiClient routes on AiFormat and authenticates with
            // AiAuthType, so a mismatched default silently breaks first run.
            Assert.Equal("Anthropic", settings.AiFormat);
            Assert.Equal("ApiKey", settings.AiAuthType);
            Assert.Contains("anthropic.com", settings.AiEndpoint);

            Assert.False(string.IsNullOrWhiteSpace(settings.AiModel),
                "Default AiModel must not be empty.");
        }

        [Fact]
        public void RoundTrip_PreservesAllProviderFields()
        {
            var original = new ClickySettings
            {
                AiProvider = "OpenAI",
                AiProviderPreset = "OpenAI",
                AiApiKey = "sk-test-key",
                AiModel = "gpt-4o",
                AiEndpoint = "https://api.openai.com/v1/chat/completions",
                AiAuthType = "Bearer",
                AiFormat = "OpenAI",
                AiCustomHeadersJson = "{\"X-Test\":\"v\"}",
                AiSupportsVision = false,
                AiSupportsStreaming = true,
                SttProvider = "OpenAI",
                SttApiKey = "stt-key",
                SttEndpoint = "https://api.openai.com/v1",
                TtsProvider = "OpenAI",
                TtsApiKey = "tts-key",
                TtsModel = "tts-1",
                TtsVoiceId = "alloy",
                TtsEndpoint = "https://api.openai.com/v1",
                IsClickyCursorEnabled = false,
                HasCompletedOnboarding = false
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(original, options);
            var restored = JsonSerializer.Deserialize<ClickySettings>(json)!;

            Assert.Equal(original.AiProvider, restored.AiProvider);
            Assert.Equal(original.AiApiKey, restored.AiApiKey);
            Assert.Equal(original.AiModel, restored.AiModel);
            Assert.Equal(original.AiEndpoint, restored.AiEndpoint);
            Assert.Equal(original.AiAuthType, restored.AiAuthType);
            Assert.Equal(original.AiFormat, restored.AiFormat);
            Assert.Equal(original.AiCustomHeadersJson, restored.AiCustomHeadersJson);
            Assert.Equal(original.AiSupportsVision, restored.AiSupportsVision);
            Assert.Equal(original.AiSupportsStreaming, restored.AiSupportsStreaming);
            Assert.Equal(original.SttProvider, restored.SttProvider);
            Assert.Equal(original.SttApiKey, restored.SttApiKey);
            Assert.Equal(original.SttEndpoint, restored.SttEndpoint);
            Assert.Equal(original.TtsProvider, restored.TtsProvider);
            Assert.Equal(original.TtsModel, restored.TtsModel);
            Assert.Equal(original.TtsVoiceId, restored.TtsVoiceId);
            Assert.Equal(original.TtsEndpoint, restored.TtsEndpoint);
            Assert.Equal(original.IsClickyCursorEnabled, restored.IsClickyCursorEnabled);
            Assert.Equal(original.HasCompletedOnboarding, restored.HasCompletedOnboarding);
        }

        [Fact]
        public void RoundTrip_OfDefaults_DoesNotThrow()
        {
            // SettingsManager.Load deserializes whatever is on disk; a corrupt or
            // partial file must not crash startup. Empty/whitespace JSON should
            // be handled gracefully by the caller, but a freshly-default object
            // must always serialize and deserialize cleanly.
            var defaults = new ClickySettings();
            var options = new JsonSerializerOptions { WriteIndented = true };

            string json = JsonSerializer.Serialize(defaults, options);
            var restored = JsonSerializer.Deserialize<ClickySettings>(json);

            Assert.NotNull(restored);
        }
    }
}
