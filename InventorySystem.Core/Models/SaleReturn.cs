using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Core.Models
{
    [System.ComponentModel.DataAnnotations.Schema.Table("SaleReturn")]
    public class SaleReturn
    {
        public bool IsCancelled { get; set; }
        public DateTime? CancelledUtc { get; set; }
        public string? CancelledBy { get; set; }
        [System.ComponentModel.DataAnnotations.MaxLength(500)] public string? CancellationReason { get; set; }

        [Key]
        public int SaleReturnId { get; set; }

        public DateTime? ReturnDate { get; set; }

        [StringLength(20)]
        public string? CustomerID { get; set; }

        public int? OriginalSaleId { get; set; }

        public int? BranchID { get; set; }

        public int? PaymentMode { get; set; }

        [StringLength(20)]
        public string? RefNo { get; set; }

        public decimal? TotalAmount { get; set; }

        public decimal? NetAmount { get; set; }

        [StringLength(500)]
        public string? Remarks { get; set; }

        public int? UserNo { get; set; }

        /// <summary>Voucher posted to the double-entry ledger for this return, if PostToGeneralLedger is enabled.</summary>
        public int? GLVoucherId { get; set; }

        public ICollection<SaleReturnBody>? SaleReturnBodies { get; set; }
    }
}