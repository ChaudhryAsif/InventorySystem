using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Services.Services;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Tests;
public class PurchaseNeedsTests
{
    private static ApplicationDbContext Db()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.CostSheet.Add(new CostSheet { CostSheetId = 1, ItemName = "Boxes", Status = "Final", BranchID = 1 });
        db.Stock.AddRange(new Stock { ItemId = 10, BranchId = 1, Quantity = 300 }, new Stock { ItemId = 11, BranchId = 1, Quantity = 5000 });
        Order(db, 1, "Pending", 500, 0); Order(db, 2, "InProduction", 400, 100); Order(db, 3, "Completed", 100, 100); Order(db, 4, "Pending", 100, 0, closedShort: true);
        db.ProductionMaterial.AddRange(Mat(1, 10, "Paper", 0.5m), Mat(2, 10, "Paper", 1m), Mat(3, 10, "Paper", 9m), Mat(4, 10, "Paper", 9m),
            Mat(1, 11, "Glue", 2m, "pcs"));
        db.SaveChanges();
        return db;
    }
    private static void Order(ApplicationDbContext db, int id, string status, int qty, int produced, bool closedShort = false) =>
        db.ProductionOrder.Add(new ProductionOrder { ProductionOrderId = id, CostSheetId = 1, ProductName = "Box " + id, Status = status, OrderQty = qty, ProducedQty = produced,
            UsesEstimatedConsumption = true, ClosedShortUtc = closedShort ? DateTime.UtcNow : null });
    private static ProductionMaterial Mat(int order, long item, string name, decimal perBox, string unit = "kg") =>
        new() { ProductionOrderId = order, ItemId = item, ItemName = name, Unit = unit, KgPerUnit = perBox };

    [Fact] public async Task TotalsOpenOrdersRemainingBoxesAgainstStock()
    {
        var rows = await PurchaseNeedsService.BuildAsync(Db());
        var paper = rows.Single(r => r.ItemName == "Paper");
        Assert.Equal(550m, paper.Required);          // order 1: 500 x 0.5 = 250, order 2: 300 remaining x 1 = 300
        Assert.Equal(300m, paper.Available);
        Assert.Equal(250m, paper.ToBuy);
        Assert.Equal([1, 2], paper.Orders.Select(o => o.OrderId));
    }
    [Fact] public async Task IgnoresCompletedAndClosedShortOrdersAndShowsEnoughStock()
    {
        var rows = await PurchaseNeedsService.BuildAsync(Db());
        Assert.DoesNotContain(rows.SelectMany(r => r.Orders), o => o.OrderId is 3 or 4);
        var glue = rows.Single(r => r.ItemName == "Glue");
        Assert.Equal(1000m, glue.Required); Assert.Equal(0m, glue.ToBuy);
        Assert.Equal("Paper", rows[0].ItemName);       // shortfalls are listed first
    }
}
