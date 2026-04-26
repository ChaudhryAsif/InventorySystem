using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    [Table("CostSheet")]
    public class CostSheet
    {
        [Key]
        public int CostSheetId { get; set; }

        public DateTime? SheetDate { get; set; }
        public string? CustomerId { get; set; }   // FK → Party.PartyId (string)
        public string? ItemName { get; set; }
        public int? BranchID { get; set; }

        [StringLength(20)]
        public string? Status { get; set; } = "Draft"; // Draft / Final

        // ── Box Construction ──────────────────────────────────────────────
        [StringLength(100)]
        public string? BoxStyle { get; set; }   // RSC, FOL, etc.

        [StringLength(20)]
        public string? SizeType { get; set; }   // External / Internal

        [StringLength(10)]
        public string? SizeUnit { get; set; }   // mm / inches

        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? Flap { get; set; }
        public decimal? AutoFlapGap { get; set; }

        // ── Sheet Dimensions (in inches) ──────────────────────────────────
        public decimal? SheetWidthIn { get; set; }
        public decimal? SheetLengthIn { get; set; }
        public decimal? WithFlap { get; set; }
        public decimal? AdjWidthIn { get; set; }
        public decimal? AdjLength1In { get; set; }
        public decimal? AdjLength2In { get; set; }
        public decimal? AdjWidthMm { get; set; }
        public decimal? AdjLengthMm { get; set; }
        public decimal? TotalSheetSqIn { get; set; }

        // ── Printing ──────────────────────────────────────────────────────
        [StringLength(50)]
        public string? PrintingType { get; set; }
        public int? PrintingColors { get; set; }
        public decimal? PrintingCost { get; set; }

        // ── Die / Dickel ──────────────────────────────────────────────────
        public decimal? DickelCost { get; set; }

        // ── Lamination ────────────────────────────────────────────────────
        public bool? HasLamination { get; set; }
        public decimal? LaminationCost { get; set; }

        // ── Glue & Silicate ───────────────────────────────────────────────
        public decimal? GlueRate { get; set; }
        public decimal? SilicateRate { get; set; }
        public decimal? GlueSilicateKg { get; set; }
        public decimal? GlueSilicateCost { get; set; }

        // ── Calculated Paper Cost ─────────────────────────────────────────
        public decimal? PaperCost { get; set; }

        // ── Labour & Energy ───────────────────────────────────────────────
        public decimal? LabourRate { get; set; }
        public decimal? LabourCost { get; set; }
        public decimal? EnergyRate { get; set; }
        public decimal? EnergyCost { get; set; }

        // ── Binding ───────────────────────────────────────────────────────
        [StringLength(30)]
        public string? BindingType { get; set; }
        public int? BindingPins { get; set; }
        public decimal? BindingCost { get; set; }

        // ── Freight ───────────────────────────────────────────────────────
        public decimal? FreightRate { get; set; }
        public decimal? FreightCost { get; set; }

        // ── Sub-Total (Paper+Labour+Energy+Binding+Freight) ───────────────
        public decimal? SubTotal { get; set; }

        // ── Overhead Costs ────────────────────────────────────────────────
        public decimal? WastePct { get; set; }
        public decimal? WasteCost { get; set; }
        public decimal? AdminExpPct { get; set; }
        public decimal? AdminExpCost { get; set; }
        public decimal? SellingDistPct { get; set; }
        public decimal? SellingDistCost { get; set; }
        public decimal? RepairMaintPct { get; set; }
        public decimal? RepairMaintCost { get; set; }
        public decimal? StoreSparesPct { get; set; }
        public decimal? StoreSparesCost { get; set; }
        public decimal? MfgCostPct { get; set; }
        public decimal? MfgCostValue { get; set; }
        public decimal? ManufacturingTotal { get; set; }

        // ── Commission ────────────────────────────────────────────────────
        [StringLength(100)]
        public string? CommPersonA { get; set; }
        public decimal? CommPersonAPct { get; set; }
        public decimal? CommPersonACost { get; set; }

        [StringLength(100)]
        public string? CommPersonB { get; set; }
        public decimal? CommPersonBPct { get; set; }
        public decimal? CommPersonBCost { get; set; }

        // ── Final ─────────────────────────────────────────────────────────
        public decimal? WHTaxPct { get; set; }
        public decimal? GSTPercentage { get; set; }
        public decimal? ProfitPct { get; set; }
        public decimal? ProfitAmount { get; set; }
        public decimal? FinalCostWOGST { get; set; }
        public int? MOQ { get; set; }
        public decimal? TaxPct { get; set; }
        public decimal? FinalRateWithGST { get; set; }

        [StringLength(500)]
        public string? Remarks { get; set; }
        public DateTime? CreatedDate { get; set; }

        public ICollection<CostSheetPly>? Plies { get; set; }
    }
}