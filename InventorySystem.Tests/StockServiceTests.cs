using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventorySystem.Tests;

public class StockServiceTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task AdjustAsync_NoExistingRow_CreatesRowWithDeltaAsQuantity()
    {
        using var db = NewContext();
        var svc = new StockService(db);

        await svc.AdjustAsync(itemId: 1, branchId: 1, delta: 25m);
        await db.SaveChangesAsync();

        var stock = await db.Stock.SingleAsync(s => s.ItemId == 1 && s.BranchId == 1);
        Assert.Equal(25m, stock.Quantity);
    }

    [Fact]
    public async Task AdjustAsync_NoExistingRow_NegativeDelta_CreatesNegativeQuantity()
    {
        using var db = NewContext();
        var svc = new StockService(db);

        await svc.AdjustAsync(itemId: 2, branchId: 1, delta: -10m);
        await db.SaveChangesAsync();

        var stock = await db.Stock.SingleAsync(s => s.ItemId == 2 && s.BranchId == 1);
        Assert.Equal(-10m, stock.Quantity);
    }

    [Fact]
    public async Task AdjustAsync_ExistingRow_PositiveDelta_IncreasesQuantity()
    {
        using var db = NewContext();
        db.Stock.Add(new Stock { ItemId = 3, BranchId = 1, Quantity = 50m, LastUpdated = DateTime.Now.AddDays(-1) });
        await db.SaveChangesAsync();

        var svc = new StockService(db);
        await svc.AdjustAsync(itemId: 3, branchId: 1, delta: 20m);
        await db.SaveChangesAsync();

        var stock = await db.Stock.SingleAsync(s => s.ItemId == 3 && s.BranchId == 1);
        Assert.Equal(70m, stock.Quantity);
    }

    [Fact]
    public async Task AdjustAsync_ExistingRow_NegativeDelta_DecreasesQuantity_CanGoNegative()
    {
        using var db = NewContext();
        db.Stock.Add(new Stock { ItemId = 4, BranchId = 1, Quantity = 5m, LastUpdated = DateTime.Now.AddDays(-1) });
        await db.SaveChangesAsync();

        var svc = new StockService(db);
        await svc.AdjustAsync(itemId: 4, branchId: 1, delta: -12m);
        await db.SaveChangesAsync();

        // Current behavior has no floor at zero — confirm it, not invent one.
        var stock = await db.Stock.SingleAsync(s => s.ItemId == 4 && s.BranchId == 1);
        Assert.Equal(-7m, stock.Quantity);
    }

    [Fact]
    public async Task AdjustAsync_TwoSequentialCalls_Accumulate()
    {
        using var db = NewContext();
        var svc = new StockService(db);

        await svc.AdjustAsync(itemId: 5, branchId: 1, delta: 10m);
        await db.SaveChangesAsync();
        await svc.AdjustAsync(itemId: 5, branchId: 1, delta: 15m);
        await db.SaveChangesAsync();

        var stock = await db.Stock.SingleAsync(s => s.ItemId == 5 && s.BranchId == 1);
        Assert.Equal(25m, stock.Quantity);
    }

    [Fact]
    public async Task AdjustAsync_RefreshesLastUpdated_OnEachCall()
    {
        using var db = NewContext();
        var svc = new StockService(db);

        await svc.AdjustAsync(itemId: 6, branchId: 1, delta: 10m);
        await db.SaveChangesAsync();
        var firstUpdated = (await db.Stock.SingleAsync(s => s.ItemId == 6 && s.BranchId == 1)).LastUpdated;

        await Task.Delay(10);   // ensure the clock actually advances between calls

        await svc.AdjustAsync(itemId: 6, branchId: 1, delta: 5m);
        await db.SaveChangesAsync();
        var secondUpdated = (await db.Stock.SingleAsync(s => s.ItemId == 6 && s.BranchId == 1)).LastUpdated;

        Assert.True(secondUpdated > firstUpdated);
    }
}
