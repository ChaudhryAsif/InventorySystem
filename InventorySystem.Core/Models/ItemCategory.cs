using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Core.Models
{
    public class ItemCategory
    {
        [Key]
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        [MaxLength(32)]
        public string? GroupType { get; set; }
        public string? Description { get; set; }
        [MaxLength(500)]
        public string? Address { get; set; }
        [MaxLength(32)]
        public string? ContactNumber { get; set; }
        public bool? IsActive { get; set; }
        public DateTime? CreatedDate { get; set; }
    }
}
