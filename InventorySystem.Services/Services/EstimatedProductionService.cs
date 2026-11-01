using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Services.Services;

public sealed class EstimatedProductionService(ApplicationDbContext db, IStockService stock)
{
    // kg rounds up to the gram; pcs rounds up to a whole piece.
    public static decimal Required(decimal qtyPerBox, int boxes, string unit = "kg") =>
        unit == "pcs" ? decimal.Ceiling(qtyPerBox * boxes) : decimal.Ceiling(qtyPerBox * boxes * 1000m) / 1000m;

    public static async Task<string> ValidateCustomerAsync(ApplicationDbContext db, string? customer)
    {
        if (!int.TryParse(customer, out var id) || id <= 0 || !await db.Parties.AnyAsync(p => p.PartyId == id &&
            p.Status == "active" && (p.PartyType == "customer" || p.PartyType == "both")))
            throw new InvalidOperationException("Select an active customer or a party marked as both for this production order.");
        return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task SnapshotAsync(ProductionOrder order, CostSheet sheet)
    {
        if (sheet.Status != "Final" || sheet.FinishedItemId is null)
            throw new InvalidOperationException("Finalize a cost sheet with a linked finished-box item first.");
        var customer = await ValidateCustomerAsync(db, order.CustomerId);
        var product = await db.Items.SingleOrDefaultAsync(i => i.ItemID == sheet.FinishedItemId && i.IsActive == true);
        if (product is not { ItemType: "FinishedGoods", StockUnit: "pcs" })
            throw new InvalidOperationException("Select an active finished-box item stocked in pieces.");
        if ((sheet.Plies ?? []).Any(p => p.KGs > 0 && p.ItemId == null))
            throw new InvalidOperationException("Link each paper layer to an inventory item before creating production.");
        if (sheet.HasLamination == true && !string.IsNullOrWhiteSpace(sheet.ClientCostingJson))
        {
            var costing = System.Text.Json.JsonSerializer.Deserialize<ClientCostingOptions>(sheet.ClientCostingJson);
            if (costing != null && (costing.GlueLaminationApplications > 0 && costing.GlueLaminationItemId == null ||
                costing.LaminationChemicalApplications > 0 && costing.LaminationChemicalItemId == null))
                throw new InvalidOperationException("Link the lamination materials before creating production.");
        }
        var estimates = (sheet.Plies ?? []).Where(p => p.ItemId.HasValue && p.KGs > 0)
            .Select(p => (Id: p.ItemId!.Value, Kg: p.KGs!.Value, Rate: p.Rate ?? 0)).ToList();
        void Add(long? id, decimal? kg, decimal? rate)
        {
            if (kg > 0 && id == null) throw new InvalidOperationException("Link each glue/silicate estimate to a stock item.");
            if (id.HasValue && kg > 0) estimates.Add((id.Value, kg.Value, rate ?? 0));
        }
        Add(sheet.GlueItemId, sheet.GlueKgPerBox, sheet.GlueRate);
        Add(sheet.SilicateItemId, sheet.SilicateKgPerBox, sheet.SilicateRate);
        foreach (var other in sheet.Materials ?? []) Add(other.ItemId, other.Quantity, other.Rate);
        if (estimates.Count == 0) throw new InvalidOperationException("Add estimated materials per box to the cost sheet.");
        var groups = estimates.GroupBy(e => e.Id).ToList();
        var items = await db.Items.Where(i => groups.Select(g => g.Key).Contains(i.ItemID)).ToListAsync();
        order.FinishedItemId = product.ItemID;
        order.ProductName = product.ItemName;
        order.CustomerId = customer;
        order.BranchId = sheet.BranchID ?? 1;
        order.EstimatedRate = sheet.FinalRateWithGST ?? 0;
        order.UsesEstimatedConsumption = true;
        foreach (var group in groups)
        {
            var item = items.SingleOrDefault(i => i.ItemID == group.Key);
            if (item is not { IsActive: true, ItemType: "RawMaterial", StockUnit: "kg" or "pcs" or "in" } || item.ItemID == product.ItemID)
                throw new InvalidOperationException("Every production material must be an active raw material stocked in kg or pcs.");
            var perBox = group.Sum(e => e.Kg);
            var available = await db.Stock.Where(s => s.ItemId == item.ItemID && s.BranchId == order.BranchId).SumAsync(s => (decimal?)s.Quantity) ?? 0;
            var required = Required(perBox, order.OrderQty, item.StockUnit);
            db.ProductionMaterial.Add(new ProductionMaterial { ProductionOrderId = order.ProductionOrderId,
                ItemId = item.ItemID, ItemName = item.ItemName, Unit = item.StockUnit, Specification = "Estimated from finalized cost sheet",
                KgPerUnit = perBox, RatePerKg = group.Sum(e => e.Kg * e.Rate) / perBox,
                RequiredKg = required, AvailableKg = available, ShortfallKg = Math.Max(0, required - available),
                StockStatus = available >= required ? "OK" : "Low" });
        }
    }

    private async Task<decimal> InvoicedAsync(int orderId)
        => await (from b in db.SaleInvoiceBody join s in db.SaleInvoice on b.SaleId equals s.SaleId
            where s.ProductionOrderId == orderId select b.Quantity ?? 0).SumAsync();

    // Sales use the oldest batches first: a batch has sold boxes unless the older active batches already cover every invoiced box.
    public static bool BatchHasSoldBoxes(IEnumerable<ProductionBatch> batches, ProductionBatch batch, decimal invoiced)
        => batches.Where(b => b.ReversedUtc == null && b.BatchNo < batch.BatchNo).Sum(b => (decimal)b.Quantity) < invoiced;

    private static void ValidateReason(string? reason, string what)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3 || reason.Length > 500)
            throw new InvalidOperationException($"Enter a {what} reason (3–500 characters).");
    }

    // Records one batch: deducts that quantity's estimated materials and adds the same number of finished boxes.
    public async Task RecordAsync(int id, int boxes, string? producedBy = null)
    {
        await using var tx = await db.Database.BeginAtomicTransactionAsync();
        var order = await db.ProductionOrder.Include(o => o.Materials).SingleOrDefaultAsync(o => o.ProductionOrderId == id)
            ?? throw new InvalidOperationException("Production order not found.");
        if (!order.UsesEstimatedConsumption || order.FinishedItemId == null)
            throw new InvalidOperationException("Edit and save this unstarted order to snapshot its cost sheet before recording production.");
        if (order.Status is not ("Pending" or "InProduction") || order.ReversedUtc != null || order.ClosedShortUtc != null)
            throw new InvalidOperationException("This order is already completed, closed or reversed. Create a new order for any further production.");
        var batchNo = (await db.ProductionBatch.Where(b => b.ProductionOrderId == id).MaxAsync(b => (int?)b.BatchNo) ?? 0) + 1;
        if (order.ProducedQty > 0 && batchNo == 1)
            throw new InvalidOperationException("This order was posted before batch production and cannot take more batches.");
        var remaining = order.OrderQty - order.ProducedQty;
        if (boxes <= 0 || boxes > remaining)
            throw new InvalidOperationException($"Produced boxes must be between 1 and the remaining quantity ({remaining}).");
        var output = await db.Items.SingleOrDefaultAsync(i => i.ItemID == order.FinishedItemId && i.IsActive == true);
        if (output is not { ItemType: "FinishedGoods", StockUnit: "pcs" }) throw new InvalidOperationException("The finished-box item is inactive or has invalid units.");
        var materials = order.Materials?.ToList() ?? [];
        if (materials.Count == 0) throw new InvalidOperationException("No saved material estimates exist for this order.");
        foreach (var group in materials.GroupBy(m => m.ItemId))
        {
            var unit = group.First().Unit;
            var required = Required(group.Sum(m => m.KgPerUnit), boxes, unit);
            if (!await db.Items.AnyAsync(i => i.ItemID == group.Key && i.IsActive == true && i.ItemType == "RawMaterial" && i.StockUnit == unit))
                throw new InvalidOperationException($"A saved material is inactive or no longer stocked in {unit}. Correct its item setup first.");
            if (required <= 0) throw new InvalidOperationException("Invalid material estimate.");
            var available = await db.Stock.Where(s => s.ItemId == group.Key && s.BranchId == order.BranchId).SumAsync(s => (decimal?)s.Quantity) ?? 0;
            var format = unit == "pcs" ? "N0" : "N3";
            if (available < required) throw new InvalidOperationException($"Insufficient {group.First().ItemName}: need {required.ToString(format)} {unit}; available {available.ToString(format)} {unit}.");
        }
        var batch = new ProductionBatch { ProductionOrderId = id, BatchNo = batchNo, Quantity = boxes, ProducedUtc = DateTime.UtcNow, ProducedBy = producedBy };
        db.ProductionBatch.Add(batch);
        await db.SaveChangesAsync();
        foreach (var group in materials.GroupBy(m => m.ItemId))
        {
            var unit = group.First().Unit;
            var required = Required(group.Sum(m => m.KgPerUnit), boxes, unit);
            await stock.AdjustAsync(group.Key, order.BranchId, -required);
            db.ProductionConsumption.Add(new ProductionConsumption { ProductionOrderId = id, BatchId = batch.BatchId, ItemId = group.Key,
                ItemName = group.First().ItemName, Unit = unit, PlannedKg = required, ActualKg = required,
                VarianceKg = 0, IsEstimated = true, ConsumedDate = DateTime.UtcNow });
        }
        await stock.AdjustAsync(order.FinishedItemId.Value, order.BranchId, boxes);
        order.ProducedQty += boxes;
        order.PostedUtc = DateTime.UtcNow;
        order.Status = order.ProducedQty >= order.OrderQty ? "Completed" : "InProduction";
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // Closes an order that will not reach its ordered quantity; boxes already produced stay in stock.
    public async Task CloseShortAsync(int id, string? reason)
    {
        ValidateReason(reason, "closing");
        await using var tx = await db.Database.BeginAtomicTransactionAsync();
        var order = await db.ProductionOrder.SingleOrDefaultAsync(o => o.ProductionOrderId == id)
            ?? throw new InvalidOperationException("Production order not found.");
        if (order.Status != "InProduction" || order.ProducedQty <= 0 || order.ProducedQty >= order.OrderQty || order.ClosedShortUtc != null)
            throw new InvalidOperationException("Only an order in production with boxes still remaining can be closed short.");
        order.Status = "Completed";
        order.ClosedShortUtc = DateTime.UtcNow;
        order.CloseReason = reason!.Trim();
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // Reverses the whole order: every batch that is still active.
    public async Task ReverseAsync(int id, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3 || reason.Length > 500)
            throw new InvalidOperationException("Enter a reversal reason (3–500 characters).");
        await using var tx = await db.Database.BeginAtomicTransactionAsync();
        var order = await db.ProductionOrder.SingleOrDefaultAsync(o => o.ProductionOrderId == id)
            ?? throw new InvalidOperationException("Production order not found.");
        if (!order.UsesEstimatedConsumption || order.Status is not ("Completed" or "InProduction") || order.ProducedQty <= 0 || order.PostedUtc == null || order.ReversedUtc != null || order.FinishedItemId == null)
            throw new InvalidOperationException("Only posted estimated production can be reversed, once.");
        if (await db.SaleInvoice.AnyAsync(s => s.ProductionOrderId == id))
            throw new InvalidOperationException("Cancel active sales linked to this order before reversing production.");
        var available = await db.Stock.Where(s => s.ItemId == order.FinishedItemId && s.BranchId == order.BranchId).SumAsync(s => (decimal?)s.Quantity) ?? 0;
        if (available < order.ProducedQty) throw new InvalidOperationException("Not enough finished stock to reverse. Resolve sales or adjustments first.");
        var batches = await db.ProductionBatch.Where(b => b.ProductionOrderId == id).ToListAsync();
        var reversedIds = batches.Where(b => b.ReversedUtc != null).Select(b => b.BatchId).ToHashSet();
        var consumption = (await db.ProductionConsumption.Where(c => c.ProductionOrderId == id && c.IsEstimated).ToListAsync())
            .Where(c => !c.BatchId.HasValue || !reversedIds.Contains(c.BatchId.Value)).ToList();
        if (consumption.Count == 0) throw new InvalidOperationException("No estimated stock movements to reverse.");
        await stock.AdjustAsync(order.FinishedItemId.Value, order.BranchId, -order.ProducedQty);
        foreach (var material in consumption) await stock.AdjustAsync(material.ItemId, order.BranchId, material.ActualKg);
        foreach (var batch in batches.Where(b => b.ReversedUtc == null)) { batch.ReversedUtc = DateTime.UtcNow; batch.ReversalReason = reason.Trim(); }
        order.Status = "Reversed";
        order.ReversedUtc = DateTime.UtcNow;
        order.ReversalReason = reason.Trim();
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // Reverses one batch: its boxes leave finished stock and its estimated materials come back.
    public async Task ReverseBatchAsync(int batchId, string? reason)
    {
        ValidateReason(reason, "reversal");
        await using var tx = await db.Database.BeginAtomicTransactionAsync();
        var batch = await db.ProductionBatch.SingleOrDefaultAsync(b => b.BatchId == batchId)
            ?? throw new InvalidOperationException("Batch not found.");
        if (batch.ReversedUtc != null) throw new InvalidOperationException("This batch is already reversed.");
        var order = await db.ProductionOrder.SingleAsync(o => o.ProductionOrderId == batch.ProductionOrderId);
        if (order.Status is not ("Completed" or "InProduction") || order.ReversedUtc != null || order.FinishedItemId == null)
            throw new InvalidOperationException("Only a batch of an active production order can be reversed.");
        var invoiced = await InvoicedAsync(order.ProductionOrderId);
        var orderBatches = await db.ProductionBatch.Where(b => b.ProductionOrderId == order.ProductionOrderId).ToListAsync();
        if (BatchHasSoldBoxes(orderBatches, batch, invoiced))
            throw new InvalidOperationException("Boxes from this batch are already invoiced. Cancel the invoice first.");
        var available = await db.Stock.Where(s => s.ItemId == order.FinishedItemId && s.BranchId == order.BranchId).SumAsync(s => (decimal?)s.Quantity) ?? 0;
        if (available < batch.Quantity) throw new InvalidOperationException("Not enough finished stock to reverse this batch. Resolve sales or adjustments first.");
        var consumption = await db.ProductionConsumption.Where(c => c.BatchId == batchId && c.IsEstimated).ToListAsync();
        if (consumption.Count == 0) throw new InvalidOperationException("No estimated stock movements to reverse.");
        await stock.AdjustAsync(order.FinishedItemId.Value, order.BranchId, -batch.Quantity);
        foreach (var material in consumption) await stock.AdjustAsync(material.ItemId, order.BranchId, material.ActualKg);
        batch.ReversedUtc = DateTime.UtcNow;
        batch.ReversalReason = reason!.Trim();
        order.ProducedQty -= batch.Quantity;
        order.ClosedShortUtc = null;
        order.CloseReason = null;
        if (order.ProducedQty <= 0) { order.Status = "Pending"; order.PostedUtc = null; }
        else order.Status = "InProduction";
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
