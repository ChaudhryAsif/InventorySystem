namespace InventorySystem.Models
{
    public class Item
    {
        public int ItemId { get; set; }
        public int CategoryId { get; set; }
        public string ItemName { get; set; }
        public decimal SalePrice { get; set; }
        public string Barcode { get; set; }
        public bool IsActive { get; set; }
        public byte[] ItemImage { get; set; }
    }
}
