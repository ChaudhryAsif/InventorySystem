using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace InventorySystem.Core.Services
{
    public class AIService : IAIService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public AIService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<AiReplyResult> GetReplyAsync(string systemPrompt, string conversationHistory, string userMessage, string apiKey, string model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                    return new AiReplyResult { NeedsHuman = true, Error = "AI API key not configured." };

                var client = _httpClientFactory.CreateClient("GroqClient");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var messages = new List<object>
                {
                    new { role = "system", content = systemPrompt }
                };

                if (!string.IsNullOrWhiteSpace(conversationHistory))
                    messages.Add(new { role = "user", content = $"[Conversation so far]\n{conversationHistory}" });

                messages.Add(new { role = "user", content = userMessage });

                var payload = new
                {
                    model = string.IsNullOrWhiteSpace(model) ? "llama-3.1-8b-instant" : model,
                    messages,
                    max_tokens = 600,
                    temperature = 0.7
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return new AiReplyResult { NeedsHuman = true, Error = $"AI API error: {response.StatusCode}" };

                var doc = JsonDocument.Parse(responseBody);
                var reply = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "";

                if (reply.Contains("HUMAN_NEEDED", StringComparison.OrdinalIgnoreCase))
                    return new AiReplyResult { NeedsHuman = true, ModelUsed = model };

                return new AiReplyResult { NeedsHuman = false, Reply = reply.Trim(), ModelUsed = model };
            }
            catch (Exception ex)
            {
                return new AiReplyResult { NeedsHuman = true, Error = ex.Message };
            }
        }

        public async Task<OrderExtractResult?> ExtractOrderAsync(string conversationHistory, string apiKey, string model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(apiKey)) return null;

                var client = _httpClientFactory.CreateClient("GroqClient");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var extractPrompt =
                    "You are a data extraction assistant. " +
                    "From the WhatsApp conversation below, extract the confirmed order details. " +
                    "Return ONLY a valid JSON object — no explanation, no markdown, no code block. " +
                    "Use exactly this format:\n" +
                    "{\"customerName\":\"...\",\"city\":\"...\",\"productName\":\"...\",\"quantity\":0,\"unitPrice\":0,\"totalAmount\":0}\n" +
                    "If a field is unknown use null for strings and 0 for numbers.";

                var messages = new List<object>
                {
                    new { role = "system", content = extractPrompt },
                    new { role = "user",   content = conversationHistory }
                };

                var payload = new
                {
                    model = string.IsNullOrWhiteSpace(model) ? "llama-3.1-8b-instant" : model,
                    messages,
                    max_tokens = 200,
                    temperature = 0.0  // deterministic for JSON extraction
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);
                if (!response.IsSuccessStatusCode) return null;

                var responseBody = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(responseBody);
                var raw = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "";

                // Strip any accidental markdown fences
                raw = raw.Trim();
                if (raw.StartsWith("```")) raw = raw.Split('\n', 2)[1];
                if (raw.EndsWith("```")) raw = raw[..^3];
                raw = raw.Trim();

                var order = JsonSerializer.Deserialize<OrderExtractResult>(raw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return order;
            }
            catch
            {
                return null;
            }
        }
    }
}