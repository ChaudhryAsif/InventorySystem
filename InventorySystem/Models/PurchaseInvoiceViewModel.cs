namespace InventorySystem.Models
{
    public class PurchaseInvoiceViewModel
    {
        public int PurchaseId { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public string? VendorID { get; set; }
        public string? BillNo { get; set; }
        public int? BranchID { get; set; }
        public int? PaymentMode { get; set; }
        public string? Remarks { get; set; }
        public decimal? GSTPer { get; set; }
        public decimal? GSTAmount { get; set; }
        public decimal? FreightExp { get; set; }
        public decimal? OtherExp { get; set; }
        public decimal? Discount { get; set; }
        public decimal? TotalAmount { get; set; }
        public decimal? NetAmount { get; set; }

        public List<PurchaseInvoiceBodyViewModel> Items { get; set; } = new();
    }
}
