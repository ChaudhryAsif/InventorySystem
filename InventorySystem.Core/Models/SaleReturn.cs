using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Core.Models
{
    [System.ComponentModel.DataAnnotations.Schema.Table("SaleReturn")]
    public class SaleReturn
    {
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

        public ICollection<SaleReturnBody>? SaleReturnBodies { get; set; }
    }
}