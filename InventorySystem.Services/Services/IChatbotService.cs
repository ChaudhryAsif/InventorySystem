using System.Threading.Tasks;

namespace InventorySystem.Core.Services
{
    public interface IChatbotService
    {
        Task ProcessInboundMessageAsync(string channelPhoneNumberId, string fromPhone, string customerName, string messageText, string whatsappMessageId);
        Task<bool> HumanReplyAsync(int threadId, string replyText);
        Task<bool> CloseThreadAsync(int threadId);
    }
}