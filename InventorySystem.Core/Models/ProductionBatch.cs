using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    /// <summary>One recorded part of a production order; recording it moves stock for that quantity only.</summary>
    [Table("ProductionBatch")]
    public class ProductionBatch
    {
        [Key]
        public int BatchId { get; set; }
        public int ProductionOrderId { get; set; }
        /// <summary>1, 2, 3... within the order.</summary>
        public int BatchNo { get; set; }
        public int Quantity { get; set; }
        public DateTime ProducedUtc { get; set; } = DateTime.UtcNow;
        [StringLength(100)] public string? ProducedBy { get; set; }
        public DateTime? ReversedUtc { get; set; }
        [StringLength(500)] public string? ReversalReason { get; set; }

        [ForeignKey(nameof(ProductionOrderId))]
        public ProductionOrder? ProductionOrder { get; set; }
    }
}
