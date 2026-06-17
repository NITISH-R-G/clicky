using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using clicky_windows.Models;

namespace clicky_windows
{
    public class SpeechToTextProvider : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly AssemblyAIClient _assemblyClient;
        private readonly List<byte> _audioBuffer = new();
        
        public event Action<string>? TranscriptUpdated;
        public event Action<string>? FinalTranscriptReady;
        public event Action<Exception>? ErrorOccurred;

        public SpeechToTextProvider()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _assemblyClient = new AssemblyAIClient();
            
            // Forward AssemblyAI events
            _assemblyClient.TranscriptUpdated += text => TranscriptUpdated?.Invoke(text);
            _assemblyClient.FinalTranscriptReady += text => FinalTranscriptReady?.Invoke(text);
            _assemblyClient.ErrorOccurred += ex => ErrorOccurred?.Invoke(ex);
        }

        public async Task StartSessionAsync()
        {
            var settings = SettingsManager.Settings;
            string provider = settings.SttProvider.ToLower();

            _audioBuffer.Clear();

            if (provider == "assemblyai")
            {
                // Connect to AssemblyAI streaming ws
                string workerUrl = settings.SttEndpoint;
                if (string.IsNullOrWhiteSpace(workerUrl) || workerUrl.Contains("your-worker-name"))
                {
                    workerUrl = CompanionManager.Instance.WorkerBaseURL;
                }
                await _assemblyClient.StartSessionAsync(workerUrl, new List<string>());
            }
            else
            {
                Console.WriteLine($"🎙️ SpeechToTextProvider (OpenAI-compatible): Session started, buffering audio locally...");
            }
        }

        public async Task SendAudioAsync(byte[] pcmData)
        {
            var settings = SettingsManager.Settings;
            string provider = settings.SttProvider.ToLower();

            if (provider == "assemblyai")
            {
                await _assemblyClient.SendAudioAsync(pcmData);
            }
            else
            {
                lock (_audioBuffer)
                {
                    _audioBuffer.AddRange(pcmData);
                }
            }
        }

        public async Task StopSessionAsync()
        {
            var settings = SettingsManager.Settings;
            string provider = settings.SttProvider.ToLower();

            if (provider == "assemblyai")
            {
                await _assemblyClient.StopSessionAsync();
            }
            else
            {
                byte[] pcmBytes;
                lock (_audioBuffer)
                {
                    pcmBytes = _audioBuffer.ToArray();
                    _audioBuffer.Clear();
                }

                if (pcmBytes.Length == 0)
                {
                    FinalTranscriptReady?.Invoke("");
                    return;
                }

                // Process OpenAI/Compatible transcript uploads in a separate thread
                _ = Task.Run(async () =>
                {
                    try
                    {
                        byte[] wavBytes = ConvertPcmToWav(pcmBytes);
                        string transcript = await TranscribeWavAsync(wavBytes, settings);
                        FinalTranscriptReady?.Invoke(transcript);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Transcription failed: {ex.Message}");
                        ErrorOccurred?.Invoke(ex);
                    }
                });
            }
        }

        private async Task<string> TranscribeWavAsync(byte[] wavBytes, ClickySettings settings)
        {
            string endpoint = settings.SttEndpoint;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                endpoint = "https://api.openai.com/v1/audio/transcriptions";
            }
            else if (!endpoint.EndsWith("/audio/transcriptions") && !endpoint.Contains("/v1/audio/transcriptions"))
            {
                endpoint = endpoint.TrimEnd('/') + "/audio/transcriptions";
            }

            Console.WriteLine($"🎙️ SpeechToTextProvider: Uploading {wavBytes.Length / 1024}KB WAV to {endpoint}...");

            using var form = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(wavBytes);
            audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/wav");
            form.Add(audioContent, "file", "speech.wav");
            form.Add(new StringContent("whisper-1"), "model");

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = form;

            if (!string.IsNullOrWhiteSpace(settings.SttApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SttApiKey);
            }

            var response = await _httpClient.SendAsync(request);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Transcription API returned error: {response.StatusCode} - {responseText}");
            }

            using var doc = JsonDocument.Parse(responseText);
            if (doc.RootElement.TryGetProperty("text", out var textProp))
            {
                return textProp.GetString() ?? "";
            }

            throw new Exception("Transcription response did not contain 'text' property.");
        }

        private byte[] ConvertPcmToWav(byte[] pcmData, int sampleRate = 16000, short bitsPerSample = 16, short channels = 1)
        {
            using (var memoryStream = new MemoryStream())
            using (var writer = new BinaryWriter(memoryStream))
            {
                // RIFF header
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + pcmData.Length);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                // Subchunk 1 (fmt )
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16); // Subchunk1Size
                writer.Write((short)1); // AudioFormat (1 = PCM)
                writer.Write(channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * (bitsPerSample / 8)); // ByteRate
                writer.Write((short)(channels * (bitsPerSample / 8))); // BlockAlign
                writer.Write(bitsPerSample);

                // Subchunk 2 (data)
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(pcmData.Length);
                writer.Write(pcmData);

                return memoryStream.ToArray();
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _assemblyClient.Dispose();
        }
    }
}
