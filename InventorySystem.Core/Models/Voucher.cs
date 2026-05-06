using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    /// <summary>
    /// Master record for ALL voucher types:
    /// PV = Payment Voucher  (cash out to supplier)
    /// RV = Receipt Voucher  (cash in from customer)
    /// JV = Journal Voucher  (adjustments, depreciation, accruals)
    /// CV = Contra Voucher   (cash to bank, bank to cash)
    /// </summary>
    [Table("Vouchers")]
    public class Voucher
    {
        [Key]
        public int VoucherId { get; set; }

        [Required, StringLength(20)]
        public string VoucherNo { get; set; } = string.Empty;

        /// <summary>PV | RV | JV | CV</summary>
        [Required, StringLength(5)]
        public string VoucherType { get; set; } = string.Empty;

        public DateTime VoucherDate { get; set; }

        public decimal TotalAmount { get; set; }

        /// <summary>Optional: link to a party.</summary>
        public int? PartyId { get; set; }

        [StringLength(50)]
        public string? PaymentMode { get; set; }   // Cash | Cheque | Bank Transfer | Online

        [StringLength(100)]
        public string? ReferenceNo { get; set; }   // cheque#, bank txn#

        [StringLength(500)]
        public string? Narration { get; set; }

        /// <summary>Draft | Posted | Void</summary>
        [StringLength(20)]
        public string Status { get; set; } = "Posted";

        public int? BranchId { get; set; }

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public bool IsVoid { get; set; } = false;

        // Navigation
        [ForeignKey(nameof(PartyId))]
        public Party? Party { get; set; }

        public ICollection<VoucherDetail> Details { get; set; } = new List<VoucherDetail>();
    }
}