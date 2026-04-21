using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    [Table("PurchaseReturnBody")]
    public class PurchaseReturnBody
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Srno { get; set; }

        [ForeignKey(nameof(PurchaseReturn))]
        public int PurchaseReturnId { get; set; }

        public long? ItemId { get; set; }

        [StringLength(50)]
        public string? Descr { get; set; }

        public decimal? Quantity { get; set; }

        public decimal? PurPrice { get; set; }

        public decimal? DiscPer { get; set; }

        public decimal? DiscAmt { get; set; }

        public decimal? Total { get; set; }

        public PurchaseReturn? PurchaseReturn { get; set; }
    }
}