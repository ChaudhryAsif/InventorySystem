namespace InventorySystem.Models
{
    public class ItemViewModel
    {
        public long ItemID { get; set; }
        public int CategoryID { get; set; }
        public string ItemName { get; set; }
        public string CompanyName { get; set; }
        public string ProductName { get; set; }
        public string Design { get; set; }
        public string Size { get; set; }
        public string PurchaseAccount { get; set; }
        public string SaleAccount { get; set; }
        public bool IsActive { get; set; }
        public bool IsMarinated { get; set; }
        public string Packing { get; set; }
        public string Origin { get; set; }
        public decimal? SalePrice { get; set; }
        public string WHCOGS { get; set; }
        public string Description { get; set; } // For UI description field
        public string ImagePath { get; set; }
    }
}
