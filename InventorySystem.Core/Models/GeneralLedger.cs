using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    /// <summary>
    /// Double-entry general ledger.
    /// Every voucher posts TWO rows: one Debit, one Credit.
    /// Sum(Debit) must always equal Sum(Credit) per VoucherId.
    /// </summary>
    [Table("GeneralLedger")]
    public class GeneralLedger
    {
        [Key]
        public long GLId { get; set; }

        [Required]
        public string VoucherNo { get; set; } = string.Empty;    // e.g. "PV-2024-0001"

        /// <summary>PV | RV | JV | CV</summary>
        [Required, StringLength(10)]
        public string VoucherType { get; set; } = string.Empty;

        public DateTime VoucherDate { get; set; }

        public int AccountHeadId { get; set; }

        /// <summary>Optional: link to a party (supplier/customer).</summary>
        public int? PartyId { get; set; }

        public decimal Debit { get; set; } = 0;
        public decimal Credit { get; set; } = 0;

        [StringLength(500)]
        public string? Narration { get; set; }

        /// <summary>Links back to the originating voucher record.</summary>
        public int? VoucherId { get; set; }

        public int? BranchId { get; set; }

        public bool IsVoid { get; set; } = false;

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Navigation
        [ForeignKey(nameof(AccountHeadId))]
        public AccountHead? AccountHead { get; set; }

        [ForeignKey(nameof(PartyId))]
        public Party? Party { get; set; }
    }
}