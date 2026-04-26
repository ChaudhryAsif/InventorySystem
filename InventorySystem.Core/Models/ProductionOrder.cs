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
    }
}