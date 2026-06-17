using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using clicky_windows.Models;

namespace clicky_windows
{
    public class TextToSpeechProvider : IDisposable
    {
        private readonly HttpClient _httpClient;
        private WaveOutEvent? _waveOut;
        private Mp3FileReader? _mp3Reader;
        private MemoryStream? _audioStream;
        private Process? _speechProcess;
        private bool _isPlayingLocal = false;

        public bool IsPlaying 
        {
            get
            {
                if (_waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing)
                    return true;
                if (_isPlayingLocal)
                    return true;
                return false;
            }
        }

        public TextToSpeechProvider()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        }

        public async Task SpeakTextAsync(string text)
        {
            StopPlayback();

            var settings = SettingsManager.Settings;
            string provider = settings.TtsProvider.ToLower();

            if (provider == "system")
            {
                await SpeakLocalSystemAsync(text);
            }
            else if (provider == "openai")
            {
                await SpeakOpenAiAsync(text, settings);
            }
            else
            {
                // Default: ElevenLabs
                await SpeakElevenLabsAsync(text, settings);
            }
        }

        private async Task SpeakElevenLabsAsync(string text, ClickySettings settings)
        {
            string endpoint = settings.TtsEndpoint;
            if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Contains("your-worker-name"))
            {
                // Fallback to Worker or ElevenLabs direct
                endpoint = CompanionManager.Instance.WorkerBaseURL + "/tts";
            }
            else if (!endpoint.EndsWith("/tts") && !endpoint.Contains("/v1/text-to-speech"))
            {
                endpoint = endpoint.TrimEnd('/') + $"/text-to-speech/{settings.TtsVoiceId}";
            }

            var requestBody = new
            {
                text = text,
                model_id = settings.TtsModel,
                voice_settings = new
                {
                    stability = 0.5,
                    similarity_boost = 0.75
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            Console.WriteLine($"🔊 TextToSpeechProvider (ElevenLabs): Requesting \"{text}\" from {endpoint}...");

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));

            if (!string.IsNullOrWhiteSpace(settings.TtsApiKey))
            {
                request.Headers.Add("xi-api-key", settings.TtsApiKey);
            }

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                string errText = await response.Content.ReadAsStringAsync();
                throw new Exception($"ElevenLabs TTS API returned: {response.StatusCode} - {errText}");
            }

            byte[] audioData = await response.Content.ReadAsByteArrayAsync();
            PlayMp3(audioData);
        }

        private async Task SpeakOpenAiAsync(string text, ClickySettings settings)
        {
            string endpoint = settings.TtsEndpoint;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                endpoint = "https://api.openai.com/v1/audio/speech";
            }
            else if (!endpoint.EndsWith("/audio/speech") && !endpoint.Contains("/v1/audio/speech"))
            {
                endpoint = endpoint.TrimEnd('/') + "/audio/speech";
            }

            var requestBody = new
            {
                model = settings.TtsModel.Contains("eleven") ? "tts-1" : settings.TtsModel,
                input = text,
                voice = string.IsNullOrWhiteSpace(settings.TtsVoiceId) || settings.TtsVoiceId.Length > 15 ? "alloy" : settings.TtsVoiceId
            };

            var json = JsonSerializer.Serialize(requestBody);
            Console.WriteLine($"🔊 TextToSpeechProvider (OpenAI): Requesting \"{text}\" from {endpoint}...");

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };

            if (!string.IsNullOrWhiteSpace(settings.TtsApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.TtsApiKey);
            }

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                string errText = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI TTS API returned: {response.StatusCode} - {errText}");
            }

            byte[] audioData = await response.Content.ReadAsByteArrayAsync();
            PlayMp3(audioData);
        }

        private async Task SpeakLocalSystemAsync(string text)
        {
            Console.WriteLine($"🔊 TextToSpeechProvider (System TTS): Speaking \"{text}\" locally via SAPI...");
            
            _isPlayingLocal = true;
            try
            {
                string escapedText = text.Replace("'", "''").Replace("\"", "`\"");
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-Type -AssemblyName System.Speech; $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer; $synth.Speak('{escapedText}')\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                _speechProcess = Process.Start(psi);
                if (_speechProcess != null)
                {
                    await _speechProcess.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Local System Speech failed: {ex.Message}");
            }
            finally
            {
                _isPlayingLocal = false;
            }
        }

        private void PlayMp3(byte[] audioData)
        {
            _audioStream = new MemoryStream(audioData);
            _mp3Reader = new Mp3FileReader(_audioStream);
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_mp3Reader);
            _waveOut.Play();
        }

        public void StopPlayback()
        {
            try
            {
                if (_waveOut != null)
                {
                    _waveOut.Stop();
                    _waveOut.Dispose();
                    _waveOut = null;
                }

                if (_mp3Reader != null)
                {
                    _mp3Reader.Dispose();
                    _mp3Reader = null;
                }

                if (_audioStream != null)
                {
                    _audioStream.Dispose();
                    _audioStream = null;
                }

                if (_speechProcess != null && !_speechProcess.HasExited)
                {
                    _speechProcess.Kill();
                    _speechProcess.Dispose();
                    _speechProcess = null;
                }
                
                _isPlayingLocal = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error stopping playback: {ex.Message}");
            }
        }

        public void Dispose()
        {
            StopPlayback();
            _httpClient.Dispose();
        }
    }
}
