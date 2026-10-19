namespace InventorySystem.Models
{
    public class PurchaseInvoiceBodyViewModel
    {
        public long? Itemid { get; set; }
        public string? Desc { get; set; }
        public int? PackageCount { get; set; }
        public string? PackageType { get; set; } // Reel / Drum; quantity remains total net kg.
        public decimal? Quantity { get; set; }
        public decimal? PurPrice { get; set; }
        public decimal? SalePrice { get; set; }
        public decimal? DiscPer { get; set; }
        public decimal? DiscAmt { get; set; }
        public decimal? Total { get; set; }
    }
}
