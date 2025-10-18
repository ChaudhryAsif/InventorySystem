namespace InventorySystem.Core.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string SKU { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public Category? Category { get; set; }
        public decimal CostPrice { get; set; }
        public decimal SalePrice { get; set; }
        public int ReorderLevel { get; set; } = 0;
        public int CurrentStock { get; set; } = 0;
    }
}
