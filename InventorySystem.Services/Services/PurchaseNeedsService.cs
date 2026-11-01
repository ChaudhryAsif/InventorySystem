using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Services.Services;

public sealed record PurchaseNeedOrder(int OrderId, string? Product, int Boxes, decimal Quantity);
public sealed record PurchaseNeedRow(long ItemId, string ItemName, string Unit, decimal Required, decimal Available, decimal ToBuy, List<PurchaseNeedOrder> Orders);

// Materials still needed by open production orders, set against current stock. Read-only.
public static class PurchaseNeedsService
{
    public static async Task<List<PurchaseNeedRow>> BuildAsync(ApplicationDbContext db)
    {
        var lines = await db.ProductionMaterial.AsNoTracking()
            .Where(m => m.ProductionOrder != null && m.ProductionOrder.ClosedShortUtc == null && m.ProductionOrder.ReversedUtc == null &&
                (m.ProductionOrder.Status == "Pending" || m.ProductionOrder.Status == "InProduction" && m.ProductionOrder.UsesEstimatedConsumption))
            .Select(m => new { m.ItemId, m.ItemName, m.Unit, m.KgPerUnit, m.ProductionOrderId, m.ProductionOrder!.ProductName,
                m.ProductionOrder.BranchId, Boxes = m.ProductionOrder.OrderQty - m.ProductionOrder.ProducedQty }).ToListAsync();
        lines = lines.Where(l => l.Boxes > 0 && l.KgPerUnit > 0).ToList();
        var stock = await db.Stock.AsNoTracking().Where(s => lines.Select(l => l.ItemId).Contains(s.ItemId)).ToListAsync();
        return lines.GroupBy(l => l.ItemId).Select(g =>
        {
            var unit = g.First().Unit;
            var orders = g.Select(l => new PurchaseNeedOrder(l.ProductionOrderId, l.ProductName, l.Boxes, EstimatedProductionService.Required(l.KgPerUnit, l.Boxes, l.Unit)))
                .OrderBy(o => o.OrderId).ToList();
            var required = orders.Sum(o => o.Quantity);
            var branches = g.Select(l => l.BranchId).Distinct().ToList();
            var available = stock.Where(s => s.ItemId == g.Key && branches.Contains(s.BranchId)).Sum(s => s.Quantity);
            return new PurchaseNeedRow(g.Key, g.Select(l => l.ItemName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? $"Item {g.Key}", unit, required, available,
                Math.Max(0, required - available), orders);
        }).OrderByDescending(r => r.ToBuy).ThenBy(r => r.ItemName).ToList();
    }
}
