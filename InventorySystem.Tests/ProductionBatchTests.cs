using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace InventorySystem.Tests;

public class ProductionBatchTests
{
    private static ApplicationDbContext Db()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.Database.EnsureCreated();
        db.ItemCategory.Add(new ItemCategory { CategoryId = 800, CategoryName = "Test", IsActive = true });
        db.Items.AddRange(new Items { ItemID = 800, CategoryID = 800, ItemName = "Boxes", ItemType = "FinishedGoods", StockUnit = "pcs", IsActive = true },
            new Items { ItemID = 801, CategoryID = 800, ItemName = "Paper", ItemType = "RawMaterial", StockUnit = "kg", IsActive = true });
        db.Parties.Add(new Party { PartyId = 800, PartyName = "Customer", PartyType = "customer", Status = "active" });
        db.CostSheet.Add(new CostSheet { CostSheetId = 1, ItemName = "Boxes", FinishedItemId = 800, CustomerId = "800", Status = "Final", BranchID = 1 });
        db.ProductionOrder.Add(new ProductionOrder { ProductionOrderId = 800, CostSheetId = 1, CustomerId = "800", FinishedItemId = 800, ProductName = "Boxes",
            BranchId = 1, Status = "Pending", UsesEstimatedConsumption = true, OrderQty = 500, AgreedUnitPrice = 20, AgreedGstPercent = 0, OrderDate = DateTime.Today, DeliveryDate = DateTime.Today });
        db.ProductionMaterial.Add(new ProductionMaterial { ProductionOrderId = 800, ItemId = 801, ItemName = "Paper", Unit = "kg", KgPerUnit = 0.5m });
        db.Stock.Add(new Stock { ItemId = 801, BranchId = 1, Quantity = 1000 });
        db.SaveChanges();
        return db;
    }
    private static EstimatedProductionService Service(ApplicationDbContext db) => new(db, new StockService(db));
    private static async Task<decimal> Qty(ApplicationDbContext db, long item) => (await db.Stock.SingleAsync(s => s.ItemId == item)).Quantity;
    private static void Sold(ApplicationDbContext db, decimal quantity, bool cancelled = false)
    {
        var sale = new SaleInvoice { ProductionOrderId = 800, IsCancelled = cancelled, SaleDate = DateTime.Today };
        db.SaleInvoice.Add(sale); db.SaveChanges();
        db.SaleInvoiceBody.Add(new SaleInvoiceBody { SaleId = sale.SaleId, ItemId = 800, Quantity = quantity }); db.SaveChanges();
    }

    [Fact]
    public async Task OneOrderIsRecordedInSeveralBatchesUntilFull()
    {
        using var db = Db(); var service = Service(db);
        await service.RecordAsync(800, 100, "QA");
        var order = await db.ProductionOrder.SingleAsync();
        Assert.Equal("InProduction", order.Status); Assert.Equal(100, order.ProducedQty);
        Assert.Equal(100, await Qty(db, 800)); Assert.Equal(950, await Qty(db, 801));
        await service.RecordAsync(800, 100);
        Assert.Equal(200, order.ProducedQty); Assert.Equal(200, await Qty(db, 800)); Assert.Equal(900, await Qty(db, 801));
        Assert.Equal([1, 2], await db.ProductionBatch.OrderBy(b => b.BatchNo).Select(b => b.BatchNo).ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordAsync(800, 301));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordAsync(800, 0));
        await service.RecordAsync(800, 300);
        Assert.Equal("Completed", order.Status); Assert.Equal(500, order.ProducedQty); Assert.Equal(500, await Qty(db, 800)); Assert.Equal(750, await Qty(db, 801));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordAsync(800, 1));
        Assert.Equal(3, await db.ProductionConsumption.CountAsync(c => c.BatchId != null));
    }

    [Fact]
    public async Task ShortageOnALaterBatchBlocksOnlyThatBatch()
    {
        using var db = Db(); var service = Service(db);
        db.Stock.Single(s => s.ItemId == 801).Quantity = 120; await db.SaveChangesAsync();
        await service.RecordAsync(800, 200);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordAsync(800, 100));
        Assert.Equal(200, (await db.ProductionOrder.SingleAsync()).ProducedQty); Assert.Equal(20, await Qty(db, 801));
        Assert.Single(await db.ProductionBatch.ToListAsync());
    }

    [Fact]
    public async Task ClosingShortKeepsProducedBoxesAndStopsMoreBatches()
    {
        using var db = Db(); var service = Service(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CloseShortAsync(800, "Customer cancelled the rest"));
        await service.RecordAsync(800, 200);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CloseShortAsync(800, " "));
        await service.CloseShortAsync(800, "Customer cancelled the rest");
        var order = await db.ProductionOrder.SingleAsync();
        Assert.Equal("Completed", order.Status); Assert.NotNull(order.ClosedShortUtc); Assert.Equal(200, await Qty(db, 800));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordAsync(800, 50));
    }

    [Fact]
    public async Task ReversingABatchRestoresItsStockAndReopensTheOrder()
    {
        using var db = Db(); var service = Service(db);
        await service.RecordAsync(800, 100); await service.RecordAsync(800, 400);
        var order = await db.ProductionOrder.SingleAsync(); Assert.Equal("Completed", order.Status);
        var second = await db.ProductionBatch.SingleAsync(b => b.BatchNo == 2);
        await service.ReverseBatchAsync(second.BatchId, "Counted wrong");
        Assert.Equal("InProduction", order.Status); Assert.Equal(100, order.ProducedQty);
        Assert.Equal(100, await Qty(db, 800)); Assert.Equal(950, await Qty(db, 801));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReverseBatchAsync(second.BatchId, "Again"));
        await service.RecordAsync(800, 400);
        Assert.Equal("Completed", order.Status); Assert.Equal(500, order.ProducedQty);
    }

    [Fact]
    public async Task ABatchAlreadyInvoicedCannotBeReversedUntilTheInvoiceIsCancelled()
    {
        using var db = Db(); var service = Service(db);
        await service.RecordAsync(800, 100); await service.RecordAsync(800, 100);
        var first = await db.ProductionBatch.SingleAsync(b => b.BatchNo == 1);
        Sold(db, 150);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReverseBatchAsync(first.BatchId, "Wrong"));
        (await db.SaleInvoice.SingleAsync()).IsCancelled = true; await db.SaveChangesAsync();
        await service.ReverseBatchAsync(first.BatchId, "Wrong");
        Assert.Equal(100, (await db.ProductionOrder.SingleAsync()).ProducedQty);
    }

    [Theory]
    [InlineData(0, true, true, true)]
    [InlineData(100, false, true, true)]
    [InlineData(156, false, false, true)]
    [InlineData(250, false, false, true)]
    [InlineData(251, false, false, false)]
    public async Task SalesUseTheOldestBatchesSoOnlyLaterBatchesCanBeReversed(int sold, bool first, bool second, bool third)
    {
        using var db = Db(); var service = Service(db);
        await service.RecordAsync(800, 100); await service.RecordAsync(800, 150); await service.RecordAsync(800, 250);
        if (sold > 0) Sold(db, sold);
        var batches = await db.ProductionBatch.OrderBy(b => b.BatchNo).ToListAsync();
        var shown = batches.Select(b => !EstimatedProductionService.BatchHasSoldBoxes(batches, b, sold)).ToArray();
        Assert.Equal([first, second, third], shown);
        for (var i = 0; i < 3; i++)
        {
            if (shown[i]) continue;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReverseBatchAsync(batches[i].BatchId, "Wrong"));
        }
        if (shown[2]) { await service.ReverseBatchAsync(batches[2].BatchId, "Wrong"); Assert.Equal(250, (await db.ProductionOrder.SingleAsync()).ProducedQty); }
    }

    [Fact]
    public async Task ReversingTheWholeOrderUndoesEveryActiveBatch()
    {
        using var db = Db(); var service = Service(db);
        await service.RecordAsync(800, 100); await service.RecordAsync(800, 150);
        var first = await db.ProductionBatch.SingleAsync(b => b.BatchNo == 1);
        await service.ReverseBatchAsync(first.BatchId, "Counted wrong");
        await service.ReverseAsync(800, "Order dropped");
        Assert.Equal("Reversed", (await db.ProductionOrder.SingleAsync()).Status);
        Assert.Equal(0, await Qty(db, 800)); Assert.Equal(1000, await Qty(db, 801));
        Assert.All(await db.ProductionBatch.ToListAsync(), b => Assert.NotNull(b.ReversedUtc));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReverseAsync(800, "Again"));
    }

    [Fact]
    public async Task InvoicesCanBeRaisedAgainstTheBoxesProducedSoFar()
    {
        using var db = Db(); var service = Service(db);
        await service.RecordAsync(800, 100);
        SaleInvoiceViewModel Sale(decimal quantity) => new() { ProductionOrderId = 800, CustomerID = "800", BranchID = 1, GSTPer = 0, SaleDate = DateTime.Today,
            Items = [new() { ItemId = 800, Quantity = quantity, SalePrice = 20 }] };
        await OrderPricing.ValidateSaleAsync(db, Sale(100));
        await Assert.ThrowsAsync<InvalidOperationException>(() => OrderPricing.ValidateSaleAsync(db, Sale(101)));
        Sold(db, 60);
        await Assert.ThrowsAsync<InvalidOperationException>(() => OrderPricing.ValidateSaleAsync(db, Sale(41)));
        await OrderPricing.ValidateSaleAsync(db, Sale(40));
        (await db.SaleInvoice.SingleAsync()).IsCancelled = true; await db.SaveChangesAsync();
        await OrderPricing.ValidateSaleAsync(db, Sale(100));
    }
}
