using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    [Table("StockAdjustment")]
    public class StockAdjustment
    {
        [Key]
        public long AdjustmentId { get; set; }

        [Required]
        [StringLength(20)]
        public string? AdjustmentNo { get; set; }

        [Required]
        public DateTime AdjustmentDate { get; set; }

        [Required]
        public int BranchId { get; set; }

        [StringLength(20)]
        public string? AdjustmentType { get; set; } // "Opening Stock", "Physical Count", "Correction", etc.

        [StringLength(500)]
        public string? Remarks { get; set; }

        public int? CreatedBy { get; set; }

        public DateTime CreatedDate { get; set; }

        [StringLength(20)]
        public string? Status { get; set; } // "Draft", "Posted"

        public ICollection<StockAdjustmentDetail>? Details { get; set; }
    }

    [Table("StockAdjustmentDetail")]
    public class StockAdjustmentDetail
    {
        [Key]
        public long DetailId { get; set; }

        [ForeignKey("StockAdjustment")]
        public long AdjustmentId { get; set; }

        public long ItemId { get; set; }

        [StringLength(100)]
        public string? Description { get; set; }

        public decimal Quantity { get; set; }

        public decimal? UnitCost { get; set; }

        public decimal? TotalCost { get; set; }

        [StringLength(200)]
        public string? Reason { get; set; }

        public StockAdjustment? StockAdjustment { get; set; }

        [ForeignKey(nameof(ItemId))]
        public Items? Item { get; set; }
    }
}