using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    [Table("ProductionOrder")]
    public class ProductionOrder
    {
        [Key]
        public int ProductionOrderId { get; set; }

        public int CostSheetId { get; set; }           // FK → CostSheet
        public string? CustomerId { get; set; }         // FK → Party
        public string? ProductName { get; set; }        // from CostSheet.ItemName

        public long? FinishedItemId { get; set; }
        public int BranchId { get; set; } = 1;
        public decimal EstimatedRate { get; set; }
        /// <summary>Confirmed selling rate per box, excluding GST. Null for legacy orders without an agreement.</summary>
        [Column(TypeName = "decimal(18,2)")] public decimal? AgreedUnitPrice { get; set; }
        [Column(TypeName = "decimal(5,2)")] public decimal AgreedGstPercent { get; set; }
        [StringLength(100)] public string? CustomerReference { get; set; }
        public int ProducedQty { get; set; }
        public bool UsesEstimatedConsumption { get; set; }
        public DateTime? PostedUtc { get; set; }
        public DateTime? ReversedUtc { get; set; }
        /// <summary>Set when the order is closed with less than the ordered quantity produced.</summary>
        public DateTime? ClosedShortUtc { get; set; }
        [StringLength(500)] public string? CloseReason { get; set; }
        [StringLength(500)] public string? ReversalReason { get; set; }
        [Timestamp] public byte[] RowVersion { get; set; } = [];
        public int OrderQty { get; set; }               // how many boxes ordered
        public DateTime OrderDate { get; set; }
        public DateTime DeliveryDate { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Pending"; // Pending / InProduction / Completed / Cancelled

        [StringLength(500)]
        public string? Remarks { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [ForeignKey(nameof(CostSheetId))]
        public CostSheet? CostSheet { get; set; }

        public ICollection<ProductionMaterial>? Materials { get; set; }
        public ICollection<ProductionConsumption>? Consumptions { get; set; }
        public ICollection<ProductionBatch>? Batches { get; set; }
    }
}
