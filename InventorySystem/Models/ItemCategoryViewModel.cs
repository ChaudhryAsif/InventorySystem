namespace InventorySystem.Models
{
    public class ItemCategoryViewModel
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; }
        public string? GroupType { get; set; }
        public string Description { get; set; }
        public string? Address { get; set; }
        public string? ContactNumber { get; set; }
        public bool? IsActive { get; set; }
        public DateTime? CreatedDate { get; set; }
        // Acknowledges the exact category/item status preview returned by Save.
        public string? StatusConfirmationToken { get; set; }
    }
}
