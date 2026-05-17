using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace InventorySystem.Core.Services
{
    public class WhatsAppService : IWhatsAppService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private const string GraphApiBase = "https://graph.facebook.com/v19.0";

        public WhatsAppService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<WhatsAppSendResult> SendTextMessageAsync(
            string phoneNumberId, string accessToken, string toPhone, string message)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("MetaClient");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var payload = new
                {
                    messaging_product = "whatsapp",
                    to = toPhone,
                    type = "text",
                    text = new { body = message }
                };

                //var payload = new
                //{
                //    messaging_product = "whatsapp",
                //    to = toPhone,
                //    type = "template",
                //    template = new
                //    {
                //        name = "hello_world",
                //        language = new { code = "en_US" }
                //    }
                //};


                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"{GraphApiBase}/{phoneNumberId}/messages", content);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return new WhatsAppSendResult { Success = false, Error = body };

                var doc = JsonDocument.Parse(body);
                var msgId = doc.RootElement
                    .GetProperty("messages")[0]
                    .GetProperty("id")
                    .GetString();

                return new WhatsAppSendResult { Success = true, MessageId = msgId };
            }
            catch (Exception ex)
            {
                return new WhatsAppSendResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<bool> RegisterWebhookAsync(
            string phoneNumberId, string accessToken, string webhookUrl, string verifyToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("MetaClient");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var payload = new
                {
                    subscribed_fields = new[] { "messages" }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(
                    $"{GraphApiBase}/{phoneNumberId}/subscribed_apps", content);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}