using System;
using System.Linq;
using System.Threading.Tasks;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Core.Services
{
    public class ChatbotService : IChatbotService
    {
        private readonly ApplicationDbContext _db;
        private readonly IWhatsAppService _whatsApp;
        private readonly IAIService _ai;

        public ChatbotService(ApplicationDbContext db, IWhatsAppService whatsApp, IAIService ai)
        {
            _db = db;
            _whatsApp = whatsApp;
            _ai = ai;
        }

        public async Task ProcessInboundMessageAsync(
            string channelPhoneNumberId,
            string fromPhone,
            string customerName,
            string messageText,
            string whatsappMessageId)
        {
            // 1. Find channel
            var channel = await _db.WhatsAppChannels
                .FirstOrDefaultAsync(c => c.PhoneNumberId == channelPhoneNumberId && c.IsActive);
            if (channel == null) return;

            // 2. Always get or create ONE thread per phone number (never create duplicates)
            var thread = await _db.AgentThreads
                .Include(t => t.Messages)
                .FirstOrDefaultAsync(t =>
                    t.CustomerPhone == fromPhone &&
                    t.WhatsAppChannelId == channel.Id);

            if (thread == null)
            {
                thread = new AgentThread
                {
                    CustomerPhone = fromPhone,
                    CustomerName = customerName,
                    WhatsAppChannelId = channel.Id,
                    Status = "active",
                    IsAiPaused = false,
                    CreatedAt = DateTime.UtcNow,
                    LastMessageAt = DateTime.UtcNow
                };
                _db.AgentThreads.Add(thread);
                await _db.SaveChangesAsync();
            }
            else
            {
                // Re-open closed thread when customer writes again
                if (thread.Status == "closed")
                    thread.Status = "active";

                // Update customer name if changed
                if (!string.IsNullOrWhiteSpace(customerName) && customerName != fromPhone)
                    thread.CustomerName = customerName;
            }

            // 3. Save inbound message
            var inboundMsg = new AgentMessage
            {
                AgentThreadId = thread.Id,
                Direction = "inbound",
                SenderType = "customer",
                MessageText = messageText,
                Status = "received",
                WhatsAppMessageId = whatsappMessageId,
                SentAt = DateTime.UtcNow,
                IsRead = false
            };
            _db.AgentMessages.Add(inboundMsg);
            thread.LastMessageAt = DateTime.UtcNow;
            thread.UnreadCount++;
            await _db.SaveChangesAsync();

            // 4. If AI is paused, don't auto-reply — wait for human
            if (thread.IsAiPaused) return;

            // 5. Build conversation history (last 10 messages for context)
            var history = thread.Messages
                .OrderBy(m => m.SentAt)
                .TakeLast(10)
                .Select(m => $"{(m.SenderType == "customer" ? "Customer" : "Agent")}: {m.MessageText}");
            var historyText = string.Join("\n", history);

            // 6. Ask AI
            var aiResult = await _ai.GetReplyAsync(
                channel.AiSystemPrompt,
                historyText,
                messageText,
                channel.AiApiKey,
                channel.AiModel);

            if (aiResult.NeedsHuman || string.IsNullOrWhiteSpace(aiResult.Reply))
            {
                bool wasAlreadyPaused = thread.IsAiPaused;
                thread.IsAiPaused = true;

                // ── 1. Send & save actual "Thank you" message FIRST (only once) ───
                if (!wasAlreadyPaused)
                {
                    var thankYouText = "Thank you for reaching out! A human agent will be with you shortly. 🙏";

                    var sendResult1 = await _whatsApp.SendTextMessageAsync(
                        channel.PhoneNumberId,
                        channel.AccessToken,
                        fromPhone,
                        thankYouText);

                    var thankYouMsg = new AgentMessage
                    {
                        AgentThreadId = thread.Id,
                        Direction = "outbound",
                        SenderType = "ai",
                        MessageText = thankYouText,
                        Status = sendResult1.Success ? "sent" : "failed",
                        WhatsAppMessageId = sendResult1.MessageId,
                        SentAt = DateTime.UtcNow,
                        IsRead = true,
                        AiModel = aiResult.ModelUsed
                    };
                    _db.AgentMessages.Add(thankYouMsg);

                    // ── 2. Save internal [NEEDS_HUMAN] flag AFTER "Thank you" ─────
                    var flagMsg = new AgentMessage
                    {
                        AgentThreadId = thread.Id,
                        Direction = "outbound",
                        SenderType = "ai",
                        MessageText = "[NEEDS_HUMAN]",
                        Status = "ai_paused",
                        SentAt = DateTime.UtcNow.AddMilliseconds(50), // after Thank you
                        IsRead = true,
                        AiModel = aiResult.ModelUsed
                    };
                    _db.AgentMessages.Add(flagMsg);
                }

                await _db.SaveChangesAsync();
                return;
            }

            // 7. Send AI reply
            var sendResult = await _whatsApp.SendTextMessageAsync(
                channel.PhoneNumberId,
                channel.AccessToken,
                fromPhone,
                aiResult.Reply);

            var aiMessage = new AgentMessage
            {
                AgentThreadId = thread.Id,
                Direction = "outbound",
                SenderType = "ai",
                MessageText = aiResult.Reply,
                Status = sendResult.Success ? "sent" : "failed",
                WhatsAppMessageId = sendResult.MessageId,
                SentAt = DateTime.UtcNow,
                AiModel = aiResult.ModelUsed
            };
            _db.AgentMessages.Add(aiMessage);
            await _db.SaveChangesAsync();
        }

        public async Task<bool> HumanReplyAsync(int threadId, string replyText)
        {
            var thread = await _db.AgentThreads
                .Include(t => t.WhatsAppChannel)
                .FirstOrDefaultAsync(t => t.Id == threadId);

            if (thread?.WhatsAppChannel == null) return false;

            // Auto-pause AI when human replies
            thread.IsAiPaused = true;

            var sendResult = await _whatsApp.SendTextMessageAsync(
                thread.WhatsAppChannel.PhoneNumberId,
                thread.WhatsAppChannel.AccessToken,
                thread.CustomerPhone,
                replyText);

            var msg = new AgentMessage
            {
                AgentThreadId = thread.Id,
                Direction = "outbound",
                SenderType = "human",
                MessageText = replyText,
                Status = sendResult.Success ? "sent" : "failed",
                WhatsAppMessageId = sendResult.MessageId,
                SentAt = DateTime.UtcNow,
                IsRead = true
            };
            _db.AgentMessages.Add(msg);
            thread.LastMessageAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return sendResult.Success;
        }

        public async Task<bool> CloseThreadAsync(int threadId)
        {
            var thread = await _db.AgentThreads.FindAsync(threadId);
            if (thread == null) return false;

            thread.Status = "closed";
            thread.ResolvedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }
    }
}