using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    [Table("ConsumeInvoiceBody")]
    public class ConsumeInvoiceBody
    {
        [Key]
        public int Srno { get; set; }

        public int ConsumeId { get; set; }

        public long? ItemId { get; set; }

        [StringLength(300)]
        public string? Descr { get; set; }

        public decimal Quantity { get; set; }

        [ForeignKey(nameof(ConsumeId))]
        public ConsumeInvoice? ConsumeInvoice { get; set; }
    }
}