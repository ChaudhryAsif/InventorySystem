using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    /// <summary>
    /// Records every manual cash movement between us and a party.
    /// VoucherType = "Payment"  → paying a supplier  (creates Debit in AccountLedger)
    /// VoucherType = "Receipt"  → receiving from customer (creates Credit in AccountLedger)
    /// </summary>
    [Table("PaymentVoucher")]
    public class PaymentVoucher
    {
        [Key]
        public int VoucherId { get; set; }

        public int PartyId { get; set; }

        [StringLength(20)]
        public string VoucherType { get; set; } = "Payment"; // "Payment" | "Receipt"

        public DateTime VoucherDate { get; set; }

        public decimal Amount { get; set; }

        [StringLength(50)]
        public string? PaymentMode { get; set; }  // Cash, Cheque, Bank Transfer

        [StringLength(100)]
        public string? ReferenceNo { get; set; }  // Cheque #, transaction ID

        public int? PurchaseId { get; set; }       // Optional: against a purchase invoice
        public int? SaleId { get; set; }           // Optional: against a sale invoice
        public int? PurchaseReturnId { get; set; } // Optional: against a purchase return
        public int? SaleReturnId { get; set; }     // Optional: against a sale return

        public int? BranchId { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        public DateTime CreatedDate { get; set; }

        public bool IsVoid { get; set; }

        [ForeignKey(nameof(PartyId))]
        public Party? Party { get; set; }
    }
}