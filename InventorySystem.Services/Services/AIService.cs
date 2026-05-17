using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace InventorySystem.Core.Services
{
    /// <summary>
    /// Uses Groq Cloud API (free tier) — compatible with OpenAI SDK format.
    /// Get your free API key at https://console.groq.com
    /// Default model: llama3-8b-8192 (fast + free)
    /// </summary>
    public class AIService : IAIService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public AIService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<AiReplyResult> GetReplyAsync(
            string systemPrompt,
            string conversationHistory,
            string userMessage,
            string apiKey,
            string model)
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

                // Include conversation history (last 10 exchanges)
                if (!string.IsNullOrWhiteSpace(conversationHistory))
                    messages.Add(new { role = "user", content = $"[Conversation so far]\n{conversationHistory}" });

                messages.Add(new { role = "user", content = userMessage });

                var payload = new
                {
                    model = string.IsNullOrWhiteSpace(model) ? "llama-3.1-8b-instant" : model,
                    messages,
                    max_tokens = 500,
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
    }
}