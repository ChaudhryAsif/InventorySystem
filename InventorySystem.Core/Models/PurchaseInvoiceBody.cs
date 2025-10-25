using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    public class PurchaseInvoiceBody
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Srno { get; set; }

        [ForeignKey("PurchaseInvoice")]
        public int PurchaseId { get; set; }

        public long? Itemid { get; set; }

        [StringLength(50)]
        public string? Descr { get; set; }

        public decimal? Quantity { get; set; }

        public decimal? PurPrice { get; set; }

        public decimal? SalePrice { get; set; }

        public decimal? Discount { get; set; }

        [StringLength(20)]
        public string? SHORTKEY { get; set; }

        public DateTime? Expiry { get; set; }

        public decimal? DiscPer { get; set; }

        public decimal? DiscAmt { get; set; }

        [StringLength(50)]
        public string? Barcode { get; set; }

        public PurchaseInvoice? PurchaseInvoice { get; set; }
    }
}
