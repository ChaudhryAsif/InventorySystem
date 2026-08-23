namespace InventorySystem.Models
{
    public class PurchaseReturnViewModel
    {
        public int? PurchaseReturnId { get; set; }   // null/0 = new return, > 0 = edit
        public DateTime? ReturnDate { get; set; }
        public string? VendorID { get; set; }
        public int? OriginalPurchaseId { get; set; }
        public int? BranchID { get; set; }
        public int? PaymentMode { get; set; }
        public string? RefNo { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal NetAmount { get; set; }
        public string? Remarks { get; set; }
        public List<PurchaseReturnItemViewModel> Items { get; set; } = [];
    }

    public class PurchaseReturnItemViewModel
    {
        public long? ItemId { get; set; }
        public string? Desc { get; set; }
        public decimal Quantity { get; set; }
        public decimal PurPrice { get; set; }
        public decimal DiscPer { get; set; }
        public decimal DiscAmt { get; set; }
        public decimal Total { get; set; }
    }
}