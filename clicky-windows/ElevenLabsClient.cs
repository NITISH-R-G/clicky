using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace clicky_windows
{
    public class ElevenLabsClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private WaveOutEvent? _waveOut;
        private Mp3FileReader? _mp3Reader;
        private MemoryStream? _audioStream;

        public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;

        public ElevenLabsClient()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        public async Task SpeakTextAsync(string text, string proxyUrl)
        {
            StopPlayback();

            var requestBody = new
            {
                text = text,
                model_id = "eleven_flash_v2_5",
                voice_settings = new
                {
                    stability = 0.5,
                    similarity_boost = 0.75
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            
            Console.WriteLine($"🔊 ElevenLabs TTS requesting text: \"{text}\"");
            
            int maxRetryAttempts = 3;
            int delayMs = 1000;
            HttpResponseMessage? response = null;
            byte[]? audioData = null;

            for (int attempt = 1; attempt <= maxRetryAttempts; attempt++)
            {
                try
                {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    _httpClient.DefaultRequestHeaders.Accept.Clear();
                    _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));

                    response = await _httpClient.PostAsync(proxyUrl, content);
                    if (response.IsSuccessStatusCode)
                    {
                        audioData = await response.Content.ReadAsByteArrayAsync();
                        break;
                    }
                    
                    string errText = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"🔊 ElevenLabs TTS Attempt {attempt} failed: {response.StatusCode} - {errText}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"🔊 ElevenLabs TTS Attempt {attempt} threw exception: {ex.Message}");
                    if (attempt == maxRetryAttempts) throw;
                }

                if (attempt < maxRetryAttempts)
                {
                    int jitter = new Random().Next(-200, 200);
                    int currentDelay = Math.Max(100, (delayMs * attempt) + jitter);
                    Console.WriteLine($"🔊 Retrying in {currentDelay}ms...");
                    await Task.Delay(currentDelay);
                }
            }

            if (response == null || !response.IsSuccessStatusCode || audioData == null)
            {
                throw new Exception($"ElevenLabs TTS Error after {maxRetryAttempts} attempts. Status: {response?.StatusCode}");
            }
            
            Console.WriteLine($"🔊 ElevenLabs TTS: received {audioData.Length / 1024}KB audio");

            // Play the MP3 data
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error stopping TTS playback: {ex.Message}");
            }
        }

        public void Dispose()
        {
            StopPlayback();
            _httpClient.Dispose();
        }
    }
}
