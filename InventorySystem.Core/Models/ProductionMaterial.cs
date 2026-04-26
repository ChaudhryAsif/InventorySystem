using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    /// <summary>
    /// Auto-generated requirement plan:
    /// CostSheetPly.KGs × OrderQty = RequiredKg
    /// Compared against live Stock to show Shortfall
    /// </summary>
    [Table("ProductionMaterial")]
    public class ProductionMaterial
    {
        [Key]
        public int MaterialId { get; set; }

        public int ProductionOrderId { get; set; }
        public long ItemId { get; set; }                // FK → Items

        [StringLength(150)]
        public string? ItemName { get; set; }

        [StringLength(100)]
        public string? Specification { get; set; }      // from CostSheetPly

        public decimal KgPerUnit { get; set; }          // CostSheetPly.KGs (per 1 box)
        public decimal RequiredKg { get; set; }         // KgPerUnit × OrderQty
        public decimal AvailableKg { get; set; }        // from Stock at time of planning
        public decimal ShortfallKg { get; set; }        // RequiredKg - AvailableKg (0 if sufficient)

        [StringLength(20)]
        public string StockStatus { get; set; } = "OK"; // OK / Low / Critical

        [ForeignKey(nameof(ProductionOrderId))]
        public ProductionOrder? ProductionOrder { get; set; }
    }
}