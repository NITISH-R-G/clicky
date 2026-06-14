using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace clicky_windows
{
    public class ClaudeClient
    {
        private readonly HttpClient _httpClient;

        public ClaudeClient()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(3)
            };
        }

        public async Task<string> AnalyzeImageAsync(
            List<ScreenCapture> images,
            string systemPrompt,
            List<(string userTranscript, string assistantResponse)> conversationHistory,
            string userPrompt,
            string model,
            string proxyUrl)
        {
            // Build the messages list
            var messages = new List<object>();

            // Add history
            foreach (var exchange in conversationHistory)
            {
                messages.Add(new { role = "user", content = exchange.userTranscript });
                messages.Add(new { role = "assistant", content = exchange.assistantResponse });
            }

            // Build current user message with images and prompt
            var contentBlocks = new List<object>();
            foreach (var img in images)
            {
                contentBlocks.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = "image/jpeg",
                        data = Convert.ToBase64String(img.ImageData)
                    }
                });
                contentBlocks.Add(new
                {
                    type = "text",
                    text = img.Label
                });
            }

            contentBlocks.Add(new
            {
                type = "text",
                text = userPrompt
            });

            messages.Add(new
            {
                role = "user",
                content = contentBlocks
            });

            var requestBody = new
            {
                model = model,
                max_tokens = 1024,
                system = systemPrompt,
                messages = messages,
                stream = false
            };

            var json = JsonSerializer.Serialize(requestBody);
            
            Console.WriteLine($"🌐 Claude Client: Sending vision request with {images.Count} images...");
            
            int maxRetryAttempts = 3;
            int delayMs = 1000;
            HttpResponseMessage? response = null;
            string responseText = "";

            for (int attempt = 1; attempt <= maxRetryAttempts; attempt++)
            {
                try
                {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    response = await _httpClient.PostAsync(proxyUrl, content);
                    responseText = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }
                    
                    Console.WriteLine($"🌐 Claude API Attempt {attempt} failed: {response.StatusCode} - {responseText}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"🌐 Claude API Attempt {attempt} threw exception: {ex.Message}");
                    if (attempt == maxRetryAttempts) throw;
                }

                if (attempt < maxRetryAttempts)
                {
                    int jitter = new Random().Next(-200, 200);
                    int currentDelay = Math.Max(100, (delayMs * attempt) + jitter);
                    Console.WriteLine($"🌐 Retrying in {currentDelay}ms...");
                    await Task.Delay(currentDelay);
                }
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                throw new Exception($"Claude API Error after {maxRetryAttempts} attempts. Status: {response?.StatusCode}, Response: {responseText}");
            }

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            if (root.TryGetProperty("content", out var contentArray) && contentArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in contentArray.EnumerateArray())
                {
                    if (element.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "text")
                    {
                        if (element.TryGetProperty("text", out var textProp))
                        {
                            return textProp.GetString() ?? "";
                        }
                    }
                }
            }

            throw new Exception("Could not find text response in Claude payload.");
        }
    }
}
