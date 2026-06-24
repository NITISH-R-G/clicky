using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace clicky_windows
{
    /// <summary>
    /// Describes how to fetch a short-lived AssemblyAI streaming token. The token
    /// source depends on the configured mode:
    ///  - Worker (proxy) mode: the endpoint is a Cloudflare Worker URL that hosts
    ///    the custom <c>/transcribe-token</c> route. The API key never leaves the
    ///    worker, so no authorization header is sent from the app.
    ///  - Direct mode: the endpoint points at AssemblyAI itself (or is blank), so
    ///    the canonical <c>https://streaming.assemblyai.com/v3/token</c> endpoint
    ///    is called with the user's AssemblyAI API key as the authorization header.
    /// This record is kept pure and static-resolvable so it can be unit-tested
    /// without any network access.
    /// </summary>
    public readonly record struct AssemblyAiTokenRequest(
        string Url,
        HttpMethod Method,
        string? AuthorizationHeader);

    public class AssemblyAIClient : IDisposable
    {
        // The canonical AssemblyAI streaming token endpoint. In direct mode the
        // app calls this with the user's API key; in worker mode the worker's
        // /transcribe-token route proxies to this same upstream endpoint.
        private const string DirectTokenEndpoint = "https://streaming.assemblyai.com/v3/token";

        // Token lifetime requested from AssemblyAI. The v3/token endpoint REQUIRES
        // an expires_in_seconds query param (a bare GET returns HTTP 422 "Field
        // required"). 480s matches the Cloudflare Worker's /transcribe-token route,
        // which appends this same value, so worker and direct modes behave identically.
        private const int TokenLifetimeSeconds = 480;

        private readonly HttpClient _httpClient;
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;

        public event Action<string>? TranscriptUpdated;
        public event Action<string>? FinalTranscriptReady;
        public event Action<Exception>? ErrorOccurred;

        // Turn transcripts keyed by turn_order so out-of-order or re-formatted
        // turns overwrite their earlier partial instead of duplicating text.
        private readonly Dictionary<int, string> _completedTurns = new();

        // Audio that arrived while the WebSocket was still connecting is buffered here
        // and flushed once the socket opens. Without this, SendAudioAsync silently drops
        // every buffer during the ~1-2s connect window, so the first ~1.5s of speech
        // (often the user's entire utterance on a quick press-and-release) never reaches
        // AssemblyAI. The macOS flow pre-connects before recording; here we buffer instead.
        // Each entry is a whole PCM chunk (as delivered by AudioRecorder.DataAvailable).
        private readonly List<byte[]> _pendingAudioWhileConnecting = new();
        private readonly object _pendingAudioLock = new();

        // Signaled by the receive loop when a final/formatted turn (end_of_turn or
        // turn_is_formatted) arrives, so StopSessionAsync can wait for the last
        // transcript before closing the socket. Replaced on each session start.
        private TaskCompletionSource<bool>? _finalTurnReceived;
        private readonly object _finalTurnLock = new();

        public AssemblyAIClient()
        {
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Resolves the HTTP request used to fetch a short-lived AssemblyAI token,
        /// given the user's STT configuration. Pure/testable: performs no I/O.
        /// </summary>
        /// <param name="configuredEndpoint">The raw <c>SttEndpoint</c> from settings.</param>
        /// <param name="assemblyAiApiKey">The user's AssemblyAI API key (direct mode only).</param>
        public static AssemblyAiTokenRequest ResolveTokenRequest(string? configuredEndpoint, string? assemblyAiApiKey)
        {
            // A configured endpoint is treated as a worker/proxy URL only when it
            // is an absolute http(s) URL that is NOT the AssemblyAI host. Anything
            // else (blank, the assemblyai host, or a stale placeholder) resolves to
            // direct mode against the canonical token endpoint.
            if (!string.IsNullOrWhiteSpace(configuredEndpoint) &&
                Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out Uri? parsedEndpoint) &&
                (parsedEndpoint.Scheme == Uri.UriSchemeHttp || parsedEndpoint.Scheme == Uri.UriSchemeHttps) &&
                !parsedEndpoint.Host.EndsWith("assemblyai.com", StringComparison.OrdinalIgnoreCase))
            {
                // Worker mode: POST {workerBaseUrl}/transcribe-token. The worker
                // holds the real key, so no authorization header is sent here.
                string workerBase = configuredEndpoint.TrimEnd('/');
                return new AssemblyAiTokenRequest(
                    $"{workerBase}/transcribe-token",
                    HttpMethod.Post,
                    AuthorizationHeader: null);
            }

            // Direct mode: the API key is required to mint a token.
            if (string.IsNullOrWhiteSpace(assemblyAiApiKey))
            {
                throw new InvalidOperationException(
                    "AssemblyAI transcription is not configured. Enter an AssemblyAI API key in Settings, " +
                    "or point the STT endpoint at your Clicky Cloudflare Worker.");
            }

            // Direct mode: the API key is required to mint a token. The v3/token
            // endpoint REQUIRES an expires_in_seconds query param (a bare GET returns
            // HTTP 422 "Field required"), so append it here -- matching the lifetime
            // the Cloudflare Worker uses in worker mode.
            string directTokenUrlWithLifetime = $"{DirectTokenEndpoint}?expires_in_seconds={TokenLifetimeSeconds}";
            return new AssemblyAiTokenRequest(
                directTokenUrlWithLifetime,
                HttpMethod.Get,
                AuthorizationHeader: assemblyAiApiKey);
        }

        /// <summary>
        /// Opens a streaming transcription session. <paramref name="configuredEndpoint"/>
        /// is the raw <c>SttEndpoint</c> setting; <paramref name="assemblyAiApiKey"/>
        /// is the raw <c>SttApiKey</c> setting (used only in direct mode).
        /// </summary>
        public async Task StartSessionAsync(string? configuredEndpoint, string? assemblyAiApiKey, List<string> keyterms)
        {
            _cts = new CancellationTokenSource();
            _completedTurns.Clear();
            lock (_pendingAudioLock)
            {
                _pendingAudioWhileConnecting.Clear();
            }
            lock (_finalTurnLock)
            {
                _finalTurnReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            try
            {
                AssemblyAiTokenRequest tokenRequest = ResolveTokenRequest(configuredEndpoint, assemblyAiApiKey);

                using var httpRequest = new HttpRequestMessage(tokenRequest.Method, tokenRequest.Url);
                if (tokenRequest.AuthorizationHeader is { } authorizationHeaderValue)
                {
                    httpRequest.Headers.Add("Authorization", authorizationHeaderValue);
                }

                // The token endpoint returns {"token": "..."} (worker) or the same
                // shape directly from AssemblyAI. Either way the "token" field holds
                // the short-lived streaming credential used in the websocket query.
                using HttpResponseMessage tokenResponse = await _httpClient.SendAsync(httpRequest, _cts.Token);
                string tokenJson = await tokenResponse.Content.ReadAsStringAsync(_cts.Token);
                if (!tokenResponse.IsSuccessStatusCode)
                {
                    throw new Exception(
                        $"AssemblyAI token endpoint returned {(int)tokenResponse.StatusCode} {tokenResponse.ReasonPhrase}: {tokenJson}");
                }

                using var tokenDocument = JsonDocument.Parse(tokenJson);
                if (!tokenDocument.RootElement.TryGetProperty("token", out var tokenProperty) ||
                    tokenProperty.ValueKind != JsonValueKind.String)
                {
                    throw new Exception("AssemblyAI token response did not contain a string 'token' field.");
                }
                string streamingToken = tokenProperty.GetString() ?? throw new Exception("AssemblyAI token was null.");

                // 2. Build websocket URL. These query params match the macOS reference
                // (u3-rt-pro model, PCM16 16kHz, formatted turns).
                var queryParams = new List<string>
                {
                    "sample_rate=16000",
                    "encoding=pcm_s16le",
                    "format_turns=true",
                    "speech_model=u3-rt-pro",
                    $"token={streamingToken}"
                };

                if (keyterms.Count > 0)
                {
                    string keytermsJson = JsonSerializer.Serialize(keyterms);
                    queryParams.Add($"keyterms_prompt={Uri.EscapeDataString(keytermsJson)}");
                }

                string wsUrl = $"wss://streaming.assemblyai.com/v3/ws?{string.Join("&", queryParams)}";
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(wsUrl), _cts.Token);
                Logger.Info("AssemblyAI WebSocket connected.");

                // Flush any audio that was captured during the connect window so the
                // start of the user's speech isn't lost (see _pendingAudioWhileConnecting).
                byte[][] pendingAudioChunks;
                lock (_pendingAudioLock)
                {
                    pendingAudioChunks = _pendingAudioWhileConnecting.Count > 0
                        ? _pendingAudioWhileConnecting.ToArray()
                        : Array.Empty<byte[]>();
                    _pendingAudioWhileConnecting.Clear();
                }
                foreach (byte[] pendingChunk in pendingAudioChunks)
                {
                    await SendAudioAsync(pendingChunk);
                }
                if (pendingAudioChunks.Length > 0)
                {
                    Logger.Info($"Flushed {pendingAudioChunks.Length} buffered audio chunk(s) captured during connect");
                }

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
            if (_webSocket == null)
            {
                // Session not started yet (or already torn down). If a session is
                // starting, buffer so the audio isn't lost during the connect window.
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    lock (_pendingAudioLock)
                    {
                        _pendingAudioWhileConnecting.Add(pcmData);
                    }
                }
                return;
            }

            if (_webSocket.State != WebSocketState.Open)
            {
                // Connecting (or closing): buffer until open, then StartSessionAsync flushes.
                if (_webSocket.State == WebSocketState.Connecting && _cts != null && !_cts.IsCancellationRequested)
                {
                    lock (_pendingAudioLock)
                    {
                        _pendingAudioWhileConnecting.Add(pcmData);
                    }
                }
                return;
            }

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
                Logger.Error("Error sending audio to AssemblyAI", ex);
                ErrorOccurred?.Invoke(ex);
            }
        }

        public async Task StopSessionAsync()
        {
            if (_webSocket == null) return;

            // Signal end-of-stream to AssemblyAI so any buffered turn is flushed as
            // a final formatted transcript before the socket closes.
            ClientWebSocket? socketToClose = _webSocket;
            _webSocket = null;

            // Capture the TCS we wait on, then null it so a later session's signal
            // isn't consumed by this stop.
            TaskCompletionSource<bool>? finalTurnSignal;
            lock (_finalTurnLock)
            {
                finalTurnSignal = _finalTurnReceived;
            }

            try
            {
                string terminateMessage = "{\"type\":\"Terminate\"}";
                byte[] terminateBytes = Encoding.UTF8.GetBytes(terminateMessage);
                await socketToClose.SendAsync(
                    new ArraySegment<byte>(terminateBytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    CancellationToken.None);

                // Give the receive loop a bounded grace period to deliver the final
                // formatted turn that AssemblyAI sends in response to Terminate. The
                // previous code closed (and cancelled) immediately, racing the final
                // turn and dropping it -- which is why the log showed Listening ->
                // Processing with no Final transcript received. If a final turn
                // already landed (signal already set), this returns instantly.
                if (finalTurnSignal != null)
                {
                    Task delayTask = Task.Delay(FinalTurnGracePeriodMs);
                    Task finished = await Task.WhenAny(finalTurnSignal.Task, delayTask);
                    if (finished == finalTurnSignal.Task)
                    {
                        Logger.Info("Final transcript turn received before close");
                    }
                    else
                    {
                        Logger.Warn($"No final transcript turn within {FinalTurnGracePeriodMs}ms grace — closing anyway");
                    }
                }

                // Close cleanly so the server sees a normal closure rather than an
                // aborted connection. Do NOT cancel the CTS before this -- the receive
                // loop needs to run through the grace period above.
                if (socketToClose.State == WebSocketState.Open)
                {
                    await socketToClose.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Close requested",
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error closing AssemblyAI socket", ex);
            }
            finally
            {
                socketToClose.Dispose();
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
                lock (_finalTurnLock)
                {
                    _finalTurnReceived = null;
                }
            }
        }

        // Bounded wait for AssemblyAI's final formatted turn after Terminate. Matches
        // the macOS reference's explicit-final-transcript grace period (~1.4s) plus
        // margin for network round-trip. Long enough to catch the final turn, short
        // enough that the user doesn't notice a hang if the turn never comes.
        private const int FinalTurnGracePeriodMs = 1500;

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
                    // All fields are optional in real AssemblyAI envelopes: a
                    // "SessionBegins" or a partial "Turn" may omit end_of_turn /
                    // turn_is_formatted / turn_order. Missing booleans default to
                    // false and a missing turn_order to 0 so no message is dropped.
                    string transcript = ReadOptionalString(root, "transcript");
                    bool endOfTurn = ReadOptionalBoolean(root, "end_of_turn");
                    bool turnIsFormatted = ReadOptionalBoolean(root, "turn_is_formatted");
                    int turnOrder = ReadOptionalInt32(root, "turn_order");

                    if (endOfTurn || turnIsFormatted)
                    {
                        _completedTurns[turnOrder] = transcript;
                        string finalTranscript = GetCombinedTranscript();
                        TranscriptUpdated?.Invoke(finalTranscript);
                        FinalTranscriptReady?.Invoke(finalTranscript);

                        // Signal StopSessionAsync that a final turn landed, so it can
                        // close promptly rather than waiting out the full grace period
                        // on every utterance.
                        lock (_finalTurnLock)
                        {
                            _finalTurnReceived?.TrySetResult(true);
                        }
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
                    // AssemblyAI error envelopes carry either "error" or "message".
                    string error = ReadOptionalString(root, "error");
                    if (string.IsNullOrEmpty(error))
                    {
                        error = ReadOptionalString(root, "message");
                    }
                    if (string.IsNullOrEmpty(error))
                    {
                        error = "Unknown error";
                    }
                    ErrorOccurred?.Invoke(new Exception($"AssemblyAI: {error}"));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error parsing AssemblyAI message: {ex.Message}");
            }
        }

        /// <summary>Reads a string property if present and a string, else "".</summary>
        private static string ReadOptionalString(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? ""
                : "";
        }

        /// <summary>Reads a boolean property if present and a boolean, else false.</summary>
        private static bool ReadOptionalBoolean(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;
        }

        /// <summary>Reads an integer property if present and a number, else 0.</summary>
        private static int ReadOptionalInt32(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
                ? property.GetInt32()
                : 0;
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

