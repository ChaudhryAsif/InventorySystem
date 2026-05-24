using InventorySystem.Services.Services;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text.Json;

namespace InventorySystem.Core.Services
{
    public class SpeechService : ISpeechService
    {
        private readonly string _groqApiKey;
        private readonly IHttpClientFactory _httpClientFactory;

        public SpeechService(IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _groqApiKey = config["Groq:ApiKey"]
                ?? throw new Exception("Groq API Key missing in appsettings.json");
            _httpClientFactory = httpClientFactory;
        }

        public async Task<string> SpeechToTextAsync(Stream audioStream)
        {
            try
            {
                // 1. Read the audio bytes from the stream
                using var ms = new MemoryStream();
                await audioStream.CopyToAsync(ms);
                var audioBytes = ms.ToArray();

                if (audioBytes.Length == 0)
                    return "";

                // 2. Build multipart form data for Groq Whisper
                using var form = new MultipartFormDataContent();

                var audioContent = new ByteArrayContent(audioBytes);
                audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
                form.Add(audioContent, "file", "voice.ogg");

                // Use Whisper Large v3 Turbo — fastest and most accurate
                form.Add(new StringContent("whisper-large-v3-turbo"), "model");

                // Optional: set language (remove if you want auto-detect)
                //form.Add(new StringContent("en"), "language"); // "ur" for Urdu
                //form.Add(new StringContent("ur"), "language"); // "ur" for Urdu

                // Response format: just plain text
                form.Add(new StringContent("text"), "response_format");

                // 3. Send to Groq
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _groqApiKey);

                var response = await client.PostAsync(
                    "https://api.groq.com/openai/v1/audio/transcriptions", form);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Groq Whisper error: {error}");
                    return "";
                }

                // 4. Return the transcribed text
                var transcribedText = await response.Content.ReadAsStringAsync();
                return transcribedText.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Groq Whisper exception: {ex.Message}");
                return "";
            }
        }

        // Text-to-Speech — we'll skip for now (reply with text)
        //public async Task<byte[]?> TextToSpeechAsync(string text)
        //{
        //    await Task.CompletedTask;
        //    return null;
        //}

        public async Task<byte[]?> TextToSpeechAsync(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                // ✅ Auto-detect language: Urdu if Arabic/Urdu script detected, else English
                var language = ContainsUrduScript(text) ? "ur" : "en";

                Console.WriteLine($"🎤 Google TTS [{language}]: {text.Substring(0, Math.Min(80, text.Length))}...");

                var chunks = SplitIntoChunks(text, 190);
                var allAudio = new List<byte>();

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                    "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                foreach (var chunk in chunks)
                {
                    var encodedText = Uri.EscapeDataString(chunk);
                    var url = $"https://translate.google.com/translate_tts?ie=UTF-8&q={encodedText}&tl={language}&client=tw-ob";

                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"❌ Google TTS error: {response.StatusCode}");
                        return null;
                    }

                    var chunkAudio = await response.Content.ReadAsByteArrayAsync();
                    allAudio.AddRange(chunkAudio);
                }

                var audioBytes = allAudio.ToArray();
                Console.WriteLine($"✅ Google TTS success: {audioBytes.Length} bytes");
                return audioBytes.Length > 0 ? audioBytes : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Google TTS exception: {ex.Message}");
                return null;
            }
        }

        // Helper: Check if text contains Urdu/Arabic script characters
        private static bool ContainsUrduScript(string text)
        {
            foreach (var c in text)
            {
                // Arabic/Urdu Unicode block: 0x0600 to 0x06FF
                if (c >= 0x0600 && c <= 0x06FF)
                    return true;
            }
            return false;
        }

        // Helper: Split long text into smaller chunks at natural breakpoints
        private static List<string> SplitIntoChunks(string text, int maxLength)
        {
            var chunks = new List<string>();

            if (text.Length <= maxLength)
            {
                chunks.Add(text);
                return chunks;
            }

            var sentences = text.Split(new[] { '.', '!', '?', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

            var currentChunk = "";
            foreach (var sentence in sentences)
            {
                var s = sentence.Trim();
                if (string.IsNullOrEmpty(s)) continue;

                if (currentChunk.Length + s.Length + 2 <= maxLength)
                {
                    currentChunk += (currentChunk.Length > 0 ? ". " : "") + s;
                }
                else
                {
                    if (currentChunk.Length > 0) chunks.Add(currentChunk + ".");
                    currentChunk = s;
                }
            }

            if (currentChunk.Length > 0) chunks.Add(currentChunk);
            return chunks;
        }
    }
}