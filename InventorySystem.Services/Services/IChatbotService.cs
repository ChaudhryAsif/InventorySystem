using System.Collections.Generic;
using System.Threading.Tasks;

namespace InventorySystem.Core.Services
{
    public interface IChatbotService
    {
        Task ProcessInboundMessageAsync(string channelPhoneNumberId, string fromPhone, string customerName, string messageText, string whatsappMessageId, bool wasVoiceInput = false);
        Task<bool> HumanReplyAsync(int threadId, string replyText);
        Task<bool> CloseThreadAsync(int threadId);

        Task ProcessVoiceMessageAsync(
    string channelPhoneNumberId, string fromPhone,
    string customerName, string audioId, string whatsappMessageId);

        // Runs the full agent pipeline (AI + live product/stock lookups + order flow) against an
        // in-memory transcript and returns the reply text. No WhatsApp message is sent, no inbox
        // thread is touched, and no sale is created — for testing the LLM/agent from the UI.
        Task<string> SimulatePlaygroundReplyAsync(
            List<PlaygroundTurn> history, string userMessage,
            string systemPrompt, string model, string apiKey, string customerName);
    }

    // A single prior turn in the playground transcript (kept client-side, never persisted).
    public class PlaygroundTurn
    {
        public string Sender { get; set; } = "customer"; // "customer" | "ai"
        public string Text { get; set; } = "";
    }
}
