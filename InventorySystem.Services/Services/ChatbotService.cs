using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Services.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace InventorySystem.Core.Services
{
    // Drives the WhatsApp sales conversation:
    //   inbound message → AI/intercept → DB lookup → outbound text or voice reply
    // Most of the work here is figuring out which "stage" of the order the customer is in
    // (product check, quantity check, final confirmation) and short-circuiting the AI
    // when we already know what to do.
    public class ChatbotService : IChatbotService
    {
        private readonly ApplicationDbContext _db;
        private readonly IWhatsAppService _whatsApp;
        private readonly IAIService _ai;
        private readonly ISpeechService _speech;

        // Sentinel tokens the AI emits when it wants us to take over with a DB lookup
        // instead of generating a free-form reply. Keep these in sync with BaseSystemPrompt.
        private const string ProductCheckSignal = "ASK_PRODUCT_CHECK";
        private const string QtyCheckSignal = "ASK_QTY_CHECK";

        // Markers we use to detect what stage the conversation is in by scanning past AI messages.
        // Changing these strings means also fixing the prompt and the matching regex below.
        private const string AvailabilityMarker = "is available";
        private const string OrderSummaryMarker = "Order Summary";

        // Yes / No vocabulary we accept as order confirmation.
        private static readonly HashSet<string> YesWords =
            new(StringComparer.OrdinalIgnoreCase) { "yes", "confirm", "ok", "okay", "y", "proceed" };

        private static readonly HashSet<string> NoWords =
            new(StringComparer.OrdinalIgnoreCase) { "no", "cancel", "nope", "n" };

        // System prompt for the AI. Kept deliberately short — the heavy lifting (stock check,
        // pricing, order creation) happens in C# code, the AI only handles the dialogue flow.
        private const string BaseSystemPrompt =
@"You are a friendly sales assistant for an online store on WhatsApp.
Reply in English only.

Your ONLY job is to collect this info step by step (ask only what is MISSING):
1. Customer Name
2. City / Location
3. Product they want to buy
4. Quantity

RULES:
- Greet warmly on first message, e.g. ""Hi! 😊 How can I help you find what you need today?""
- Never mention internal systems or use the word ""POS"" with the customer.
- Never ask for info already provided.
- Ask only ONE missing thing at a time.
- When customer mentions a product name, reply with ONLY: ASK_PRODUCT_CHECK:[product name]
- When customer gives a number as quantity, reply with ONLY: ASK_QTY_CHECK:[number]
- Do NOT reply ASK_QTY_CHECK for 'yes', 'no' or city replies.
- For complaints or anything outside orders: reply exactly: HUMAN_NEEDED
- Keep replies short and friendly.";

        public ChatbotService(
            ApplicationDbContext db,
            IWhatsAppService whatsApp,
            IAIService ai,
            ISpeechService speech)
        {
            _db = db;
            _whatsApp = whatsApp;
            _ai = ai;
            _speech = speech;
        }

        // ── Public entry points ──────────────────────────────────────────────

        // Main pipeline for any inbound message (text or transcribed voice).
        // Flow: load/create thread → save inbound → run intercepts → fall back to AI → send reply.
        public async Task ProcessInboundMessageAsync(
            string channelPhoneNumberId, string fromPhone,
            string customerName, string messageText,
            string whatsappMessageId, bool wasVoiceInput = false)
        {
            var channel = await _db.WhatsAppChannels
                .FirstOrDefaultAsync(c => c.PhoneNumberId == channelPhoneNumberId && c.IsActive);
            if (channel == null) return;

            var thread = await GetOrCreateThreadAsync(channel.Id, fromPhone, customerName);

            await SaveInboundMessageAsync(thread, messageText, whatsappMessageId, wasVoiceInput);

            // If a human took over this thread, the AI stays silent.
            if (thread.IsAiPaused) return;

            // Stage 1: customer is confirming/cancelling an order summary we just sent.
            if (await TryHandleYesNoAsync(thread, channel, fromPhone, messageText))
                return;

            // Stage 2: customer is replying to an availability message with a quantity.
            if (await TryHandleQuantityReplyAsync(thread, channel, fromPhone, messageText))
                return;

            // Stage 3: nothing matched — let the AI drive the conversation.
            await HandleAiReplyAsync(thread, channel, fromPhone, messageText);
        }

        // Voice messages go through STT first, then re-enter the normal text pipeline.
        public async Task ProcessVoiceMessageAsync(
            string channelPhoneNumberId, string fromPhone,
            string customerName, string audioId, string whatsappMessageId)
        {
            var channel = await _db.WhatsAppChannels
                .FirstOrDefaultAsync(c => c.PhoneNumberId == channelPhoneNumberId && c.IsActive);
            if (channel == null) return;

            var audioStream = await _whatsApp.DownloadMediaAsync(audioId, channel.AccessToken);
            if (audioStream == null)
            {
                await _whatsApp.SendTextMessageAsync(
                    channel.PhoneNumberId, channel.AccessToken, fromPhone,
                    "Sorry, I couldn't hear your voice message. Could you please type your message instead?");
                return;
            }

            var transcribedText = await _speech.SpeechToTextAsync(audioStream);
            if (string.IsNullOrWhiteSpace(transcribedText))
            {
                await _whatsApp.SendTextMessageAsync(
                    channel.PhoneNumberId, channel.AccessToken, fromPhone,
                    "Sorry, I couldn't understand your voice message. Could you please try again or type your message?");
                return;
            }

            // Flag the thread so our reply also goes out as voice (the SendAndSave helper checks this).
            var thread = await _db.AgentThreads
                .FirstOrDefaultAsync(t => t.CustomerPhone == fromPhone && t.WhatsAppChannelId == channel.Id);
            if (thread != null)
            {
                thread.LastInboundWasVoice = true;
                await _db.SaveChangesAsync();
            }

            await ProcessInboundMessageAsync(
                channelPhoneNumberId, fromPhone, customerName,
                transcribedText, whatsappMessageId, wasVoiceInput: true);
        }

        // ── PLAYGROUND SIMULATION ────────────────────────────────────────────
        // Mirrors ProcessInboundMessageAsync exactly (same three-stage pipeline, same prompt,
        // same DB lookups) but runs against an in-memory thread and RETURNS the reply text
        // instead of sending it. Nothing is persisted: no thread, no message rows, no sale.
        // Product/stock lookups are real (read-only); the YES path shows a *simulated* invoice.
        public async Task<string> SimulatePlaygroundReplyAsync(
            List<PlaygroundTurn> history, string userMessage,
            string systemPrompt, string model, string apiKey, string customerName)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return "Please type a message.";

            var thread = BuildInMemoryThread(history, userMessage, customerName);
            var effectivePrompt = string.IsNullOrWhiteSpace(systemPrompt) ? BaseSystemPrompt : systemPrompt;

            // Stage 1: customer confirming/cancelling an order summary.
            var yesNo = await SimulateYesNoAsync(thread, userMessage, apiKey, model);
            if (yesNo != null) return yesNo;

            // Stage 2: customer replying to an availability message with a quantity.
            var qty = await SimulateQuantityReplyAsync(thread, userMessage);
            if (qty != null) return qty;

            // Stage 3: let the AI drive (+ sentinel-triggered DB lookups).
            return await SimulateAiReplyAsync(thread, userMessage, effectivePrompt, model, apiKey);
        }

        // Reconstruct a transient thread from the posted transcript. Mirrors the production
        // ordering where the current inbound is appended before the stage handlers run.
        private static AgentThread BuildInMemoryThread(
            List<PlaygroundTurn> history, string currentMessage, string customerName)
        {
            var thread = new AgentThread
            {
                Id = 0,
                CustomerName = string.IsNullOrWhiteSpace(customerName) ? "Playground User" : customerName,
                CustomerPhone = "playground",
                Messages = new List<AgentMessage>()
            };

            var stamp = DateTime.UtcNow.AddMinutes(-((history?.Count ?? 0) + 1));

            if (history != null)
            {
                foreach (var turn in history)
                {
                    var isCustomer = string.Equals(turn.Sender, "customer", StringComparison.OrdinalIgnoreCase);
                    thread.Messages.Add(new AgentMessage
                    {
                        SenderType = isCustomer ? "customer" : "ai",
                        Direction = isCustomer ? "inbound" : "outbound",
                        MessageText = turn.Text ?? "",
                        Status = "sent",
                        SentAt = stamp,
                        IsRead = true
                    });
                    stamp = stamp.AddSeconds(30);
                }
            }

            thread.Messages.Add(new AgentMessage
            {
                SenderType = "customer",
                Direction = "inbound",
                MessageText = currentMessage,
                Status = "received",
                SentAt = stamp,
                IsRead = false
            });

            return thread;
        }

        // Mirror of TryHandleYesNoAsync — returns the reply, or null if this stage doesn't apply.
        // The YES branch extracts the order (read-only) but simulates the confirmation; it never
        // writes an invoice or decrements stock.
        private async Task<string?> SimulateYesNoAsync(
            AgentThread thread, string messageText, string apiKey, string model)
        {
            var trimmed = messageText.Trim();
            var isYes = YesWords.Contains(trimmed);
            var isNo = NoWords.Contains(trimmed);
            if (!isYes && !isNo) return null;

            var lastAiMsg = GetLastAiMessage(thread);
            var lastWasSummary = lastAiMsg?.MessageText
                .Contains(OrderSummaryMarker, StringComparison.OrdinalIgnoreCase) == true;
            if (!lastWasSummary) return null;

            if (isNo)
                return "No problem! Would you like to order a different product or quantity?";

            var history = BuildConversationHistory(thread);
            var extracted = await _ai.ExtractOrderAsync(history, apiKey, model);

            if (extracted == null || extracted.Quantity <= 0)
                return "Sorry, I had trouble processing your order. Could you please tell me the product and quantity again?";

            return
                $"🎉 *Order Confirmed!*\n" +
                $"📋 Invoice: *INV-XXXXX*  _(simulation — no invoice created)_\n" +
                $"📦 Product: {extracted.ProductName}\n" +
                $"🔢 Quantity: {extracted.Quantity:N0} units\n" +
                $"💰 Total: PKR {extracted.TotalAmount:N0}\n" +
                $"📍 City: {extracted.City}\n\n" +
                $"Our team will contact you shortly for delivery. Thank you! 🙏";
        }

        // Mirror of TryHandleQuantityReplyAsync — returns the reply, or null if it doesn't apply.
        private async Task<string?> SimulateQuantityReplyAsync(AgentThread thread, string messageText)
        {
            var lastAiMsg = GetLastAiMessage(thread);
            var lastWasAvailability = lastAiMsg?.MessageText
                .Contains(AvailabilityMarker, StringComparison.OrdinalIgnoreCase) == true;
            if (!lastWasAvailability) return null;

            var qtyMatch = Regex.Match(messageText, @"\b(\d+)\b");
            if (!qtyMatch.Success ||
                !decimal.TryParse(qtyMatch.Groups[1].Value, out var qty) ||
                qty <= 0)
            {
                return null;
            }

            var productName = ExtractProductNameFromAvailMsg(lastAiMsg!.MessageText);
            if (string.IsNullOrWhiteSpace(productName)) return null;

            return await CheckQuantityInDbAsync(productName, qty, thread);
        }

        // Mirror of HandleAiReplyAsync — same sentinel handling and DB lookups, returns the text.
        private async Task<string> SimulateAiReplyAsync(
            AgentThread thread, string messageText, string systemPrompt, string model, string apiKey)
        {
            var historyText = BuildConversationHistory(thread, takeLast: 12);
            var aiResult = await _ai.GetReplyAsync(systemPrompt, historyText, messageText, apiKey, model);

            if (aiResult.NeedsHuman || string.IsNullOrWhiteSpace(aiResult.Reply))
            {
                if (!string.IsNullOrWhiteSpace(aiResult.Error))
                    return $"⚠️ {aiResult.Error}";

                return "Thank you for reaching out! A human agent will be with you shortly. 🙏\n\n" +
                       "_(HUMAN_NEEDED — the AI would pause this chat for a human in production.)_";
            }

            var aiReply = aiResult.Reply.Trim();

            var productMatch = Regex.Match(aiReply, $@"{ProductCheckSignal}[:\-]\s*(.+)", RegexOptions.IgnoreCase);
            if (productMatch.Success)
                return await CheckProductInDbAsync(productMatch.Groups[1].Value.Trim());

            var qtySignalMatch = Regex.Match(aiReply, $@"{QtyCheckSignal}[:\-]\s*(\d+[\.,]?\d*)", RegexOptions.IgnoreCase);
            if (qtySignalMatch.Success &&
                decimal.TryParse(qtySignalMatch.Groups[1].Value.Replace(",", ""), out var signalQty))
            {
                var lastAvailMsg = thread.Messages
                    .Where(m => m.SenderType == "ai" &&
                                m.MessageText.Contains(AvailabilityMarker, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(m => m.SentAt)
                    .FirstOrDefault();

                var productForQty = ExtractProductNameFromAvailMsg(lastAvailMsg?.MessageText ?? "");
                if (!string.IsNullOrWhiteSpace(productForQty))
                    return await CheckQuantityInDbAsync(productForQty, signalQty, thread);
            }

            return aiReply;
        }

        // Human agent replying from the inbox UI. Pauses the AI so it won't talk over them.
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

        // ── Stage handlers ───────────────────────────────────────────────────

        // Confirm or cancel a pending order. Only acts when the previous AI message was the
        // order summary — otherwise "yes" could mean anything and we let the AI handle it.
        private async Task<bool> TryHandleYesNoAsync(
            AgentThread thread, WhatsAppChannel channel, string fromPhone, string messageText)
        {
            var trimmed = messageText.Trim();
            var isYes = YesWords.Contains(trimmed);
            var isNo = NoWords.Contains(trimmed);

            if (!isYes && !isNo) return false;

            var lastAiMsg = GetLastAiMessage(thread);
            var lastWasSummary = lastAiMsg?.MessageText
                .Contains(OrderSummaryMarker, StringComparison.OrdinalIgnoreCase) == true;

            if (!lastWasSummary) return false;

            if (isNo)
            {
                await SendAndSaveAsync(thread, channel, fromPhone,
                    "No problem! Would you like to order a different product or quantity?",
                    "ai", null);
                return true;
            }

            // Customer said YES — extract order details from history and create the invoice.
            var history = BuildConversationHistory(thread);
            var extracted = await _ai.ExtractOrderAsync(history, channel.AiApiKey, channel.AiModel);

            if (extracted == null || extracted.Quantity <= 0)
            {
                await SendAndSaveAsync(thread, channel, fromPhone,
                    "Sorry, I had trouble processing your order. Could you please tell me the product and quantity again?",
                    "ai", null);
                return true;
            }

            var saleId = await CreateOrderAsync(extracted, thread);

            string confirmMsg;
            if (saleId.HasValue)
            {
                confirmMsg =
                    $"🎉 *Order Confirmed!*\n" +
                    $"📋 Invoice: *INV-{saleId.Value:D5}*\n" +
                    $"📦 Product: {extracted.ProductName}\n" +
                    $"🔢 Quantity: {extracted.Quantity:N0} units\n" +
                    $"💰 Total: PKR {extracted.TotalAmount:N0}\n" +
                    $"📍 City: {extracted.City}\n\n" +
                    $"Our team will contact you shortly for delivery. Thank you! 🙏";
            }
            else
            {
                // DB insert failed — escalate to a human and stop the bot.
                confirmMsg = "Thank you! Our team will review and contact you shortly. 🙏";
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
            return true;
        }

        // Customer just got an "X is available" message and replied with a number → treat it as quantity.
        // We only do this if availability was the *most recent* AI message; anything in between resets state.
        private async Task<bool> TryHandleQuantityReplyAsync(
            AgentThread thread, WhatsAppChannel channel, string fromPhone, string messageText)
        {
            var lastAiMsg = GetLastAiMessage(thread);
            var lastWasAvailability = lastAiMsg?.MessageText
                .Contains(AvailabilityMarker, StringComparison.OrdinalIgnoreCase) == true;

            if (!lastWasAvailability) return false;

            var qtyMatch = Regex.Match(messageText, @"\b(\d+)\b");
            if (!qtyMatch.Success ||
                !decimal.TryParse(qtyMatch.Groups[1].Value, out var qty) ||
                qty <= 0)
            {
                return false;
            }

            var productName = ExtractProductNameFromAvailMsg(lastAiMsg!.MessageText);
            if (string.IsNullOrWhiteSpace(productName)) return false;

            var qtyReply = await CheckQuantityInDbAsync(productName, qty, thread);
            await SendAndSaveAsync(thread, channel, fromPhone, qtyReply, "ai", null);
            return true;
        }

        // No structured stage detected — hand the message to the LLM and react to its response.
        // The AI may either reply normally or emit one of our sentinel tokens for a DB lookup.
        private async Task HandleAiReplyAsync(
            AgentThread thread, WhatsAppChannel channel, string fromPhone, string messageText)
        {
            var historyText = BuildConversationHistory(thread, takeLast: 12);
            var systemPrompt = string.IsNullOrWhiteSpace(channel.AiSystemPrompt)
                ? BaseSystemPrompt : channel.AiSystemPrompt;

            var aiResult = await _ai.GetReplyAsync(
                systemPrompt, historyText, messageText, channel.AiApiKey, channel.AiModel);

            // AI gave up (or returned empty) — escalate once and pause.
            if (aiResult.NeedsHuman || string.IsNullOrWhiteSpace(aiResult.Reply))
            {
                await EscalateToHumanAsync(thread, channel, fromPhone, aiResult.ModelUsed);
                return;
            }

            var aiReply = aiResult.Reply.Trim();

            // Sentinel: AI wants us to verify a product against the catalog.
            var productMatch = Regex.Match(aiReply, $@"{ProductCheckSignal}[:\-]\s*(.+)", RegexOptions.IgnoreCase);
            if (productMatch.Success)
            {
                var productName = productMatch.Groups[1].Value.Trim();
                var dbReply = await CheckProductInDbAsync(productName);
                await SendAndSaveAsync(thread, channel, fromPhone, dbReply, "ai", aiResult.ModelUsed);
                return;
            }

            // Sentinel: AI wants us to verify a requested quantity.
            var qtySignalMatch = Regex.Match(aiReply, $@"{QtyCheckSignal}[:\-]\s*(\d+[\.,]?\d*)", RegexOptions.IgnoreCase);
            if (qtySignalMatch.Success &&
                decimal.TryParse(qtySignalMatch.Groups[1].Value.Replace(",", ""), out var signalQty))
            {
                var lastAvailMsg = thread.Messages
                    .Where(m => m.SenderType == "ai" &&
                                m.MessageText.Contains(AvailabilityMarker, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(m => m.SentAt)
                    .FirstOrDefault();

                var productForQty = ExtractProductNameFromAvailMsg(lastAvailMsg?.MessageText ?? "");
                if (!string.IsNullOrWhiteSpace(productForQty))
                {
                    var qtyReply = await CheckQuantityInDbAsync(productForQty, signalQty, thread);
                    await SendAndSaveAsync(thread, channel, fromPhone, qtyReply, "ai", aiResult.ModelUsed);
                    return;
                }
            }

            // Plain conversational reply (greeting, asking for name/city, etc.).
            // Guard against the AI leaking a sentinel token into a user-facing message.
            if (!aiReply.Contains(ProductCheckSignal, StringComparison.OrdinalIgnoreCase) &&
                !aiReply.Contains(QtyCheckSignal, StringComparison.OrdinalIgnoreCase))
            {
                await SendAndSaveAsync(thread, channel, fromPhone, aiReply, "ai", aiResult.ModelUsed);
            }
        }

        // ── DB lookups ───────────────────────────────────────────────────────

        // Look up a product by name (fuzzy contains-match on ProductName or ItemName).
        // Returns a customer-facing reply with stock + price, or a list of alternatives if not found.
        private async Task<string> CheckProductInDbAsync(string productName)
        {
            var allItems = await _db.Items.AsNoTracking()
                .Where(i => i.IsActive == true)
                .ToListAsync();

            var search = productName.ToLower();
            var matched = allItems.FirstOrDefault(i =>
                (i.ProductName?.ToLower().Contains(search) ?? false) ||
                (i.ItemName?.ToLower().Contains(search) ?? false));

            if (matched == null)
            {
                var names = allItems
                    .Select(i => $"• {i.ProductName ?? i.ItemName}")
                    .Where(n => n != "• ")
                    .ToList();

                if (names.Count == 0)
                    return $"Sorry, we don't carry *{productName}*, and we currently have no products in stock.";

                return $"Sorry, we don't carry *{productName}*. 😔\n\n" +
                       "Here's what we currently have:\n" + string.Join("\n", names) +
                       "\n\nWould you like to order any of these? 🛍️";
            }

            var stock = await _db.Stock.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ItemId == matched.ItemID);
            var qty = stock?.Quantity ?? 0;
            var displayName = matched.ProductName ?? matched.ItemName;

            if (qty <= 0)
                return $"Sorry, *{displayName}* is currently *out of stock*. 😔\nLet me know if you need something else.";

            var price = matched.SalePrice.HasValue
                ? $"PKR {matched.SalePrice:N0}"
                : "price on request";

            var size = !string.IsNullOrWhiteSpace(matched.Size) ? $"\n📐 Size: {matched.Size}" : "";
            var packing = !string.IsNullOrWhiteSpace(matched.Packing) ? $"\n📦 Packing: {matched.Packing}" : "";

            return $"Great news! ✅ *{displayName}* is available!\n" +
                   $"📦 Stock: *{qty:N0} units*\n" +
                   $"💰 Price: *{price} per unit*{size}{packing}\n\n" +
                   $"How many units would you like to order? 🛍️";
        }

        // Verify the requested quantity is in stock and build an order summary for confirmation.
        private async Task<string> CheckQuantityInDbAsync(
            string productName, decimal requestedQty, AgentThread thread)
        {
            var search = productName.ToLower();
            var item = await _db.Items.AsNoTracking()
                .FirstOrDefaultAsync(i =>
                    i.IsActive == true &&
                    ((i.ProductName != null && i.ProductName.ToLower().Contains(search)) ||
                     (i.ItemName != null && i.ItemName.ToLower().Contains(search))));

            if (item == null)
                return "Sorry, I couldn't find that product. Could you confirm the product name?";

            var stock = await _db.Stock.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ItemId == item.ItemID);
            var available = stock?.Quantity ?? 0;
            var displayName = item.ProductName ?? item.ItemName;
            var price = item.SalePrice ?? 0;

            if (requestedQty > available)
            {
                return $"Sorry, we only have *{available:N0} units* of *{displayName}* available. 😔\n" +
                       $"Would you like to proceed with {available:N0} units instead?";
            }

            var total = price * requestedQty;

            return $"✅ *Order Summary:*\n" +
                   $"👤 Name: {thread.CustomerName}\n" +
                   $"📦 Product: {displayName}\n" +
                   $"🔢 Quantity: {requestedQty:N0} units\n" +
                   $"💰 Price: PKR {price:N0} per unit\n" +
                   $"💵 Total: PKR {total:N0}\n\n" +
                   $"Reply *YES* to confirm your order or *NO* to cancel.";
        }

        // Create the SaleInvoice + line item, decrement stock, link to a Party (create one if new).
        // Returns the new SaleId on success, null if anything failed validation.
        private async Task<int?> CreateOrderAsync(OrderExtractResult order, AgentThread thread)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(order.ProductName) || order.Quantity <= 0)
                    return null;

                var search = order.ProductName.ToLower();
                var item = await _db.Items.AsNoTracking()
                    .FirstOrDefaultAsync(i =>
                        i.IsActive == true &&
                        ((i.ProductName != null && i.ProductName.ToLower().Contains(search)) ||
                         (i.ItemName != null && i.ItemName.ToLower().Contains(search))));
                if (item == null) return null;

                var stock = await _db.Stock.FirstOrDefaultAsync(s => s.ItemId == item.ItemID);
                if (stock == null || stock.Quantity < order.Quantity) return null;

                var unitPrice = order.UnitPrice > 0 ? order.UnitPrice : item.SalePrice ?? 0;
                var total = unitPrice * order.Quantity;

                // Reuse the customer record if we've sold to this number before, otherwise create one.
                var party = await _db.Parties.FirstOrDefaultAsync(p =>
                    p.Mobile == thread.CustomerPhone || p.Phone == thread.CustomerPhone);

                if (party == null)
                {
                    party = new Party
                    {
                        PartyCode = $"WA-{thread.CustomerPhone[^6..]}",
                        PartyName = string.IsNullOrWhiteSpace(order.CustomerName)
                            ? thread.CustomerName
                            : order.CustomerName,
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
            catch
            {
                return null;
            }
        }

        // ── Thread / message persistence ─────────────────────────────────────

        private async Task<AgentThread> GetOrCreateThreadAsync(
            int channelId, string fromPhone, string customerName)
        {
            var thread = await _db.AgentThreads
                .Include(t => t.Messages)
                .FirstOrDefaultAsync(t =>
                    t.CustomerPhone == fromPhone && t.WhatsAppChannelId == channelId);

            if (thread == null)
            {
                thread = new AgentThread
                {
                    CustomerPhone = fromPhone,
                    CustomerName = customerName,
                    WhatsAppChannelId = channelId,
                    Status = "active",
                    IsAiPaused = false,
                    CreatedAt = DateTime.UtcNow,
                    LastMessageAt = DateTime.UtcNow
                };
                _db.AgentThreads.Add(thread);
                await _db.SaveChangesAsync();
                return thread;
            }

            // Reactivate a previously-closed thread on a new inbound.
            if (thread.Status == "closed") thread.Status = "active";

            // Only overwrite the name if we got a real profile name (not just the phone number echo).
            if (!string.IsNullOrWhiteSpace(customerName) && customerName != fromPhone)
                thread.CustomerName = customerName;

            return thread;
        }

        private async Task SaveInboundMessageAsync(
            AgentThread thread, string messageText, string whatsappMessageId, bool wasVoiceInput)
        {
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

            thread.LastInboundWasVoice = wasVoiceInput;
            thread.LastMessageAt = DateTime.UtcNow;
            thread.UnreadCount++;
            await _db.SaveChangesAsync();
        }

        // Sends the reply (as voice if the customer last spoke to us in voice, otherwise as text),
        // then persists it. Falls back to text automatically if the voice pipeline fails.
        private async Task SendAndSaveAsync(
            AgentThread thread, WhatsAppChannel channel,
            string toPhone, string text, string senderType, string? aiModel = null)
        {
            WhatsAppSendResult result;

            if (thread.LastInboundWasVoice)
            {
                result = await SendAsVoiceAsync(channel, toPhone, text);
                if (!result.Success)
                {
                    // Voice path broke — don't leave the customer hanging, send the text.
                    result = await _whatsApp.SendTextMessageAsync(
                        channel.PhoneNumberId, channel.AccessToken, toPhone, text);
                }
            }
            else
            {
                result = await _whatsApp.SendTextMessageAsync(
                    channel.PhoneNumberId, channel.AccessToken, toPhone, text);
            }

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

        // TTS → upload to Meta → send as voice note. Three places this can fail; each returns
        // a structured error so the caller can decide whether to fall back to plain text.
        private async Task<WhatsAppSendResult> SendAsVoiceAsync(
            WhatsAppChannel channel, string toPhone, string text)
        {
            try
            {
                var spokenText = StripEmojisAndMarkdown(text);

                var audioBytes = await _speech.TextToSpeechAsync(spokenText);
                if (audioBytes == null || audioBytes.Length == 0)
                    return new WhatsAppSendResult { Success = false, Error = "TTS returned no audio" };

                var mediaId = await _whatsApp.UploadMediaAsync(
                    channel.PhoneNumberId, channel.AccessToken, audioBytes, "audio/mpeg");
                if (string.IsNullOrEmpty(mediaId))
                    return new WhatsAppSendResult { Success = false, Error = "Failed to upload audio" };

                return await _whatsApp.SendAudioMessageAsync(
                    channel.PhoneNumberId, channel.AccessToken, toPhone, mediaId);
            }
            catch (Exception ex)
            {
                return new WhatsAppSendResult { Success = false, Error = ex.Message };
            }
        }

        // Pause AI and send the standard handoff message — but only once per pause to avoid spam.
        private async Task EscalateToHumanAsync(
            AgentThread thread, WhatsAppChannel channel, string fromPhone, string? aiModel)
        {
            var wasAlreadyPaused = thread.IsAiPaused;
            thread.IsAiPaused = true;
            await _db.SaveChangesAsync();

            if (wasAlreadyPaused) return;

            await SendAndSaveAsync(thread, channel, fromPhone,
                "Thank you for reaching out! A human agent will be with you shortly. 🙏",
                "ai", aiModel);

            // Internal marker row so the inbox UI can show "needs human" state without re-sending.
            _db.AgentMessages.Add(new AgentMessage
            {
                AgentThreadId = thread.Id,
                Direction = "outbound",
                SenderType = "ai",
                MessageText = "[NEEDS_HUMAN]",
                Status = "ai_paused",
                SentAt = DateTime.UtcNow.AddMilliseconds(50),
                IsRead = true,
                AiModel = aiModel
            });
            await _db.SaveChangesAsync();
        }

        // ── Pure helpers ─────────────────────────────────────────────────────

        // Most recent AI message that's actually visible to the customer
        // (internal markers like "ai_paused" don't count).
        private static AgentMessage? GetLastAiMessage(AgentThread thread) =>
            thread.Messages
                .Where(m => m.SenderType == "ai" && m.Status != "ai_paused")
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefault();

        // Flatten the thread into a "Customer: ... / Agent: ..." transcript for the LLM.
        private static string BuildConversationHistory(AgentThread thread, int? takeLast = null)
        {
            var query = thread.Messages
                .Where(m => m.Status != "ai_paused")
                .OrderBy(m => m.SentAt)
                .AsEnumerable();

            if (takeLast.HasValue) query = query.TakeLast(takeLast.Value);

            return string.Join("\n", query.Select(m =>
                $"{(m.SenderType == "customer" ? "Customer" : "Agent")}: {m.MessageText}"));
        }

        // Pull the product name back out of an availability message we sent earlier.
        // Tries the bold-wrapped form first, falls back to plain.
        private static string ExtractProductNameFromAvailMsg(string msg)
        {
            var m = Regex.Match(msg, @"\*(.+?)\*\s+is available", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();

            m = Regex.Match(msg, @"(.+?)\s+is available", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
        }

        // TTS reads emoji and markdown literally ("star asterisk star") — strip them before speaking.
        private static string StripEmojisAndMarkdown(string text)
        {
            var cleaned = Regex.Replace(text, @"[\*_~`]", "");

            // Strip the emoji we actually use in our templates. Cheaper and safer than a full
            // Unicode-category regex which can over-match on legitimate punctuation.
            string[] emojis = { "📦", "💰", "✅", "📋", "👤", "🔢", "💵", "📍",
                                "📐", "🛍️", "🎉", "🙏", "😊", "😔", "📞" };

            foreach (var e in emojis) cleaned = cleaned.Replace(e, "");

            return cleaned.Trim();
        }
    }
}