using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Core.Models
{
    public class PurchaseInvoice
    {
        [Key]
        public int PurchaseId { get; set; }

        public DateTime? PurchaseDate { get; set; }

        [StringLength(20)]
        public string? VendorID { get; set; }

        public decimal? Discount { get; set; }

        public int? PaymentMode { get; set; }

        public decimal? TotalAmount { get; set; }

        public decimal? AmountPaid { get; set; }

        [StringLength(20)]
        public string? BillNo { get; set; }

        public bool? AddtoPrint { get; set; }

        public int? BranchID { get; set; }

        public int? UserNo { get; set; }

        [StringLength(500)]
        public string? Remarks { get; set; }

        public bool? IsAdjEnt { get; set; }

        public decimal? GSTPer { get; set; }

        public decimal? GSTAmount { get; set; }

        public decimal? FreightExp { get; set; }

        public decimal? OtherExp { get; set; }

        [StringLength(500)]
        public string? Location { get; set; }

        public ICollection<PurchaseInvoiceBody>? PurchaseInvoiceBodies { get; set; }
    }
}