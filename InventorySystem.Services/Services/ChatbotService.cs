using System;
using System.Linq;
using System.Text.RegularExpressions;
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

        private const string BaseSystemPrompt =
            @"You are a friendly sales assistant for a POS business on WhatsApp.

Your ONLY job is to collect this information from the customer (ask only what is MISSING):
1. Customer Name  
2. City / Location
3. Product they want to buy
4. Quantity

RULES:
- Greet warmly on first message.
- If customer already gave name and/or city, do NOT ask again — use what they gave.
- Ask only for MISSING info, one at a time.
- When customer mentions a product name, reply with ONLY: ASK_PRODUCT_CHECK:[product name]
  Example: customer says 'CMP' → reply: ASK_PRODUCT_CHECK:CMP
- When customer gives a number as quantity, reply with ONLY: ASK_QTY_CHECK:[number]
  Example: customer says '50' or '50 units' → reply: ASK_QTY_CHECK:50
- Do NOT reply ASK_QTY_CHECK for 'yes', 'no', or city confirmations.
- For complaints or anything outside orders: reply exactly: HUMAN_NEEDED
- Keep replies short and friendly. Use emojis occasionally. 🛍️";

        public ChatbotService(ApplicationDbContext db, IWhatsAppService whatsApp, IAIService ai)
        {
            _db = db;
            _whatsApp = whatsApp;
            _ai = ai;
        }

        // ── DB: verify product, return human-readable reply ───────────────────
        private async Task<string> CheckProductInDbAsync(string productName)
        {
            var allItems = await _db.Items.AsNoTracking()
                .Where(i => i.IsActive == true).ToListAsync();

            var matched = allItems.FirstOrDefault(i =>
                (i.ProductName != null && i.ProductName.ToLower().Contains(productName.ToLower())) ||
                (i.ItemName != null && i.ItemName.ToLower().Contains(productName.ToLower())));

            if (matched == null)
            {
                var names = allItems
                    .Select(i => $"• {i.ProductName ?? i.ItemName}")
                    .Where(n => n != "• ")
                    .ToList();
                return $"Sorry, we don't carry *{productName}*. 😔\n\n" +
                       (names.Any()
                           ? "Here's what we currently have:\n" + string.Join("\n", names) +
                             "\n\nWould you like to order any of these? 🛍️"
                           : "We currently have no products in stock.");
            }

            var stock = await _db.Stock.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ItemId == matched.ItemID);

            var displayName = matched.ProductName ?? matched.ItemName;
            var qty = stock?.Quantity ?? 0;

            if (qty <= 0)
                return $"Sorry, *{displayName}* is currently *out of stock*. 😔\n" +
                       "Let me know if you need something else.";

            var price = matched.SalePrice.HasValue ? $"PKR {matched.SalePrice:N0} per unit" : "price on request";
            var size = !string.IsNullOrWhiteSpace(matched.Size) ? $"\n📐 Size: {matched.Size}" : "";
            var packing = !string.IsNullOrWhiteSpace(matched.Packing) ? $"\n📦 Packing: {matched.Packing}" : "";

            return $"Great news! ✅ *{displayName}* is available!\n" +
                   $"📦 Stock: *{qty:N0} units*\n" +
                   $"💰 Price: *{price}*{size}{packing}\n\n" +
                   $"How many units would you like to order? 🛍️";
        }

        // ── DB: verify quantity, return order summary or error ────────────────
        private async Task<string> CheckQuantityInDbAsync(
            string productName, decimal requestedQty, AgentThread thread)
        {
            var item = await _db.Items.AsNoTracking()
                .FirstOrDefaultAsync(i =>
                    i.IsActive == true &&
                    (i.ProductName != null && i.ProductName.ToLower().Contains(productName.ToLower()) ||
                     i.ItemName != null && i.ItemName.ToLower().Contains(productName.ToLower())));

            if (item == null)
                return "Sorry, I couldn't find that product. Could you confirm the product name?";

            var stock = await _db.Stock.AsNoTracking().FirstOrDefaultAsync(s => s.ItemId == item.ItemID);
            var available = stock?.Quantity ?? 0;
            var displayName = item.ProductName ?? item.ItemName;
            var price = item.SalePrice ?? 0;

            if (requestedQty > available)
                return $"Sorry, we only have *{available:N0} units* of *{displayName}* available. 😔\n" +
                       $"Would you like to proceed with {available:N0} units instead?";

            var total = price * requestedQty;
            return $"✅ *Order Summary:*\n" +
                   $"👤 Name: {thread.CustomerName}\n" +
                   $"📦 Product: {displayName}\n" +
                   $"🔢 Quantity: {requestedQty:N0} units\n" +
                   $"💰 Price: PKR {price:N0} per unit\n" +
                   $"💵 Total: PKR {total:N0}\n\n" +
                   $"Reply *YES* to confirm your order or *NO* to cancel.";
        }

        // ── Create SaleInvoice in DB ──────────────────────────────────────────
        private async Task<int?> CreateOrderAsync(OrderExtractResult order, AgentThread thread)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(order.ProductName) || order.Quantity <= 0) return null;

                var item = await _db.Items.AsNoTracking()
                    .FirstOrDefaultAsync(i =>
                        i.IsActive == true &&
                        (i.ProductName != null && i.ProductName.ToLower().Contains(order.ProductName.ToLower()) ||
                         i.ItemName != null && i.ItemName.ToLower().Contains(order.ProductName.ToLower())));
                if (item == null) return null;

                var stock = await _db.Stock.FirstOrDefaultAsync(s => s.ItemId == item.ItemID);
                if (stock == null || stock.Quantity < order.Quantity) return null;

                var unitPrice = order.UnitPrice > 0 ? order.UnitPrice : item.SalePrice ?? 0;
                var total = unitPrice * order.Quantity;

                var party = await _db.Parties.FirstOrDefaultAsync(p =>
                    p.Mobile == thread.CustomerPhone || p.Phone == thread.CustomerPhone);

                if (party == null)
                {
                    party = new Party
                    {
                        PartyCode = $"WA-{thread.CustomerPhone[^6..]}",
                        PartyName = string.IsNullOrWhiteSpace(order.CustomerName)
                                    ? thread.CustomerName : order.CustomerName,
                        PartyType = "customer",
                        Mobile = thread.CustomerPhone,
                        City = order.City,
                        Status = "active"
                    };
                    _db.Parties.Add(party);
                    await _db.SaveChangesAsync();
                }

                var invoice = new SaleInvoice
                {
                    SaleDate = DateTime.Now,
                    CustomerID = party.PartyId.ToString(),
                    BranchID = 1,
                    PaymentMode = 1,
                    TotalAmount = total,
                    NetAmount = total,
                    Remarks = $"WhatsApp Order — {order.City} — Auto by AI",
                    UserNo = 1
                };
                _db.SaleInvoice.Add(invoice);
                await _db.SaveChangesAsync();

                _db.SaleInvoiceBody.Add(new SaleInvoiceBody
                {
                    SaleId = invoice.SaleId,
                    ItemId = item.ItemID,
                    Descr = item.ProductName ?? item.ItemName,
                    Quantity = order.Quantity,
                    SalePrice = unitPrice,
                    DiscPer = 0,
                    DiscAmt = 0,
                    Total = total
                });

                stock.Quantity -= order.Quantity;
                stock.LastUpdated = DateTime.Now;
                thread.ConfirmedSaleId = invoice.SaleId;
                await _db.SaveChangesAsync();
                return invoice.SaleId;
            }
            catch { return null; }
        }

        public async Task ProcessInboundMessageAsync(
            string channelPhoneNumberId, string fromPhone,
            string customerName, string messageText, string whatsappMessageId)
        {
            // 1. Find channel
            var channel = await _db.WhatsAppChannels
                .FirstOrDefaultAsync(c => c.PhoneNumberId == channelPhoneNumberId && c.IsActive);
            if (channel == null) return;

            // 2. Get or create thread
            var thread = await _db.AgentThreads
                .Include(t => t.Messages)
                .FirstOrDefaultAsync(t =>
                    t.CustomerPhone == fromPhone && t.WhatsAppChannelId == channel.Id);

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
                if (thread.Status == "closed") thread.Status = "active";
                if (!string.IsNullOrWhiteSpace(customerName) && customerName != fromPhone)
                    thread.CustomerName = customerName;
            }

            // 3. Save inbound
            _db.AgentMessages.Add(new AgentMessage
            {
                AgentThreadId = thread.Id,
                Direction = "inbound",
                SenderType = "customer",
                MessageText = messageText,
                Status = "received",
                WhatsAppMessageId = whatsappMessageId,
                SentAt = DateTime.UtcNow,
                IsRead = false
            });
            thread.LastMessageAt = DateTime.UtcNow;
            thread.UnreadCount++;
            await _db.SaveChangesAsync();

            if (thread.IsAiPaused) return;

            // ── INTERCEPT 1: YES/NO — ONLY if last AI message was Order Summary ─
            var msgTrimmed = messageText.Trim().ToLower();
            var isYes = msgTrimmed is "yes" or "confirm" or "ok" or "okay" or "ji" or "haan" or "y" or "proceed";
            var isNo = msgTrimmed is "no" or "cancel" or "nahi" or "nope" or "n";

            if (isYes || isNo)
            {
                // ✅ KEY FIX: Only intercept if the VERY LAST outbound AI message is an Order Summary
                var lastAiMsg = thread.Messages
                    .Where(m => m.SenderType == "ai" && m.Status != "ai_paused")
                    .OrderByDescending(m => m.SentAt)
                    .FirstOrDefault();

                bool lastMsgWasSummary = lastAiMsg != null &&
                    lastAiMsg.MessageText.Contains("Order Summary", StringComparison.OrdinalIgnoreCase);

                if (lastMsgWasSummary)
                {
                    if (isNo)
                    {
                        await SendAndSaveAsync(thread, channel, fromPhone,
                            "No problem! 😊 Would you like to order a different product or quantity?",
                            "ai", null);
                        return;
                    }

                    // YES → extract and create order
                    var fullHistory = thread.Messages
                        .Where(m => m.Status != "ai_paused")
                        .OrderBy(m => m.SentAt)
                        .Select(m => $"{(m.SenderType == "customer" ? "Customer" : "Agent")}: {m.MessageText}");

                    var extracted = await _ai.ExtractOrderAsync(
                        string.Join("\n", fullHistory), channel.AiApiKey, channel.AiModel);

                    if (extracted != null && extracted.Quantity > 0)
                    {
                        var saleId = await CreateOrderAsync(extracted, thread);

                        var confirmMsg = saleId.HasValue
                            ? $"🎉 *Order Confirmed!*\n" +
                              $"📋 Invoice: *INV-{saleId.Value:D5}*\n" +
                              $"📦 Product: {extracted.ProductName}\n" +
                              $"🔢 Quantity: {extracted.Quantity:N0} units\n" +
                              $"💰 Total: PKR {extracted.TotalAmount:N0}\n" +
                              $"📍 City: {extracted.City}\n\n" +
                              $"Our team will contact you shortly for delivery. Thank you! 🙏"
                            : "Thank you! ✅ Our team will review and contact you shortly. 🙏";

                        if (!saleId.HasValue)
                        {
                            thread.IsAiPaused = true;
                            _db.AgentMessages.Add(new AgentMessage
                            {
                                AgentThreadId = thread.Id,
                                Direction = "outbound",
                                SenderType = "ai",
                                MessageText = "[NEEDS_HUMAN] Order auto-create failed.",
                                Status = "ai_paused",
                                SentAt = DateTime.UtcNow.AddMilliseconds(50),
                                IsRead = true
                            });
                        }

                        await SendAndSaveAsync(thread, channel, fromPhone, confirmMsg, "ai", null);
                        await _db.SaveChangesAsync();
                    }
                    else
                    {
                        // Extraction failed — ask AI to handle
                        await SendAndSaveAsync(thread, channel, fromPhone,
                            "Sorry, I had trouble processing your order. Could you please tell me the product and quantity again?",
                            "ai", null);
                    }
                    return;
                }
                // else: "yes/no" is NOT an order confirmation → fall through to AI
            }

            // ── INTERCEPT 2: Quantity number — only if last AI msg was availability ─
            var lastAvailMsg = thread.Messages
                .Where(m => m.SenderType == "ai" && m.Status != "ai_paused" &&
                            m.MessageText.Contains("is available", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefault();

            // Check if last AI message was the availability message (not something after it)
            var lastAiMsgForQty = thread.Messages
                .Where(m => m.SenderType == "ai" && m.Status != "ai_paused")
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefault();

            bool lastMsgWasAvailability = lastAvailMsg != null &&
                lastAiMsgForQty?.Id == lastAvailMsg.Id;

            if (lastMsgWasAvailability)
            {
                var qtyMatch = Regex.Match(messageText, @"\b(\d+)\b");
                if (qtyMatch.Success && decimal.TryParse(qtyMatch.Groups[1].Value, out var qty) && qty > 0)
                {
                    var productName = ExtractProductNameFromAvailMsg(lastAvailMsg!.MessageText);
                    if (!string.IsNullOrWhiteSpace(productName))
                    {
                        var qtyReply = await CheckQuantityInDbAsync(productName, qty, thread);
                        await SendAndSaveAsync(thread, channel, fromPhone, qtyReply, "ai", null);
                        return;
                    }
                }
            }

            // 4. Conversation history for AI
            var history = thread.Messages
                .Where(m => m.Status != "ai_paused")
                .OrderBy(m => m.SentAt)
                .TakeLast(12)
                .Select(m => $"{(m.SenderType == "customer" ? "Customer" : "Agent")}: {m.MessageText}");
            var historyText = string.Join("\n", history);

            var systemPrompt = string.IsNullOrWhiteSpace(channel.AiSystemPrompt)
                ? BaseSystemPrompt : channel.AiSystemPrompt;

            // 5. Ask AI
            var aiResult = await _ai.GetReplyAsync(
                systemPrompt, historyText, messageText, channel.AiApiKey, channel.AiModel);

            if (aiResult.NeedsHuman || string.IsNullOrWhiteSpace(aiResult.Reply))
            {
                bool wasAlreadyPaused = thread.IsAiPaused;
                thread.IsAiPaused = true;
                await _db.SaveChangesAsync();

                if (!wasAlreadyPaused)
                {
                    await SendAndSaveAsync(thread, channel, fromPhone,
                        "Thank you for reaching out! A human agent will be with you shortly. 🙏",
                        "ai", aiResult.ModelUsed);
                    _db.AgentMessages.Add(new AgentMessage
                    {
                        AgentThreadId = thread.Id,
                        Direction = "outbound",
                        SenderType = "ai",
                        MessageText = "[NEEDS_HUMAN]",
                        Status = "ai_paused",
                        SentAt = DateTime.UtcNow.AddMilliseconds(50),
                        IsRead = true,
                        AiModel = aiResult.ModelUsed
                    });
                    await _db.SaveChangesAsync();
                }
                return;
            }

            var aiReply = aiResult.Reply.Trim();

            // ── INTERCEPT 3: AI signals product check ─────────────────────────
            var productMatch = Regex.Match(aiReply,
                @"ASK_PRODUCT_CHECK[:\-]\s*(.+)", RegexOptions.IgnoreCase);
            if (productMatch.Success)
            {
                var productName = productMatch.Groups[1].Value.Trim();
                var dbReply = await CheckProductInDbAsync(productName);
                await SendAndSaveAsync(thread, channel, fromPhone, dbReply, "ai", aiResult.ModelUsed);
                return;
            }

            // ── INTERCEPT 4: AI signals quantity check ────────────────────────
            var qtySignalMatch = Regex.Match(aiReply,
                @"ASK_QTY_CHECK[:\-]\s*(\d+[\.,]?\d*)", RegexOptions.IgnoreCase);
            if (qtySignalMatch.Success &&
                decimal.TryParse(qtySignalMatch.Groups[1].Value.Replace(",", ""), out var signalQty))
            {
                var availMsg = thread.Messages
                    .Where(m => m.SenderType == "ai" &&
                                m.MessageText.Contains("is available", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(m => m.SentAt)
                    .FirstOrDefault();

                var productForQty = ExtractProductNameFromAvailMsg(availMsg?.MessageText ?? "");
                if (!string.IsNullOrWhiteSpace(productForQty))
                {
                    var qtyReply = await CheckQuantityInDbAsync(productForQty, signalQty, thread);
                    await SendAndSaveAsync(thread, channel, fromPhone, qtyReply, "ai", aiResult.ModelUsed);
                    return;
                }
            }

            // 6. Normal AI reply (greeting, asking name/city etc.)
            if (!aiReply.Contains("ASK_PRODUCT_CHECK", StringComparison.OrdinalIgnoreCase) &&
                !aiReply.Contains("ASK_QTY_CHECK", StringComparison.OrdinalIgnoreCase))
            {
                await SendAndSaveAsync(thread, channel, fromPhone, aiReply, "ai", aiResult.ModelUsed);
            }
        }

        private static string ExtractProductNameFromAvailMsg(string msg)
        {
            // Matches: *ProductName* is available
            var m = Regex.Match(msg, @"\*(.+?)\*\s+is available", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();
            m = Regex.Match(msg, @"(.+?)\s+is available", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
        }

        private async Task SendAndSaveAsync(AgentThread thread, WhatsAppChannel channel,
            string toPhone, string text, string senderType, string? aiModel = null)
        {
            var result = await _whatsApp.SendTextMessageAsync(
                channel.PhoneNumberId, channel.AccessToken, toPhone, text);
            _db.AgentMessages.Add(new AgentMessage
            {
                AgentThreadId = thread.Id,
                Direction = "outbound",
                SenderType = senderType,
                MessageText = text,
                Status = result.Success ? "sent" : "failed",
                WhatsAppMessageId = result.MessageId,
                SentAt = DateTime.UtcNow,
                IsRead = true,
                AiModel = aiModel
            });
            thread.LastMessageAt = DateTime.UtcNow;
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
                thread.CustomerPhone, replyText);

            _db.AgentMessages.Add(new AgentMessage
            {
                AgentThreadId = thread.Id,
                Direction = "outbound",
                SenderType = "human",
                MessageText = replyText,
                Status = sendResult.Success ? "sent" : "failed",
                WhatsAppMessageId = sendResult.MessageId,
                SentAt = DateTime.UtcNow,
                IsRead = true
            });
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