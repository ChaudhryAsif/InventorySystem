using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    /// <summary>
    /// Records actual raw material consumed during production.
    /// Deducts from Stock when saved.
    /// </summary>
    [Table("ProductionConsumption")]
    public class ProductionConsumption
    {
        [Key]
        public int ConsumptionId { get; set; }

        public int ProductionOrderId { get; set; }
        public long ItemId { get; set; }

        [StringLength(150)]
        public string? ItemName { get; set; }

        public decimal PlannedKg { get; set; }    // from ProductionMaterial.RequiredKg
        public decimal ActualKg { get; set; }     // entered by user
        public decimal VarianceKg { get; set; }   // ActualKg - PlannedKg

        public DateTime ConsumedDate { get; set; } = DateTime.Now;

        [ForeignKey(nameof(ProductionOrderId))]
        public ProductionOrder? ProductionOrder { get; set; }
    }
}