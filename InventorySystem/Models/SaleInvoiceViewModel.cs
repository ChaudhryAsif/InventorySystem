namespace InventorySystem.Models
{
    public class SaleInvoiceViewModel
    {
        public DateTime? SaleDate { get; set; }
        public string? CustomerID { get; set; }
        public int? BranchID { get; set; }
        public int? PaymentMode { get; set; }
        public string? RefNo { get; set; }
        public decimal GSTPer { get; set; }
        public decimal GSTAmount { get; set; }
        public decimal Discount { get; set; }
        public decimal FreightExp { get; set; }
        public decimal OtherExp { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal NetAmount { get; set; }
        public string? Remarks { get; set; }
        public List<SaleItemViewModel> Items { get; set; } = [];
    }

    public class SaleItemViewModel
    {
        public long? ItemId { get; set; }
        public string? Desc { get; set; }
        public decimal Quantity { get; set; }
        public decimal SalePrice { get; set; }
        public decimal DiscPer { get; set; }
        public decimal DiscAmt { get; set; }
        public decimal Total { get; set; }
    }
}