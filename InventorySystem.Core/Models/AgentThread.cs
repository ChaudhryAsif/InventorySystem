using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Core.Models
{
    public class AgentThread
    {
        public int Id { get; set; }

        [Required, MaxLength(20)]
        public string CustomerPhone { get; set; } = string.Empty;

        [MaxLength(100)]
        public string CustomerName { get; set; } = string.Empty;

        public int WhatsAppChannelId { get; set; }
        public WhatsAppChannel? WhatsAppChannel { get; set; }

        // ── Simple status: active | closed ───────────────────────────────────
        [MaxLength(20)]
        public string Status { get; set; } = "active";

        // ── AI Pause Toggle ───────────────────────────────────────────────────
        /// <summary>When true, AI will NOT auto-reply. Human must reply manually.</summary>
        public bool IsAiPaused { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
        public int UnreadCount { get; set; } = 0;

        public ICollection<AgentMessage> Messages { get; set; } = new List<AgentMessage>();
    }
}