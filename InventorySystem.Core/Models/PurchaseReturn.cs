using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Core.Models
{
    [System.ComponentModel.DataAnnotations.Schema.Table("PurchaseReturn")]
    public class PurchaseReturn
    {
        [Key]
        public int PurchaseReturnId { get; set; }

        public DateTime? ReturnDate { get; set; }

        [StringLength(20)]
        public string? VendorID { get; set; }

        public int? OriginalPurchaseId { get; set; }

        public int? BranchID { get; set; }

        public int? PaymentMode { get; set; }

        [StringLength(20)]
        public string? RefNo { get; set; }

        public decimal? TotalAmount { get; set; }

        public decimal? NetAmount { get; set; }

        [StringLength(500)]
        public string? Remarks { get; set; }

        public int? UserNo { get; set; }

        public ICollection<PurchaseReturnBody>? PurchaseReturnBodies { get; set; }
    }
}