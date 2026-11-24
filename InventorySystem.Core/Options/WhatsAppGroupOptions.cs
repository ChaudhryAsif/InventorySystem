namespace InventorySystem.Core.Options;

// Address and secret key of the small WhatsApp service that holds the linked number.
public sealed class WhatsAppGroupOptions
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
}
