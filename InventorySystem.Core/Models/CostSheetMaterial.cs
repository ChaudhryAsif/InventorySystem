using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    // Extra raw material per box (kg or pcs), e.g. packing tape. Not part of box weight.
    [Table("CostSheetMaterial")]
    public class CostSheetMaterial
    {
        [Key]
        public int CostSheetMaterialId { get; set; }

        public int CostSheetId { get; set; }
        public int SrNo { get; set; }
        public long ItemId { get; set; }

        [StringLength(10)]
        public string Unit { get; set; } = "pcs";

        public decimal Quantity { get; set; }   // per ONE box, in Unit
        public decimal Rate { get; set; }       // per Unit
        public decimal Cost { get; set; }       // Quantity x Rate

        [ForeignKey(nameof(CostSheetId))]
        public CostSheet? CostSheet { get; set; }
    }
}
