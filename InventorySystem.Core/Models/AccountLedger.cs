using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    /// <summary>
    /// Every financial movement in the system writes one row here.
    /// Supplier balance  = Sum(Credit) - Sum(Debit)   → positive = we owe them
    /// Customer balance  = Sum(Debit)  - Sum(Credit)  → positive = they owe us
    /// </summary>
    [Table("AccountLedger")]
    public class AccountLedger
    {
        [Key]
        public long LedgerId { get; set; }

        /// <summary>FK to Party (supplier or customer). Null for non-party entries.</summary>
        public int? PartyId { get; set; }

        public DateTime EntryDate { get; set; }

        /// <summary>Use <see cref="TransactionTypes"/> constants.</summary>
        [StringLength(50)]
        public string TransactionType { get; set; } = string.Empty;

        /// <summary>PurchaseId, SaleId, or VoucherId — depends on TransactionType.</summary>
        public int? ReferenceId { get; set; }

        [StringLength(300)]
        public string? Description { get; set; }

        /// <summary>Money paid OUT  / amount owed TO US  (customer invoice).</summary>
        public decimal Debit { get; set; }

        /// <summary>Money owed BY US / amount received FROM customer.</summary>
        public decimal Credit { get; set; }

        public int? BranchId { get; set; }

        [StringLength(500)]
        public string? Narration { get; set; }

        public DateTime CreatedDate { get; set; }

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        /// <summary>Soft-delete / reversal flag — never hard-delete ledger rows.</summary>
        public bool IsVoid { get; set; }

        [ForeignKey(nameof(PartyId))]
        public Party? Party { get; set; }
    }
}