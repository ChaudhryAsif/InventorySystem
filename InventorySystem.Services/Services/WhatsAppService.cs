using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Core.Services
{
    // Thin wrapper around Meta's WhatsApp Cloud API (Graph v19).
    // Handles outbound text/audio messages, media upload/download, and webhook subscription.
    public class WhatsAppService : IWhatsAppService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WhatsAppService> _logger;
        private const string GraphApiBase = "https://graph.facebook.com/v19.0";

        public WhatsAppService(IHttpClientFactory httpClientFactory, ILogger<WhatsAppService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        // Send a plain text message to a customer.
        public async Task<WhatsAppSendResult> SendTextMessageAsync(
            string phoneNumberId, string accessToken, string toPhone, string message)
        {
            try
            {
                var client = CreateAuthenticatedClient(accessToken);

                var payload = new
                {
                    messaging_product = "whatsapp",
                    to = toPhone,
                    type = "text",
                    text = new { body = message }
                };

                var response = await client.PostAsync(
                    $"{GraphApiBase}/{phoneNumberId}/messages",
                    BuildJsonContent(payload));

                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return new WhatsAppSendResult { Success = false, Error = body };

                return new WhatsAppSendResult
                {
                    Success = true,
                    MessageId = ExtractMessageId(body)
                };
            }
            catch (Exception ex)
            {
                return new WhatsAppSendResult { Success = false, Error = ex.Message };
            }
        }

        // Subscribe our app to receive inbound messages on this phone number.
        // Called once after the channel is configured — we still keep it idempotent.
        public async Task<bool> RegisterWebhookAsync(
            string phoneNumberId, string accessToken, string webhookUrl, string verifyToken)
        {
            try
            {
                var client = CreateAuthenticatedClient(accessToken);

                var payload = new { subscribed_fields = new[] { "messages" } };

                var response = await client.PostAsync(
                    $"{GraphApiBase}/{phoneNumberId}/subscribed_apps",
                    BuildJsonContent(payload));

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // Download a voice/audio file the customer sent us.
        // Meta returns only a media ID in the webhook — we need two calls to get the actual bytes:
        //   1) GET /{media-id}  → returns the signed download URL
        //   2) GET that URL     → returns the binary stream
        public async Task<Stream?> DownloadMediaAsync(string mediaId, string accessToken)
        {
            try
            {
                var client = CreateAuthenticatedClient(accessToken);

                // Step 1: resolve the signed media URL
                var metaResponse = await client.GetAsync($"{GraphApiBase}/{mediaId}");
                if (!metaResponse.IsSuccessStatusCode) return null;

                var metaBody = await metaResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(metaBody);

                var mediaUrl = doc.RootElement.GetProperty("url").GetString();
                if (string.IsNullOrEmpty(mediaUrl)) return null;

                // Step 2: download the actual file bytes
                var fileResponse = await client.GetAsync(mediaUrl);
                if (!fileResponse.IsSuccessStatusCode) return null;

                return await fileResponse.Content.ReadAsStreamAsync();
            }
            catch
            {
                return null;
            }
        }

        // Upload an audio file to Meta so we can later send it as a voice message.
        // Meta is strict about multipart field ordering — messaging_product must come first,
        // then the file, then the type. Don't reshuffle this.
        public async Task<string?> UploadMediaAsync(
            string phoneNumberId, string accessToken, byte[] audioBytes, string mimeType)
        {
            try
            {
                var client = CreateAuthenticatedClient(accessToken);

                using var multipart = new MultipartFormDataContent();
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
                multipart.Add(new StringContent(mimeType), "type");

                var response = await client.PostAsync(
                    $"{GraphApiBase}/{phoneNumberId}/media", multipart);
                var responseBody = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("WhatsApp upload [{StatusCode}]: {ResponseBody}", response.StatusCode, responseBody);

                if (!response.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(responseBody);
                return doc.RootElement.GetProperty("id").GetString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WhatsApp upload exception");
                return null;
            }
        }

        // Send a previously-uploaded audio file as a voice note.
        // voice = true tells WhatsApp to render it as a push-to-talk bubble instead of an audio attachment.
        public async Task<WhatsAppSendResult> SendAudioMessageAsync(
            string phoneNumberId, string accessToken, string toPhone, string mediaId)
        {
            try
            {
                var client = CreateAuthenticatedClient(accessToken);

                var payload = new
                {
                    messaging_product = "whatsapp",
                    to = toPhone,
                    type = "audio",
                    audio = new { id = mediaId, voice = true }
                };

                var response = await client.PostAsync(
                    $"{GraphApiBase}/{phoneNumberId}/messages",
                    BuildJsonContent(payload));

                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return new WhatsAppSendResult { Success = false, Error = body };

                return new WhatsAppSendResult
                {
                    Success = true,
                    MessageId = ExtractMessageId(body)
                };
            }
            catch (Exception ex)
            {
                return new WhatsAppSendResult { Success = false, Error = ex.Message };
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private HttpClient CreateAuthenticatedClient(string accessToken)
        {
            var client = _httpClientFactory.CreateClient("MetaClient");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            return client;
        }

        private static StringContent BuildJsonContent(object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        // Pull the message ID out of Meta's standard success response.
        private static string? ExtractMessageId(string responseBody)
        {
            using var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement
                .GetProperty("messages")[0]
                .GetProperty("id")
                .GetString();
        }
    }
}