using System.Threading.Tasks;

namespace InventorySystem.Core.Services
{
    public interface IAIService
    {
        /// <summary>Returns AI reply text. Returns NeedsHuman=true if HUMAN_NEEDED.</summary>
        Task<AiReplyResult> GetReplyAsync(string systemPrompt, string conversationHistory,
            string userMessage, string apiKey, string model);

        /// <summary>Extracts structured order data from conversation history as JSON.</summary>
        Task<OrderExtractResult?> ExtractOrderAsync(string conversationHistory,
            string apiKey, string model);
    }

    public class AiReplyResult
    {
        public bool NeedsHuman { get; set; }
        public string? Reply { get; set; }
        public string? ModelUsed { get; set; }
        public string? Error { get; set; }
    }

    public class OrderExtractResult
    {
        public string? CustomerName { get; set; }
        public string? City { get; set; }
        public string? ProductName { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalAmount { get; set; }
    }
}