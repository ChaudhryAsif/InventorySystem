using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Core.Models
{
    public class ItemCategory
    {
        [Key]
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool? IsActive { get; set; }
        public DateTime? CreatedDate { get; set; }
        public ICollection<Product>? Products { get; set; }
    }
}
