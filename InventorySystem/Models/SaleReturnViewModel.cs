namespace InventorySystem.Models
{
    public class SaleReturnViewModel
    {
        public DateTime? ReturnDate { get; set; }
        public string? CustomerID { get; set; }
        public int? OriginalSaleId { get; set; }
        public int? BranchID { get; set; }
        public int? PaymentMode { get; set; }
        public string? RefNo { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal NetAmount { get; set; }
        public string? Remarks { get; set; }
        public List<SaleReturnItemViewModel> Items { get; set; } = [];
    }

    public class SaleReturnItemViewModel
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