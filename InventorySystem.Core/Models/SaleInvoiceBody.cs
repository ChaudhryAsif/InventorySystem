using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    [Table("SaleInvoiceBody")]
    public class SaleInvoiceBody
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Srno { get; set; }

        [ForeignKey(nameof(SaleInvoice))]
        public int SaleId { get; set; }

        public long? ItemId { get; set; }

        [StringLength(50)]
        public string? Descr { get; set; }

        public decimal? Quantity { get; set; }

        public decimal? SalePrice { get; set; }

        public decimal? DiscPer { get; set; }

        public decimal? DiscAmt { get; set; }

        public decimal? Total { get; set; }

        public SaleInvoice? SaleInvoice { get; set; }
    }
}