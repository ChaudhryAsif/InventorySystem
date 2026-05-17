using System.Threading.Tasks;

namespace InventorySystem.Core.Services
{
    public interface IAIService
    {
        /// <summary>Returns AI reply text. Returns null if HUMAN_NEEDED.</summary>
        Task<AiReplyResult> GetReplyAsync(string systemPrompt, string conversationHistory, string userMessage, string apiKey, string model);
    }

    public class AiReplyResult
    {
        public bool NeedsHuman { get; set; }
        public string? Reply { get; set; }
        public string? ModelUsed { get; set; }
        public string? Error { get; set; }
    }
}