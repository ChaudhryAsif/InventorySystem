using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    /// <summary>
    /// Line items for a Voucher — each line is one side of a double-entry.
    /// </summary>
    [Table("VoucherDetail")]
    public class VoucherDetail
    {
        [Key]
        public int VoucherDetailId { get; set; }

        public int VoucherId { get; set; }

        public int AccountHeadId { get; set; }

        public int? PartyId { get; set; }

        public decimal Debit { get; set; } = 0;
        public decimal Credit { get; set; } = 0;

        [StringLength(500)]
        public string? Narration { get; set; }

        // Navigation
        [ForeignKey(nameof(VoucherId))]
        public Voucher? Voucher { get; set; }

        [ForeignKey(nameof(AccountHeadId))]
        public AccountHead? AccountHead { get; set; }

        [ForeignKey(nameof(PartyId))]
        public Party? Party { get; set; }
    }
}