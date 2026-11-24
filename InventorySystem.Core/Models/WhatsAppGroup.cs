using System.ComponentModel.DataAnnotations;
namespace InventorySystem.Core.Models;

// Kinds of document that can be sent to a WhatsApp group. Add a name here to give a new page its own group.
public static class WhatsAppDocumentTypes
{
    public const string GatePass = "GatePass";
    public static readonly string[] All = [GatePass];
}

// Which group receives which kind of document, and whether sending is switched on.
public class WhatsAppDestination
{
    public int Id { get; set; }
    [Required, MaxLength(40)] public string DocumentType { get; set; } = "";
    [MaxLength(120)] public string GroupId { get; set; } = "";
    [MaxLength(200)] public string GroupName { get; set; } = "";
    public bool Enabled { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

// One row per send attempt, so staff can see what went out and why something failed.
public class WhatsAppSendLog
{
    public int Id { get; set; }
    [Required, MaxLength(40)] public string DocumentType { get; set; } = "";
    public int? DocumentId { get; set; }
    [MaxLength(60)] public string? DocumentNumber { get; set; }
    [MaxLength(120)] public string GroupId { get; set; } = "";
    [Required, MaxLength(10)] public string Status { get; set; } = "";
    [MaxLength(500)] public string? Error { get; set; }
    [MaxLength(80)] public string? RequestId { get; set; }
    [MaxLength(100)] public string? SentBy { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
