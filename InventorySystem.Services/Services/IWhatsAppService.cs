using System.Threading.Tasks;

namespace InventorySystem.Core.Services
{
    public interface IWhatsAppService
    {
        Task<WhatsAppSendResult> SendTextMessageAsync(string phoneNumberId, string accessToken, string toPhone, string message);
        Task<bool> RegisterWebhookAsync(string phoneNumberId, string accessToken, string webhookUrl, string verifyToken);
    }

    public class WhatsAppSendResult
    {
        public bool Success { get; set; }
        public string? MessageId { get; set; }
        public string? Error { get; set; }
    }
}