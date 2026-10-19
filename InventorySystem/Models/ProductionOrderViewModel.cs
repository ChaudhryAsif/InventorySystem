namespace InventorySystem.Models
{
    public class ProductionOrderViewModel
    {
        public int ProductionOrderId { get; set; }
        public int CostSheetId { get; set; }
        public string? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? ProductName { get; set; }
        public long? FinishedItemId { get; set; }
        public int OrderQty { get; set; }
        public decimal? AgreedUnitPrice { get; set; }
        public decimal AgreedGstPercent { get; set; }
        public string? CustomerReference { get; set; }
        public DateTime OrderDate { get; set; }
        public DateTime DeliveryDate { get; set; }
        public string Status { get; set; } = "Pending";
        public string? Remarks { get; set; }

        // Cost Sheet summary (for display)
        public string? BoxStyle { get; set; }
        public decimal? FinalRateWithGST { get; set; }
        public decimal? TotalOrderValue => AgreedUnitPrice.HasValue ? OrderPricing.Total(OrderQty, AgreedUnitPrice.Value, AgreedGstPercent) : null;
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

    public class RecordProductionRequest
    {
        public int ProductionOrderId { get; set; }
        public int ProducedQty { get; set; }
        public string? Reason { get; set; }
    }

    public class SaveConsumptionRequest
    {
        public int ProductionOrderId { get; set; }
        public List<ProductionConsumptionViewModel> Items { get; set; } = [];
    }
}
