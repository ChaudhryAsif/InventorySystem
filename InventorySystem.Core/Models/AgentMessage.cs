using System;
using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Core.Models
{
    public class AgentMessage
    {
        public int Id { get; set; }

        public int AgentThreadId { get; set; }
        public AgentThread? AgentThread { get; set; }

        // Direction: inbound | outbound
        [MaxLength(10)]
        public string Direction { get; set; } = "inbound";

        // SenderType: customer | ai | human
        [MaxLength(10)]
        public string SenderType { get; set; } = "customer";

        [Required]
        public string MessageText { get; set; } = string.Empty;

        // Status: sent | delivered | read | failed | human_needed
        [MaxLength(20)]
        public string Status { get; set; } = "sent";

        [MaxLength(100)]
        public string? WhatsAppMessageId { get; set; }

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public bool IsRead { get; set; }
        public string? AiModel { get; set; }
        public decimal? AiConfidence { get; set; }
    }
}