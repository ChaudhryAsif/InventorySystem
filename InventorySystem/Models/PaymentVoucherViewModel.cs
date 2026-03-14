namespace InventorySystem.Models
{
    public class PaymentVoucherViewModel
    {
        public int VoucherId { get; set; }          // 0 = new
        public int PartyId { get; set; }
        public string VoucherType { get; set; } = "Payment"; // "Payment" | "Receipt"
        public DateTime VoucherDate { get; set; }
        public decimal Amount { get; set; }
        public string? PaymentMode { get; set; }
        public string? ReferenceNo { get; set; }
        public int? PurchaseId { get; set; }
        public int? SaleId { get; set; }
        public int? BranchId { get; set; }
        public string? Notes { get; set; }
    }
}