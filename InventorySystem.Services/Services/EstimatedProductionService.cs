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
            if (item is not { IsActive: true, ItemType: "RawMaterial", StockUnit: "kg" or "pcs" } || item.ItemID == product.ItemID)
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

    public async Task RecordAsync(int id, int boxes)
    {
        await using var tx = await db.Database.BeginAtomicTransactionAsync();
        var order = await db.ProductionOrder.Include(o => o.Materials).SingleOrDefaultAsync(o => o.ProductionOrderId == id)
            ?? throw new InvalidOperationException("Production order not found.");
        if (!order.UsesEstimatedConsumption || order.FinishedItemId == null)
            throw new InvalidOperationException("Edit and save this unstarted order to snapshot its cost sheet before recording production.");
        if (order.Status != "Pending" || order.PostedUtc != null || order.ProducedQty > 0 || await db.ProductionConsumption.AnyAsync(c => c.ProductionOrderId == id))
            throw new InvalidOperationException("This order is already posted or closed. Production can be recorded only once.");
        if (boxes <= 0 || boxes > order.OrderQty) throw new InvalidOperationException("Produced boxes must be between 1 and the order quantity. This entry closes the order.");
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
            if (available < required) throw new InvalidOperationException($"Insufficient {group.First().ItemName}: need {required.ToString(format)} {unit}; available {available.ToString(format)} {unit}. No stock was changed.");
        }
        foreach (var group in materials.GroupBy(m => m.ItemId))
        {
            var unit = group.First().Unit;
            var required = Required(group.Sum(m => m.KgPerUnit), boxes, unit);
            await stock.AdjustAsync(group.Key, order.BranchId, -required);
            db.ProductionConsumption.Add(new ProductionConsumption { ProductionOrderId = id, ItemId = group.Key,
                ItemName = group.First().ItemName, Unit = unit, PlannedKg = required, ActualKg = required,
                VarianceKg = 0, IsEstimated = true, ConsumedDate = DateTime.UtcNow });
        }
        await stock.AdjustAsync(order.FinishedItemId.Value, order.BranchId, boxes);
        order.ProducedQty = boxes;
        order.PostedUtc = DateTime.UtcNow;
        order.Status = "Completed";
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task ReverseAsync(int id, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3 || reason.Length > 500)
            throw new InvalidOperationException("Enter a reversal reason (3–500 characters).");
        await using var tx = await db.Database.BeginAtomicTransactionAsync();
        var order = await db.ProductionOrder.SingleOrDefaultAsync(o => o.ProductionOrderId == id)
            ?? throw new InvalidOperationException("Production order not found.");
        if (!order.UsesEstimatedConsumption || order.Status != "Completed" || order.PostedUtc == null || order.ReversedUtc != null || order.FinishedItemId == null)
            throw new InvalidOperationException("Only posted estimated production can be reversed, once.");
        if (await db.SaleInvoice.AnyAsync(s => s.ProductionOrderId == id))
            throw new InvalidOperationException("Cancel active sales linked to this order before reversing production.");
        var available = await db.Stock.Where(s => s.ItemId == order.FinishedItemId && s.BranchId == order.BranchId).SumAsync(s => (decimal?)s.Quantity) ?? 0;
        if (available < order.ProducedQty) throw new InvalidOperationException("Not enough finished stock to reverse. Resolve sales or adjustments first.");
        var consumption = await db.ProductionConsumption.Where(c => c.ProductionOrderId == id && c.IsEstimated).ToListAsync();
        if (consumption.Count == 0) throw new InvalidOperationException("No estimated stock movements to reverse.");
        await stock.AdjustAsync(order.FinishedItemId.Value, order.BranchId, -order.ProducedQty);
        foreach (var material in consumption) await stock.AdjustAsync(material.ItemId, order.BranchId, material.ActualKg);
        order.Status = "Reversed";
        order.ReversedUtc = DateTime.UtcNow;
        order.ReversalReason = reason.Trim();
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
