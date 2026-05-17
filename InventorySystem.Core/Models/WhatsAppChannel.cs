using System;
using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Core.Models
{
    public class WhatsAppChannel
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string PhoneNumberId { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string WabaId { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string AccessToken { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string WebhookVerifyToken { get; set; } = string.Empty;

        [MaxLength(20)]
        public string DisplayPhoneNumber { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(100)]
        public string MetaAppId { get; set; } = string.Empty;

        [MaxLength(200)]
        public string MetaAppSecret { get; set; } = string.Empty;

        public bool IsWebhookRegistered { get; set; }
        public DateTime? WebhookRegisteredAt { get; set; }

        // AI Configuration
        [MaxLength(500)]
        public string AiApiKey { get; set; } = string.Empty;

        [MaxLength(100)]
        public string AiModel { get; set; } = "llama3-8b-8192";

        [MaxLength(1000)]
        public string AiSystemPrompt { get; set; } = "You are a helpful customer support assistant. Answer customer questions concisely and professionally. If you cannot confidently answer, respond with exactly: HUMAN_NEEDED";
    }
}