using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    [Table("CostSheetPly")]
    public class CostSheetPly
    {
        [Key]
        public int PlyId { get; set; }

        public int CostSheetId { get; set; }
        public int SrNo { get; set; }

        // ── NEW: link to Items table ──────────────────────────────────────
        public long? ItemId { get; set; }          // FK → Items.ItemID

        [StringLength(100)]
        public string? PlyName { get; set; } // Top Layer, Layer 2 (F)…

        [StringLength(100)]
        public string? Specification { get; set; } // Coated, CMP, Kraft…

        public decimal? GSM { get; set; }  // paper quality — informational only
        public decimal? Rate { get; set; }  // price per KG (from last purchase)
        public decimal? KGs { get; set; }  // weight used
        public decimal? Cost { get; set; }  // KGs × Rate (auto-calculated)

        [ForeignKey(nameof(CostSheetId))]
        public CostSheet? CostSheet { get; set; }
    }
}