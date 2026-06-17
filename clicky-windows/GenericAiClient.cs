using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using clicky_windows.Models;

namespace clicky_windows
{
    public class GenericAiClient
    {
        private readonly HttpClient _httpClient;

        public GenericAiClient()
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
            string userPrompt)
        {
            var settings = SettingsManager.Settings;
            string format = settings.AiFormat.ToLower();
            
            if (format == "anthropic")
            {
                return await AnalyzeAnthropicAsync(images, systemPrompt, conversationHistory, userPrompt, settings);
            }
            else
            {
                // Default: OpenAI Chat Completions format
                return await AnalyzeOpenAiCompatibleAsync(images, systemPrompt, conversationHistory, userPrompt, settings);
            }
        }

        private async Task<string> AnalyzeAnthropicAsync(
            List<ScreenCapture> images,
            string systemPrompt,
            List<(string userTranscript, string assistantResponse)> conversationHistory,
            string userPrompt,
            ClickySettings settings)
        {
            var messages = new List<object>();

            // Add history
            foreach (var exchange in conversationHistory)
            {
                messages.Add(new { role = "user", content = exchange.userTranscript });
                messages.Add(new { role = "assistant", content = exchange.assistantResponse });
            }

            // Build current user message with images and prompt
            var contentBlocks = new List<object>();
            
            // Only add images if the configured provider capabilities support vision
            if (settings.AiSupportsVision)
            {
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
                model = settings.AiModel,
                max_tokens = 1024,
                system = systemPrompt,
                messages = messages,
                stream = false
            };

            var json = JsonSerializer.Serialize(requestBody);
            
            Console.WriteLine($"🌐 GenericAiClient: Sending Anthropic-format request for model {settings.AiModel} to {settings.AiEndpoint}...");
            
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.AiEndpoint);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            
            ApplyAuthentication(request, settings);
            ApplyCustomHeaders(request, settings);

            var response = await _httpClient.SendAsync(request);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Anthropic-format endpoint returned error: {response.StatusCode} - {responseText}");
            }

            using var doc = JsonDocument.Parse(responseText);
            var contentArray = doc.RootElement.GetProperty("content");
            if (contentArray.GetArrayLength() > 0)
            {
                return contentArray[0].GetProperty("text").GetString() ?? "";
            }

            throw new Exception("Empty response received from Anthropic-format endpoint.");
        }

        private async Task<string> AnalyzeOpenAiCompatibleAsync(
            List<ScreenCapture> images,
            string systemPrompt,
            List<(string userTranscript, string assistantResponse)> conversationHistory,
            string userPrompt,
            ClickySettings settings)
        {
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };

            // Add history
            foreach (var exchange in conversationHistory)
            {
                messages.Add(new { role = "user", content = exchange.userTranscript });
                messages.Add(new { role = "assistant", content = exchange.assistantResponse });
            }

            // Build current user message with images and prompt
            var contentBlocks = new List<object>();

            // Only add images if the configured provider capabilities support vision
            if (settings.AiSupportsVision)
            {
                foreach (var img in images)
                {
                    contentBlocks.Add(new
                    {
                        type = "image_url",
                        image_url = new
                        {
                            url = $"data:image/jpeg;base64,{Convert.ToBase64String(img.ImageData)}"
                        }
                    });
                    contentBlocks.Add(new
                    {
                        type = "text",
                        text = $"Image description: {img.Label}"
                    });
                }
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
                model = settings.AiModel,
                messages = messages,
                stream = false
            };

            var json = JsonSerializer.Serialize(requestBody);
            
            Console.WriteLine($"🌐 GenericAiClient: Sending OpenAI-format request for model {settings.AiModel} to {settings.AiEndpoint}...");
            
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.AiEndpoint);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            
            ApplyAuthentication(request, settings);
            ApplyCustomHeaders(request, settings);

            var response = await _httpClient.SendAsync(request);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"OpenAI-format endpoint returned error: {response.StatusCode} - {responseText}");
            }

            using var doc = JsonDocument.Parse(responseText);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() > 0)
            {
                var message = choices[0].GetProperty("message");
                return message.GetProperty("content").GetString() ?? "";
            }

            throw new Exception("Empty response received from OpenAI-format endpoint.");
        }

        private void ApplyAuthentication(HttpRequestMessage request, ClickySettings settings)
        {
            string authType = settings.AiAuthType.ToLower();
            string key = settings.AiApiKey;

            if (string.IsNullOrWhiteSpace(key)) return;

            if (authType == "bearer")
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            }
            else if (authType == "apikey")
            {
                request.Headers.Add("x-api-key", key);
                // Standard Anthropic compatibility requirement when sending api key directly
                if (settings.AiFormat.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Add("anthropic-version", "2023-06-01");
                }
            }
        }

        private void ApplyCustomHeaders(HttpRequestMessage request, ClickySettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.AiCustomHeadersJson) || settings.AiCustomHeadersJson == "{}") return;

            try
            {
                using var doc = JsonDocument.Parse(settings.AiCustomHeadersJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (request.Headers.Contains(prop.Name))
                    {
                        request.Headers.Remove(prop.Name);
                    }
                    request.Headers.Add(prop.Name, prop.Value.GetString() ?? "");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to parse custom headers JSON: {ex.Message}");
            }
        }
    }
}
