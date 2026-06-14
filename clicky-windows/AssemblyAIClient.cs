using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace clicky_windows
{
    public class AssemblyAIClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;

        public event Action<string>? TranscriptUpdated;
        public event Action<string>? FinalTranscriptReady;
        public event Action<Exception>? ErrorOccurred;

        private string _accumulatedTranscript = "";
        private readonly Dictionary<int, string> _completedTurns = new();

        public AssemblyAIClient()
        {
            _httpClient = new HttpClient();
        }

        public async Task StartSessionAsync(string proxyUrl, List<string> keyterms)
        {
            _cts = new CancellationTokenSource();
            _completedTurns.Clear();
            _accumulatedTranscript = "";

            try
            {
                // 1. Fetch token from proxy /transcribe-token
                string tokenUrl = $"{proxyUrl}/transcribe-token";
                var response = await _httpClient.PostAsync(tokenUrl, null);
                response.EnsureSuccessStatusCode();
                string tokenJson = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(tokenJson);
                string token = doc.RootElement.GetProperty("token").GetString() ?? throw new Exception("Token missing from proxy response.");

                // 2. Build websocket URL
                var queryParams = new List<string>
                {
                    "sample_rate=16000",
                    "encoding=pcm_s16le",
                    "format_turns=true",
                    "speech_model=u3-rt-pro",
                    $"token={token}"
                };

                if (keyterms.Count > 0)
                {
                    string keytermsJson = JsonSerializer.Serialize(keyterms);
                    queryParams.Add($"keyterms_prompt={Uri.EscapeDataString(keytermsJson)}");
                }

                string wsUrl = $"wss://streaming.assemblyai.com/v3/ws?{string.Join("&", queryParams)}";
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(wsUrl), _cts.Token);
                Console.WriteLine("🎙️ AssemblyAI WebSocket connected.");

                // Start receive loop
                _ = ReceiveLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
                throw;
            }
        }

        public async Task SendAudioAsync(byte[] pcmData)
        {
            if (_webSocket == null || _webSocket.State != WebSocketState.Open) return;

            try
            {
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(pcmData),
                    WebSocketMessageType.Binary,
                    true,
                    _cts?.Token ?? CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error sending audio to AssemblyAI: {ex.Message}");
                ErrorOccurred?.Invoke(ex);
            }
        }

        public async Task StopSessionAsync()
        {
            if (_webSocket == null) return;

            try
            {
                // Send Terminate message
                string terminateMsg = "{\"type\":\"Terminate\"}";
                var bytes = Encoding.UTF8.GetBytes(terminateMsg);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);

                _cts?.Cancel();
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Close requested", CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error closing AssemblyAI socket: {ex.Message}");
            }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[1024 * 16];

            try
            {
                while (!ct.IsCancellationRequested && _webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    string text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    HandleMessage(text);
                }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    ErrorOccurred?.Invoke(ex);
                }
            }
        }

        private void HandleMessage(string messageText)
        {
            try
            {
                using var doc = JsonDocument.Parse(messageText);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp)) return;

                string type = typeProp.GetString()?.ToLower() ?? "";
                if (type == "turn")
                {
                    string transcript = root.GetProperty("transcript").GetString() ?? "";
                    bool endOfTurn = root.GetProperty("end_of_turn").GetBoolean();
                    bool turnIsFormatted = root.GetProperty("turn_is_formatted").GetBoolean();
                    int turnOrder = root.GetProperty("turn_order").GetInt32();

                    if (endOfTurn || turnIsFormatted)
                    {
                        _completedTurns[turnOrder] = transcript;
                        string finalTranscript = GetCombinedTranscript();
                        TranscriptUpdated?.Invoke(finalTranscript);
                        FinalTranscriptReady?.Invoke(finalTranscript);
                    }
                    else
                    {
                        // Interim transcript
                        string interim = GetCombinedTranscript() + " " + transcript;
                        TranscriptUpdated?.Invoke(interim.Trim());
                    }
                }
                else if (type == "error")
                {
                    string error = root.GetProperty("error").GetString() ?? "Unknown error";
                    ErrorOccurred?.Invoke(new Exception($"AssemblyAI: {error}"));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error parsing AssemblyAI message: {ex.Message}");
            }
        }

        private string GetCombinedTranscript()
        {
            var parts = new List<string>();
            var sortedKeys = new List<int>(_completedTurns.Keys);
            sortedKeys.Sort();
            foreach (var key in sortedKeys)
            {
                parts.Add(_completedTurns[key]);
            }
            return string.Join(" ", parts).Trim();
        }

        public void Dispose()
        {
            _ = StopSessionAsync();
            _httpClient.Dispose();
        }
    }
}
