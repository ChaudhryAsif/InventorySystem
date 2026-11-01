using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Controllers;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InventorySystem.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    { if (Environment.GetEnvironmentVariable("POS_TEST_SQL_SERVER") != "localdb") Skip = "Set POS_TEST_SQL_SERVER=localdb to run isolated SQL Server tests."; }
}
// This fixture accepts only the local SQL Server instance and creates its own uniquely named database.
public class SqlServerSafetyTests : IAsyncLifetime
{
    private readonly string name = "InventorySystem_Test_" + Guid.NewGuid().ToString("N");
    private DbContextOptions<ApplicationDbContext> Options => new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={name};Trusted_Connection=True;TrustServerCertificate=True;").Options;
    private ApplicationDbContext Db() => new(Options);
    [SqlServerFact]
    public async Task CategoryStatus_UsesOuterOperationTransaction_AndRollsBackAllItemsTogether()
    {
        await using var db = Db();
        var category = await db.ItemCategory.SingleAsync();
        var model = new ItemCategoryViewModel { CategoryId = category.CategoryId, CategoryName = category.CategoryName, IsActive = false };
        var controller = new CategoryController(db);
        await using (var outer = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
        {
            var preview = await controller.Save(model);
            model.StatusConfirmationToken = System.Text.Json.JsonSerializer.SerializeToElement(preview.Value).GetProperty("confirmationToken").GetString();
            var saved = await controller.Save(model);
            Assert.True(System.Text.Json.JsonSerializer.SerializeToElement(saved.Value).GetProperty("success").GetBoolean());
            Assert.Same(outer, db.Database.CurrentTransaction);
            Assert.All(await db.Items.ToListAsync(), i => Assert.False(i.IsActive));
            await outer.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        Assert.True((await db.ItemCategory.SingleAsync()).IsActive);
        Assert.All(await db.Items.ToListAsync(), i => Assert.True(i.IsActive));
    }
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("POS_TEST_SQL_SERVER") != "localdb") return;
        await using var db = Db(); await db.Database.EnsureCreatedAsync();
        db.ItemCategory.Add(new ItemCategory { CategoryName = "SQL test category", IsActive = true });
        await db.SaveChangesAsync();
        db.Parties.Add(new Party { PartyName = "SQL test customer", PartyType = "customer", Status = "active" });
        db.Items.Add(new Items { CategoryID = 1, ItemName = "SQL test item", IsActive = true, SalePrice = 180 });
        await db.SaveChangesAsync();
        db.Stock.Add(new Stock { ItemId = (await db.Items.SingleAsync(i => i.ItemName == "SQL test item")).ItemID, BranchId = 1, Quantity = 10, LastUpdated = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
    public async Task DisposeAsync()
    {
        if (Environment.GetEnvironmentVariable("POS_TEST_SQL_SERVER") != "localdb") return;
        if (!name.StartsWith("InventorySystem_Test_", StringComparison.Ordinal) || name.Length != 53) throw new InvalidOperationException("Unsafe test database name.");
        await using var db = Db(); await db.Database.EnsureDeletedAsync();
    }
    [SqlServerFact] public async Task ConcurrentStockUpdates_RejectLostUpdate()
    {
        await using var first = Db(); await using var second = Db();
        var a = await first.Stock.SingleAsync(); var b = await second.Stock.SingleAsync();
        a.Quantity -= 7; b.Quantity -= 7; await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var check = Db(); Assert.Equal(3, (await check.Stock.SingleAsync()).Quantity);
    }
    [SqlServerFact] public async Task InvoiceStockAndGeneralLedger_ShareTransaction_AndRollbackTogether()
    {
        await using var db = Db();
        var item = await db.Items.SingleAsync(i => i.ItemName == "SQL test item");
        var party = await db.Parties.SingleAsync(p => p.PartyName == "SQL test customer");
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var accounts = new AccountService(db);
            var controller = new SaleController(db, new LedgerPostingService(db, accounts), Microsoft.Extensions.Options.Options.Create(new AccountingOptions { PostToGeneralLedger = true }), new StockService(db));
            var response = await controller.Save(new SaleInvoiceViewModel { CustomerID = party.PartyId.ToString(), BranchID = 1, PaymentMode = 1, SaleDate = DateTime.Today,
                Items = [new() { ItemId = item.ItemID, Quantity = 2, SalePrice = 180, DiscPer = 10 }] });
            Assert.IsType<OkObjectResult>(response);
            Assert.Equal(324, (await db.SaleInvoice.SingleAsync()).NetAmount);
            Assert.Equal(8, (await db.Stock.SingleAsync()).Quantity);
            Assert.Equal(2, await db.GeneralLedger.CountAsync());
            await tx.RollbackAsync();
        }
        await using var check = Db();
        Assert.Empty(await check.SaleInvoice.ToListAsync()); Assert.Empty(await check.GeneralLedger.ToListAsync());
        Assert.Equal(10, (await check.Stock.SingleAsync()).Quantity);
    }
    [SqlServerFact] public async Task BranchFilters_TranslateOnSqlServer_AndIsolateStock()
    {
        await using var db = Db(); db.RestrictBranches = true; db.AllowedBranchIds = [2];
        Assert.Empty(await db.Stock.ToListAsync());
        Assert.Empty(await db.SaleInvoice.ToListAsync());
        Assert.Empty(await db.ProductionOrder.ToListAsync());
    }
    [SqlServerFact] public async Task StockReport_ContainsNeverStockedProducts()
    {
        await using var db = Db();
        db.Items.Add(new Items { ItemName = "Never stocked", CategoryID = 1, IsActive = true }); await db.SaveChangesAsync();
        var response = Assert.IsType<JsonResult>(await new ReportController(db).GetStockData(1));
        var json = System.Text.Json.JsonSerializer.Serialize(response.Value);
        Assert.Contains("Never stocked", json); Assert.Contains("Out of Stock", json);
    }
    [SqlServerFact] public async Task EstimatedProduction_PostsOnce_AndReversalRestoresBothStocks()
    {
        await using var db = Db(); var item = await db.Items.SingleAsync(i => i.ItemName == "SQL test item");
        item.ItemType = "RawMaterial"; item.StockUnit = "kg";
        var output = new Items { ItemName = "SQL finished box", CategoryID = 1, ItemType = "FinishedGoods", StockUnit = "pcs", IsActive = true };
        db.Items.Add(output); await db.SaveChangesAsync();
        var sheet = new CostSheet { BranchID = 1, Status = "Final", ItemName = "Test box", FinishedItemId = output.ItemID }; db.CostSheet.Add(sheet); await db.SaveChangesAsync();
        var order = new ProductionOrder { CostSheetId = sheet.CostSheetId, FinishedItemId = output.ItemID, UsesEstimatedConsumption = true, BranchId = 1, OrderQty = 1, OrderDate = DateTime.Today, DeliveryDate = DateTime.Today };
        db.ProductionOrder.Add(order); await db.SaveChangesAsync();
        db.ProductionMaterial.Add(new ProductionMaterial { ProductionOrderId = order.ProductionOrderId, ItemId = item.ItemID, KgPerUnit = 3, RequiredKg = 3 }); await db.SaveChangesAsync();
        var service = new InventorySystem.Services.Services.EstimatedProductionService(db, new StockService(db));
        await service.RecordAsync(order.ProductionOrderId, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordAsync(order.ProductionOrderId, 1));
        Assert.Equal(7, (await db.Stock.SingleAsync(s => s.ItemId == item.ItemID)).Quantity);
        Assert.Equal(1, (await db.Stock.SingleAsync(s => s.ItemId == output.ItemID)).Quantity);
        await service.ReverseAsync(order.ProductionOrderId, "SQL reversal test");
        await using var check = Db();
        Assert.Equal(10, (await check.Stock.SingleAsync(s => s.ItemId == item.ItemID)).Quantity);
        Assert.Equal(0, (await check.Stock.SingleAsync(s => s.ItemId == output.ItemID)).Quantity);
        Assert.True((await check.ProductionConsumption.SingleAsync()).IsEstimated);
    }
    [SqlServerFact] public async Task MigrationChain_CreatesTheCurrentSchema()
    {
        var migrationName = "InventorySystem_Test_" + Guid.NewGuid().ToString("N");
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={migrationName};Trusted_Connection=True;TrustServerCertificate=True;").Options);
        try
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.OperationRecords.ToListAsync());
            Assert.All(await db.AppUsers.ToListAsync(), u => Assert.Equal("1", u.BranchIds));
            Assert.Empty(await db.SaleInvoice.ToListAsync());
            Assert.Empty(await db.Items.Select(i => i.Barcode).ToListAsync());
            Assert.Empty(await db.GatePasses.Include(g => g.Lines).ToListAsync());
        }
        finally
        {
            if (migrationName.Length != 53 || !migrationName.StartsWith("InventorySystem_Test_")) throw new InvalidOperationException("Unsafe database name.");
            await db.Database.EnsureDeletedAsync();
        }
    }
    [SqlServerFact] public async Task GatePass_SaveEditSearchAndCancel_WorkOnSqlServer()
    {
        await using var db = Db();
        var controller = new GatePassController(db) { ControllerContext = new() { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
        var pass = new GatePass { Direction="Outward", BranchId=1, PartyName="SQL gate party", GrossWeightKg=1500, TareWeightKg=500,
            Lines=[new() { Description="SQL cartons", PackingType="3-Ply", PackingQuantity=20, BundleQuantity=5, TotalQuantity=100, Unit="pcs" }] };
        Assert.IsType<OkObjectResult>(await controller.Save(pass));
        db.ChangeTracker.Clear();
        var saved = await db.GatePasses.AsNoTracking().Include(g=>g.Lines).SingleAsync();
        Assert.Equal(1000,saved.NetWeightKg);
        saved.Lines[0].PackingType="5-Ply";saved.Lines[0].TotalQuantity=120;
        Assert.IsType<OkObjectResult>(await controller.Save(saved));
        db.ChangeTracker.Clear();
        var updated=await db.GatePasses.AsNoTracking().Include(g=>g.Lines).SingleAsync();
        Assert.Equal(2,updated.Revision);Assert.Equal(120,Assert.Single(updated.Lines).TotalQuantity);
        Assert.Equal(1,await db.GatePassLines.CountAsync());
        var result=Assert.IsType<JsonResult>(await controller.GetList("Outward",updated.PassNumber));
        Assert.Contains("SQL gate party",System.Text.Json.JsonSerializer.Serialize(result.Value));
        Assert.IsType<ViewResult>(await controller.Print(updated.Id));
        Assert.IsType<OkObjectResult>(await controller.Cancel(new() { Id=updated.Id,Revision=updated.Revision,Reason="SQL cancellation check" }));
        db.ChangeTracker.Clear();Assert.True((await db.GatePasses.SingleAsync()).IsCancelled);
        Assert.Empty(await db.AccountLedger.ToListAsync());
        Assert.Equal(10,(await db.Stock.SingleAsync()).Quantity);
    }
}
