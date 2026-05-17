using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    //[Authorize]
    public class WhatsAppController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWhatsAppService _whatsApp;
        private readonly IChatbotService _chatbot;

        public WhatsAppController(
            ApplicationDbContext db,
            IWhatsAppService whatsApp,
            IChatbotService chatbot)
        {
            _db = db;
            _whatsApp = whatsApp;
            _chatbot = chatbot;
        }

        // ── CONFIGURATION ────────────────────────────────────────────────────

        public async Task<IActionResult> Configure()
        {
            var channel = await _db.WhatsAppChannels.FirstOrDefaultAsync() ?? new WhatsAppChannel();
            return View(channel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Configure(WhatsAppChannel model)
        {
            if (!ModelState.IsValid) return View(model);

            var existing = await _db.WhatsAppChannels.FindAsync(model.Id);
            if (existing == null)
            {
                model.CreatedAt = DateTime.UtcNow;
                model.UpdatedAt = DateTime.UtcNow;
                _db.WhatsAppChannels.Add(model);
            }
            else
            {
                existing.PhoneNumberId = model.PhoneNumberId;
                existing.WabaId = model.WabaId;
                existing.AccessToken = model.AccessToken;
                existing.WebhookVerifyToken = model.WebhookVerifyToken;
                existing.DisplayPhoneNumber = model.DisplayPhoneNumber;
                existing.IsActive = model.IsActive;
                existing.MetaAppId = model.MetaAppId;
                existing.MetaAppSecret = model.MetaAppSecret;
                existing.AiApiKey = model.AiApiKey;
                existing.AiModel = model.AiModel;
                existing.AiSystemPrompt = model.AiSystemPrompt;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = "WhatsApp channel configuration saved successfully.";
            return RedirectToAction(nameof(Configure));
        }

        [HttpPost]
        public async Task<IActionResult> RegisterWebhook(int channelId, string webhookBaseUrl)
        {
            var channel = await _db.WhatsAppChannels.FindAsync(channelId);
            if (channel == null) return Json(new { success = false, message = "Channel not found." });

            var webhookUrl = $"{webhookBaseUrl.TrimEnd('/')}/WhatsApp/Webhook";
            var result = await _whatsApp.RegisterWebhookAsync(
                channel.PhoneNumberId, channel.AccessToken, webhookUrl, channel.WebhookVerifyToken);

            if (result)
            {
                channel.IsWebhookRegistered = true;
                channel.WebhookRegisteredAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return Json(new { success = result, message = result ? "Webhook registered successfully." : "Webhook registration failed." });
        }

        // ── WEBHOOK (Public — No Auth) ────────────────────────────────────────

        [AllowAnonymous]
        [HttpGet("/WhatsApp/Webhook")]
        public IActionResult MetaWebhookVerify()
        {
            // Use .Value to get the clean string value
            var hubMode = Request.Query["hub.mode"].ToString();
            var hubChallenge = Request.Query["hub.challenge"].ToString();
            var hubVerifyToken = Request.Query["hub.verify_token"].ToString();

            // Verify token matches your dashboard: "AsifPOS@2026"
            if (hubMode == "subscribe" && hubVerifyToken == "AsifPOS@2026")
            {
                // Return as plain text WITHOUT quotes
                return Content(hubChallenge, "text/plain");
            }

            return Forbid();
        }

        [AllowAnonymous]
        [HttpPost("/WhatsApp/Webhook")]
        public async Task<IActionResult> WebhookPost()
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            try
            {
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.GetProperty("object").GetString() != "whatsapp_business_account")
                    return Ok();

                foreach (var entry in root.GetProperty("entry").EnumerateArray())
                {
                    foreach (var change in entry.GetProperty("changes").EnumerateArray())
                    {
                        var value = change.GetProperty("value");
                        if (!value.TryGetProperty("messages", out var messages)) continue;

                        var phoneNumberId = value.GetProperty("metadata").GetProperty("phone_number_id").GetString() ?? "";

                        foreach (var message in messages.EnumerateArray())
                        {
                            var type = message.GetProperty("type").GetString();
                            if (type != "text") continue;

                            var fromPhone = message.GetProperty("from").GetString() ?? "";
                            var msgId = message.GetProperty("id").GetString() ?? "";
                            var msgText = message.GetProperty("text").GetProperty("body").GetString() ?? "";
                            var profileName = value.TryGetProperty("contacts", out var contacts) &&
                                              contacts.GetArrayLength() > 0
                                ? contacts[0].GetProperty("profile").GetProperty("name").GetString() ?? fromPhone
                                : fromPhone;

                            await _chatbot.ProcessInboundMessageAsync(
                                phoneNumberId, fromPhone, profileName, msgText, msgId);
                        }
                    }
                }
            }
            catch { /* Log in production */ }

            return Ok();
        }

        // ── INBOX ─────────────────────────────────────────────────────────────

        public async Task<IActionResult> Inbox(string status = "all", int? threadId = null)
        {
            var query = _db.AgentThreads
                .AsNoTracking()   // ← CRITICAL: prevents EF tracking merge with activeThread
                .Include(t => t.WhatsAppChannel)
                .Include(t => t.Messages
                    .Where(m => m.Status != "ai_paused")   // skip internal flag for preview
                    .OrderByDescending(m => m.SentAt)
                    .Take(1))
                .AsQueryable();

            if (status == "ai_paused")
                query = query.Where(t => t.IsAiPaused && t.Status != "closed");
            else if (status == "active")
                query = query.Where(t => !t.IsAiPaused && t.Status == "active");
            else if (status == "closed")
                query = query.Where(t => t.Status == "closed");

            var threads = await query
                .OrderByDescending(t => t.LastMessageAt)
                .ToListAsync();

            // Load active thread — separate tracked query
            AgentThread? activeThread = null;
            var targetId = threadId ?? (threads.Any() ? threads.First().Id : (int?)null);

            if (targetId.HasValue)
            {
                activeThread = await _db.AgentThreads
                    .Include(t => t.WhatsAppChannel)
                    .Include(t => t.Messages.OrderBy(m => m.SentAt))
                    .FirstOrDefaultAsync(t => t.Id == targetId.Value);

                if (activeThread != null)
                {
                    var unread = activeThread.Messages
                        .Where(m => !m.IsRead && m.Direction == "inbound").ToList();
                    unread.ForEach(m => m.IsRead = true);
                    activeThread.UnreadCount = 0;
                    await _db.SaveChangesAsync();
                }
            }

            ViewBag.Status = status;
            ViewBag.AiPausedCount = await _db.AgentThreads.CountAsync(t => t.IsAiPaused && t.Status != "closed");
            ViewBag.ActiveCount = await _db.AgentThreads.CountAsync(t => !t.IsAiPaused && t.Status == "active");
            ViewBag.ActiveThread = activeThread;

            return View(threads);
        }

        public async Task<IActionResult> Chat(int id)
        {
            var thread = await _db.AgentThreads
                .Include(t => t.WhatsAppChannel)
                .Include(t => t.Messages.OrderBy(m => m.SentAt))
                .FirstOrDefaultAsync(t => t.Id == id);

            if (thread == null) return NotFound();

            // Mark as read
            var unread = thread.Messages.Where(m => !m.IsRead && m.Direction == "inbound").ToList();
            unread.ForEach(m => m.IsRead = true);
            thread.UnreadCount = 0;
            await _db.SaveChangesAsync();

            return View(thread);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HumanReply(int threadId, string replyText, string status = "all")
        {
            if (string.IsNullOrWhiteSpace(replyText))
                return RedirectToAction(nameof(Inbox), new { status, threadId });

            await _chatbot.HumanReplyAsync(threadId, replyText);

            // ← Always redirect back to Inbox (two-panel), never Chat
            return RedirectToAction(nameof(Inbox), new { status, threadId });
        }

        [HttpPost]
        public async Task<IActionResult> CloseThread(int threadId)
        {
            await _chatbot.CloseThreadAsync(threadId);
            return RedirectToAction(nameof(Inbox));
        }

        [HttpGet]
        public async Task<IActionResult> GetNewMessages(int threadId, int afterMessageId)
        {
            var messages = await _db.AgentMessages
                .Where(m => m.AgentThreadId == threadId
                         && m.Id > afterMessageId
                         && m.Status != "ai_paused")  // ← skip internal flag messages
                .OrderBy(m => m.SentAt)
                .Select(m => new {
                    m.Id,
                    m.MessageText,
                    m.SenderType,
                    m.Direction,
                    m.Status,
                    SentAt = m.SentAt.ToString("hh:mm tt"),
                    m.AiModel
                })
                .ToListAsync();

            return Json(messages);
        }

        // ── TEST MESSAGE ──────────────────────────────────────────────────────

        public async Task<IActionResult> TestMessage()
        {
            var channel = await _db.WhatsAppChannels.FirstOrDefaultAsync(c => c.IsActive);
            ViewBag.Channel = channel;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TestMessage(string toPhone, string message)
        {
            var channel = await _db.WhatsAppChannels.FirstOrDefaultAsync(c => c.IsActive);
            if (channel == null)
            {
                TempData["Error"] = "No active WhatsApp channel configured.";
                return RedirectToAction(nameof(TestMessage));
            }

            var result = await _whatsApp.SendTextMessageAsync(
                channel.PhoneNumberId, channel.AccessToken, toPhone, message);

            TempData[result.Success ? "Success" : "Error"] =
                result.Success ? $"Message sent! ID: {result.MessageId}" : $"Failed: {result.Error}";

            return RedirectToAction(nameof(TestMessage));
        }

        [HttpPost]
        public async Task<IActionResult> ToggleAI(int threadId, string status = "all")
        {
            var thread = await _db.AgentThreads.FindAsync(threadId);
            if (thread == null) return NotFound();

            thread.IsAiPaused = !thread.IsAiPaused;
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Inbox), new { status, threadId });
        }

        // ── CLOSE THREAD ─────────────────────────────────────────────────────────────

        [HttpPost]
        public async Task<IActionResult> CloseThread(int threadId, string status = "all")
        {
            await _chatbot.CloseThreadAsync(threadId);
            return RedirectToAction(nameof(Inbox), new { status });
        }
    }
}