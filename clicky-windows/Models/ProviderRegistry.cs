using System;
using System.Collections.Generic;

namespace clicky_windows.Models
{
    public class ProviderPreset
    {
        public string Name { get; set; } = "";
        public string DefaultModel { get; set; } = "";
        public string DefaultEndpoint { get; set; } = "";
        public string Format { get; set; } = "OpenAI"; // "OpenAI" or "Anthropic"
        public string AuthType { get; set; } = "Bearer"; // "Bearer", "ApiKey", "None", "CustomHeader"
        public string CustomHeadersJson { get; set; } = "{}";
        public bool SupportsVision { get; set; } = true;
        public bool SupportsStreaming { get; set; } = false;
    }

    public static class ProviderRegistry
    {
        private static readonly List<ProviderPreset> Presets = new()
        {
            new ProviderPreset
            {
                Name = "Anthropic (Claude)",
                DefaultModel = "claude-3-5-sonnet-20241022",
                DefaultEndpoint = "https://api.anthropic.com/v1/messages",
                Format = "Anthropic",
                AuthType = "ApiKey",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "OpenAI",
                DefaultModel = "gpt-4o",
                DefaultEndpoint = "https://api.openai.com/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "Bearer",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "Google Gemini",
                DefaultModel = "gemini-1.5-flash",
                DefaultEndpoint = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
                Format = "OpenAI",
                AuthType = "Bearer",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "Groq",
                DefaultModel = "llama3-8b-8192",
                DefaultEndpoint = "https://api.groq.com/openai/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "Bearer",
                SupportsVision = true
            },
            new ProviderPreset
            {
                // Free vision-capable model verified live against
                // https://openrouter.ai/api/v1/models (the :free image-input list changes
                // often, so this is checked against the current catalog). Clicky sends a
                // screenshot with every turn, so the default MUST accept image input or
                // the AI call 404s. Past defaults that broke: meta-llama/llama-3-8b-instruct:free
                // (retired, also non-vision) and google/gemini-2.0-flash-exp:free (retired).
                // If this slug 404s in the future, the live free-vision list is the source
                // of truth; gemma-4-31b-it:free / gemma-4-26b-a4b-it:free / nemotron-nano-12b-v2-vl:free
                // are alternatives as of this check.
                Name = "OpenRouter",
                DefaultModel = "google/gemma-4-31b-it:free",
                DefaultEndpoint = "https://openrouter.ai/api/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "Bearer",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "Ollama (Local)",
                DefaultModel = "llama3",
                DefaultEndpoint = "http://localhost:11434/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "None",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "LM Studio (Local)",
                DefaultModel = "local-model",
                DefaultEndpoint = "http://localhost:1234/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "None",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "LocalAI",
                DefaultModel = "local-model",
                DefaultEndpoint = "http://localhost:8080/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "None",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "vLLM (Local)",
                DefaultModel = "local-model",
                DefaultEndpoint = "http://localhost:8000/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "None",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "llama.cpp (Local)",
                DefaultModel = "local-model",
                DefaultEndpoint = "http://localhost:8080/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "None",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "KoboldCpp (Local)",
                DefaultModel = "local-model",
                DefaultEndpoint = "http://localhost:5001/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "None",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "Open WebUI",
                DefaultModel = "local-model",
                DefaultEndpoint = "http://localhost:3000/api/chat/completions",
                Format = "OpenAI",
                AuthType = "Bearer",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "Hugging Face",
                DefaultModel = "meta-llama/Meta-Llama-3-8B-Instruct",
                DefaultEndpoint = "https://api-inference.huggingface.co/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "Bearer",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "Together AI",
                DefaultModel = "meta-llama/Llama-3-8b-chat-hf",
                DefaultEndpoint = "https://api.together.xyz/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "Bearer",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "DeepInfra",
                DefaultModel = "meta-llama/Meta-Llama-3-8B-Instruct",
                DefaultEndpoint = "https://api.deepinfra.com/v1/openai/chat/completions",
                Format = "OpenAI",
                AuthType = "Bearer",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "Fireworks AI",
                DefaultModel = "accounts/fireworks/models/llama-v3-8b-instruct",
                DefaultEndpoint = "https://api.fireworks.ai/inference/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "Bearer",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "Nebius AI",
                DefaultModel = "meta-llama/Meta-Llama-3.1-8B-Instruct",
                DefaultEndpoint = "https://api.studio.nebius.ai/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "Bearer",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "Perplexity",
                DefaultModel = "llama-3-sonar-large-32k-online",
                DefaultEndpoint = "https://api.perplexity.ai/chat/completions",
                Format = "OpenAI",
                AuthType = "Bearer",
                SupportsVision = false
            },
            new ProviderPreset
            {
                Name = "Mistral AI",
                DefaultModel = "mistral-large-latest",
                DefaultEndpoint = "https://api.mistral.ai/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "Bearer",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "xAI (Grok)",
                DefaultModel = "grok-beta",
                DefaultEndpoint = "https://api.x.ai/v1/chat/completions",
                Format = "OpenAI",
                AuthType = "Bearer",
                SupportsVision = true
            },
            new ProviderPreset
            {
                Name = "Custom Provider",
                DefaultModel = "",
                DefaultEndpoint = "",
                Format = "OpenAI",
                AuthType = "None",
                SupportsVision = true
            }
        };

        public static List<ProviderPreset> GetPresets()
        {
            return Presets;
        }

        public static ProviderPreset GetPreset(string name)
        {
            return Presets.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? Presets[Presets.Count - 1]; // Default to Custom
        }
    }
}
