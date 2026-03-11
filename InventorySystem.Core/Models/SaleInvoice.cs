using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Core.Models
{
    [System.ComponentModel.DataAnnotations.Schema.Table("SaleInvoice")]
    public class SaleInvoice
    {
        [Key]
        public int SaleId { get; set; }

        public DateTime? SaleDate { get; set; }

        [StringLength(20)]
        public string? CustomerID { get; set; }

        public int? BranchID { get; set; }

        public int? PaymentMode { get; set; }

        [StringLength(20)]
        public string? RefNo { get; set; }

        public decimal? GSTPer { get; set; }

        public decimal? GSTAmount { get; set; }

        public decimal? Discount { get; set; }

        public decimal? FreightExp { get; set; }

        public decimal? OtherExp { get; set; }

        public decimal? TotalAmount { get; set; }

        public decimal? NetAmount { get; set; }

        [StringLength(500)]
        public string? Remarks { get; set; }

        public int? UserNo { get; set; }

        public ICollection<SaleInvoiceBody>? SaleInvoiceBodies { get; set; }
    }
}