using System.ComponentModel.DataAnnotations;
namespace InventorySystem.Core.Models;
public class OperationRecord
{
    public long Id { get; set; }
    [MaxLength(100)] public string UserId { get; set; } = "";
    [MaxLength(100)] public string Action { get; set; } = "";
    [MaxLength(100)] public string? Reference { get; set; }
    [MaxLength(64)] public string? RequestId { get; set; }
    [MaxLength(64)] public string? RequestHash { get; set; }
    public string? ResponseJson { get; set; }
    public DateTime CreatedUtc { get; set; }
}
