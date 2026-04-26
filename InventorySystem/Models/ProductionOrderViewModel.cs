namespace InventorySystem.Models
{
    public class ProductionOrderViewModel
    {
        public int ProductionOrderId { get; set; }
        public int CostSheetId { get; set; }
        public string? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? ProductName { get; set; }
        public int OrderQty { get; set; }
        public DateTime OrderDate { get; set; }
        public DateTime DeliveryDate { get; set; }
        public string Status { get; set; } = "Pending";
        public string? Remarks { get; set; }

        // Cost Sheet summary (for display)
        public string? BoxStyle { get; set; }
        public decimal? FinalRateWithGST { get; set; }
        public decimal? TotalOrderValue => OrderQty * (FinalRateWithGST ?? 0);
    }

    public class ProductionMaterialViewModel
    {
        public int MaterialId { get; set; }
        public long ItemId { get; set; }
        public string? ItemName { get; set; }
        public string? Specification { get; set; }
        public decimal KgPerUnit { get; set; }
        public decimal RequiredKg { get; set; }
        public decimal AvailableKg { get; set; }
        public decimal ShortfallKg { get; set; }
        public string StockStatus { get; set; } = "OK";
    }

    public class ProductionConsumptionViewModel
    {
        public long ItemId { get; set; }
        public string? ItemName { get; set; }
        public decimal PlannedKg { get; set; }
        public decimal ActualKg { get; set; }
    }

    public class SaveConsumptionRequest
    {
        public int ProductionOrderId { get; set; }
        public List<ProductionConsumptionViewModel> Items { get; set; } = [];
    }
}