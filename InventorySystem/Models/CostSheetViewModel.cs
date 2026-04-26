namespace InventorySystem.Models
{
    public class CostSheetViewModel
    {
        public int CostSheetId { get; set; }
        public DateTime? SheetDate { get; set; }
        public string? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? ItemName { get; set; }
        public int? BranchID { get; set; }
        public string? Status { get; set; }

        // Construction
        public string? BoxStyle { get; set; }
        public string? SizeType { get; set; }
        public string? SizeUnit { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? Flap { get; set; }
        public decimal? AutoFlapGap { get; set; }

        // Sheet dimensions
        public decimal? SheetWidthIn { get; set; }
        public decimal? SheetLengthIn { get; set; }
        public decimal? WithFlap { get; set; }
        public decimal? AdjWidthIn { get; set; }
        public decimal? AdjLength1In { get; set; }
        public decimal? AdjLength2In { get; set; }
        public decimal? AdjWidthMm { get; set; }
        public decimal? AdjLengthMm { get; set; }
        public decimal? TotalSheetSqIn { get; set; }

        // Printing
        public string? PrintingType { get; set; }
        public int? PrintingColors { get; set; }
        public decimal? PrintingCost { get; set; }

        // Dickel / Lamination / Glue
        public decimal? DickelCost { get; set; }
        public bool? HasLamination { get; set; }
        public decimal? LaminationCost { get; set; }
        public decimal? GlueRate { get; set; }
        public decimal? SilicateRate { get; set; }
        public decimal? GlueSilicateKg { get; set; }
        public decimal? GlueSilicateCost { get; set; }
        public decimal? PaperCost { get; set; }

        // Labour / Energy / Binding / Freight
        public decimal? LabourRate { get; set; }
        public decimal? LabourCost { get; set; }
        public decimal? EnergyRate { get; set; }
        public decimal? EnergyCost { get; set; }
        public string? BindingType { get; set; }
        public int? BindingPins { get; set; }
        public decimal? BindingCost { get; set; }
        public decimal? FreightRate { get; set; }
        public decimal? FreightCost { get; set; }
        public decimal? SubTotal { get; set; }

        // Overheads
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

        // Commission
        public string? CommPersonA { get; set; }
        public decimal? CommPersonAPct { get; set; }
        public decimal? CommPersonACost { get; set; }
        public string? CommPersonB { get; set; }
        public decimal? CommPersonBPct { get; set; }
        public decimal? CommPersonBCost { get; set; }

        // Final
        public decimal? WHTaxPct { get; set; }
        public decimal? GSTPercentage { get; set; }
        public decimal? ProfitPct { get; set; }
        public decimal? ProfitAmount { get; set; }
        public decimal? FinalCostWOGST { get; set; }
        public int? MOQ { get; set; }
        public decimal? TaxPct { get; set; }
        public decimal? FinalRateWithGST { get; set; }

        public string? Remarks { get; set; }
        public List<CostSheetPlyViewModel> Plies { get; set; } = [];
    }

    public class CostSheetPlyViewModel
    {
        public int SrNo { get; set; }
        public long? ItemId { get; set; }   // ADD THIS
        public string? PlyName { get; set; }
        public string? Specification { get; set; }
        public decimal? GSM { get; set; }
        public decimal? Rate { get; set; }
        public decimal? KGs { get; set; }
        public decimal? Cost { get; set; }
    }

    public class CostSheetSettingsViewModel
    {
        public decimal LabourRate { get; set; }
        public decimal EnergyRate { get; set; }
        public decimal WastePercentage { get; set; }
        public decimal AdminExpPercentage { get; set; }
        public decimal SellingDistPercentage { get; set; }
        public decimal RepairMaintenancePercentage { get; set; }
        public decimal StoreSparesPercentage { get; set; }
        public decimal ManufacturingCostPercentage { get; set; }
        public decimal DefaultFreightRate { get; set; }
        public decimal DefaultProfitPct { get; set; }
    }
}