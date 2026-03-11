using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    [Table("Stock")]
    public class Stock
    {
        [Key]
        public long StockId { get; set; }

        public long ItemId { get; set; }

        public int BranchId { get; set; }

        public decimal Quantity { get; set; }

        public DateTime LastUpdated { get; set; }

        [ForeignKey(nameof(ItemId))]
        public Items? Item { get; set; }
    }
}