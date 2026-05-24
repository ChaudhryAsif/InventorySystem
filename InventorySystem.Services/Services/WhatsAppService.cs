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

        // Download voice/audio file from Meta
        public async Task<Stream?> DownloadMediaAsync(string mediaId, string accessToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("MetaClient");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // Step 1: Get the media URL from Meta
                var metaResponse = await client.GetAsync($"{GraphApiBase}/{mediaId}");
                if (!metaResponse.IsSuccessStatusCode) return null;

                var metaBody = await metaResponse.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(metaBody);
                var mediaUrl = doc.RootElement.GetProperty("url").GetString();
                if (string.IsNullOrEmpty(mediaUrl)) return null;

                // Step 2: Download the actual file
                var fileResponse = await client.GetAsync(mediaUrl);
                if (!fileResponse.IsSuccessStatusCode) return null;

                return await fileResponse.Content.ReadAsStreamAsync();
            }
            catch { return null; }
        }

        public async Task<string?> UploadMediaAsync(
            string phoneNumberId, string accessToken, byte[] audioBytes, string mimeType)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("MetaClient");
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);

                using var multipart = new MultipartFormDataContent();

                // ✅ Order matters for Meta — try this exact order
                multipart.Add(new StringContent("whatsapp"), "messaging_product");

                var fileContent = new ByteArrayContent(audioBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

                var fileName = mimeType switch
                {
                    "audio/mpeg" => "voice.mp3",
                    "audio/ogg" => "voice.ogg",
                    "audio/wav" => "voice.wav",
                    _ => "voice.mp3"
                };

                multipart.Add(fileContent, "file", fileName);

                // ✅ Send "type" AFTER the file
                multipart.Add(new StringContent(mimeType), "type");

                var response = await client.PostAsync(
                    $"{GraphApiBase}/{phoneNumberId}/media", multipart);

                var responseBody = await response.Content.ReadAsStringAsync();

                // ✅ Better logging
                Console.WriteLine($"📤 WhatsApp Upload Status: {response.StatusCode}");
                Console.WriteLine($"📤 WhatsApp Upload Body: {responseBody}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ WhatsApp upload failed: {responseBody}");
                    return null;
                }

                var doc = JsonDocument.Parse(responseBody);
                var mediaId = doc.RootElement.GetProperty("id").GetString();

                Console.WriteLine($"✅ WhatsApp upload success. Media ID: {mediaId}");
                return mediaId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ WhatsApp upload exception: {ex.Message}");
                return null;
            }
        }

        public async Task<WhatsAppSendResult> SendAudioMessageAsync(
            string phoneNumberId, string accessToken, string toPhone, string mediaId)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("MetaClient");
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);

                var payload = new
                {
                    messaging_product = "whatsapp",
                    to = toPhone,
                    type = "audio",
                    audio = new { id = mediaId, voice = true }  // ✅ voice = true → true voice note
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json,
                    System.Text.Encoding.UTF8, "application/json");

                var response = await client.PostAsync(
                    $"{GraphApiBase}/{phoneNumberId}/messages", content);
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
    }
}