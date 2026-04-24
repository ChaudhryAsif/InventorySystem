namespace InventorySystem.Models
{
    public class ConsumeInvoiceViewModel
    {
        public DateTime? ConsumeDate { get; set; }
        public int? BranchID { get; set; }
        public string? RefNo { get; set; }
        public string? Purpose { get; set; }
        public string? Remarks { get; set; }
        public List<ConsumeItemViewModel> Items { get; set; } = [];
    }

    public class ConsumeItemViewModel
    {
        public long? ItemId { get; set; }
        public string? Desc { get; set; }
        public decimal Quantity { get; set; }
    }
}