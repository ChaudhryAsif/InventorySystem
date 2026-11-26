using System.ComponentModel.DataAnnotations;
namespace InventorySystem.Core.Models;

// One line per change to roles, permissions or logins, so the SuperAdmin can see who changed what and when.
public class AdminAuditLog
{
    public int Id { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public int? ActorId { get; set; }
    [MaxLength(100)] public string ActorName { get; set; } = "";
    [Required, MaxLength(60)] public string Action { get; set; } = "";
    [MaxLength(200)] public string Target { get; set; } = "";
    [MaxLength(2000)] public string? Detail { get; set; }
}
