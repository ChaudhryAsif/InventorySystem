using System;
using System.Linq;
using System.Text;
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

        // ── Build live stock catalog for AI context ──────────────────────────────
        private async Task<string> BuildStockContextAsync()
        {
            var stockItems = await _db.Stock
                .AsNoTracking()
                .Include(s => s.Item)
                .Where(s => s.Item != null && s.Item.IsActive == true && s.Quantity > 0)
                .OrderBy(s => s.Item!.ItemName)
                .ToListAsync();

            if (!stockItems.Any())
                return "No products currently in stock.";

            var sb = new StringBuilder();
            sb.AppendLine("=== CURRENT PRODUCT CATALOG & STOCK ===");
            sb.AppendLine("(Use this data to answer availability questions accurately)");
            sb.AppendLine();

            foreach (var s in stockItems)
            {
                var item = s.Item!;
                var name = item.ProductName ?? item.ItemName ?? "Unknown";
                var price = item.SalePrice.HasValue ? $"PKR {item.SalePrice:N0}" : "Price on request";
                var size = !string.IsNullOrWhiteSpace(item.Size) ? $" | Size: {item.Size}" : "";
                var packing = !string.IsNullOrWhiteSpace(item.Packing) ? $" | Packing: {item.Packing}" : "";
                sb.AppendLine($"• {name} — Available: {s.Quantity:N0} units | Price: {price}{size}{packing}");
            }

            sb.AppendLine();
            sb.AppendLine("=== END OF CATALOG ===");
            return sb.ToString();
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

            // 2. Get or create ONE thread per phone number
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
                if (thread.Status == "closed")
                    thread.Status = "active";

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

            // 4. If AI is paused, wait for human
            if (thread.IsAiPaused) return;

            // 5. Build conversation history (last 10 messages)
            var history = thread.Messages
                .OrderBy(m => m.SentAt)
                .TakeLast(10)
                .Select(m => $"{(m.SenderType == "customer" ? "Customer" : "Agent")}: {m.MessageText}");
            var historyText = string.Join("\n", history);

            // 6. Build enriched system prompt with live stock data
            var stockContext = await BuildStockContextAsync();
            var enrichedPrompt = AiSystemPrompt + "\n\n" + stockContext;

            // 7. Ask AI
            var aiResult = await _ai.GetReplyAsync(
                enrichedPrompt,
                historyText,
                messageText,
                channel.AiApiKey,
                channel.AiModel);

            if (aiResult.NeedsHuman || string.IsNullOrWhiteSpace(aiResult.Reply))
            {
                bool wasAlreadyPaused = thread.IsAiPaused;
                thread.IsAiPaused = true;

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

                    var flagMsg = new AgentMessage
                    {
                        AgentThreadId = thread.Id,
                        Direction = "outbound",
                        SenderType = "ai",
                        MessageText = "[NEEDS_HUMAN]",
                        Status = "ai_paused",
                        SentAt = DateTime.UtcNow.AddMilliseconds(50),
                        IsRead = true,
                        AiModel = aiResult.ModelUsed
                    };
                    _db.AgentMessages.Add(flagMsg);
                }

                await _db.SaveChangesAsync();
                return;
            }

            // 8. Send AI reply
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

        private string AiSystemPrompt { get; set; } =
        @"You are a friendly and professional sales assistant for a POS (Point of Sale) business on WhatsApp.

        Your goal is to help customers place orders by following these steps IN ORDER:

        STEP 1 — GREETING & NAME
        - Greet the customer warmly.
        - If they have NOT mentioned their name, politely ask: ""May I know your name please? 😊""

        STEP 2 — LOCATION
        - Once you have their name, ask for their city/location:
          ""Thank you, [Name]! Which city are you ordering from?""

        STEP 3 — PRODUCT INQUIRY
        - Ask what they would like to purchase:
          ""Great! What product are you looking for today?""
        - If they mention a product, check it against the CURRENT PRODUCT CATALOG provided below.
        - If found: confirm availability and price.
        - If NOT found: say it's currently unavailable and suggest similar items from the catalog.

        STEP 4 — QUANTITY
        - Ask: ""How many units would you like to order?""
        - Check if that quantity is available in stock from the catalog.
        - If available: confirm the order details (product, quantity, price, location).
        - If NOT enough stock: politely inform them of available quantity and ask if they want to proceed with what's available.

        STEP 5 — ORDER SUMMARY
        - Summarize the order:
          ""✅ Order Summary:
          - Name: [Name]
          - Location: [City]
          - Product: [Product]
          - Quantity: [Qty]
          - Total: PKR [Amount]
  
          Shall I confirm this order? Our team will contact you shortly for delivery details.""

        IMPORTANT RULES:
        - Always be polite, warm, and professional.
        - Only answer questions about products, orders, pricing, and availability.
        - If asked about anything unrelated to the business, politely redirect.
        - If you cannot answer confidently (e.g., custom pricing, special requests, complaints), respond with exactly: HUMAN_NEEDED
        - Never make up stock quantities or prices — only use the catalog data provided.
        - Keep replies concise and use emojis occasionally to be friendly. 🛍️";
    }
}