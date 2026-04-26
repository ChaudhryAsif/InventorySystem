using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    [Table("CostSheetSettings")]
    public class CostSheetSettings
    {
        [Key]
        public int Id { get; set; }

        // ── Labour & Energy ───────────────────────────────────────────────
        public decimal LabourRate { get; set; } = 5m;
        public decimal EnergyRate { get; set; } = 5.08m;

        // ── Overhead Percentages (applied on Sub-Total) ───────────────────
        public decimal WastePercentage { get; set; } = 6m;
        public decimal AdminExpPercentage { get; set; } = 1.52m;
        public decimal SellingDistPercentage { get; set; } = 1m;
        public decimal RepairMaintenancePercentage { get; set; } = 1.5m;
        public decimal StoreSparesPercentage { get; set; } = 1.5m;
        public decimal ManufacturingCostPercentage { get; set; } = 1.72m;

        // ── Defaults ──────────────────────────────────────────────────────
        public decimal DefaultFreightRate { get; set; } = 0.5m;
        public decimal DefaultProfitPct { get; set; } = 11m;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}