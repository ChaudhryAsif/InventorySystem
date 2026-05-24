using InventorySystem.Services.Services;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;

namespace InventorySystem.Core.Services
{
    // Speech-to-text via Groq (Whisper Large v3 Turbo) and text-to-speech via the
    // unofficial Google Translate TTS endpoint. The TTS endpoint is free but has a
    // ~200 char limit per request, so longer replies are split into sentence chunks
    // and concatenated client-side.
    public class SpeechService : ISpeechService
    {
        private readonly string _groqApiKey;
        private readonly IHttpClientFactory _httpClientFactory;

        // Google Translate TTS hard-caps each call at ~200 chars. Leave a little headroom.
        private const int TtsChunkSize = 190;

        // Spoofing a desktop Chrome UA is required — the endpoint returns 403 for default .NET UAs.
        private const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        public SpeechService(IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _groqApiKey = config["Groq:ApiKey"]
                ?? throw new Exception("Groq API Key missing in appsettings.json");
            _httpClientFactory = httpClientFactory;
        }

        // Transcribe a WhatsApp voice note (OGG/Opus) to text using Groq Whisper.
        // Returns empty string on any failure — caller decides how to handle that.
        public async Task<string> SpeechToTextAsync(Stream audioStream)
        {
            try
            {
                using var ms = new MemoryStream();
                await audioStream.CopyToAsync(ms);
                var audioBytes = ms.ToArray();
                if (audioBytes.Length == 0) return "";

                using var form = new MultipartFormDataContent();

                var audioContent = new ByteArrayContent(audioBytes);
                audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
                form.Add(audioContent, "file", "voice.ogg");

                // whisper-large-v3-turbo: best speed/accuracy tradeoff Groq currently offers.
                form.Add(new StringContent("whisper-large-v3-turbo"), "model");
                form.Add(new StringContent("en"), "language");
                form.Add(new StringContent("text"), "response_format");

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

                var transcribedText = await response.Content.ReadAsStringAsync();
                return transcribedText.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Groq Whisper exception: {ex.Message}");
                return "";
            }
        }

        // Convert outbound text to MP3 audio bytes using Google Translate's TTS.
        // Longer text is split into chunks and the resulting MP3 streams are concatenated.
        // MP3 happens to be one of the few codecs where naive byte concatenation plays back
        // correctly in most clients including WhatsApp — works for our purposes.
        public async Task<byte[]?> TextToSpeechAsync(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return null;

                Console.WriteLine($"Google TTS [en]: {text[..Math.Min(80, text.Length)]}...");

                var chunks = SplitIntoChunks(text, TtsChunkSize);
                var allAudio = new List<byte>();

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);

                foreach (var chunk in chunks)
                {
                    var encodedText = Uri.EscapeDataString(chunk);
                    var url = $"https://translate.google.com/translate_tts" +
                              $"?ie=UTF-8&q={encodedText}&tl=en&client=tw-ob";

                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Google TTS error: {response.StatusCode}");
                        return null;
                    }

                    allAudio.AddRange(await response.Content.ReadAsByteArrayAsync());
                }

                var audioBytes = allAudio.ToArray();
                Console.WriteLine($"Google TTS success: {audioBytes.Length} bytes");
                return audioBytes.Length > 0 ? audioBytes : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Google TTS exception: {ex.Message}");
                return null;
            }
        }

        // Split text into chunks no longer than maxLength, breaking at sentence boundaries
        // where possible so the resulting audio doesn't cut mid-word.
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