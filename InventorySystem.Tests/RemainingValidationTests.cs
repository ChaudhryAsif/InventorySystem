using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventorySystem.Tests;

public class RemainingValidationTests
{
    private static ApplicationDbContext Database()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.Database.EnsureCreated(); return db;
    }
    [Theory]
    [InlineData("pcs", .5, false)] [InlineData("kg", 1.0001, false)] [InlineData("kg", 1.125, true)]
    public async Task Adjustment_UsesStockUnitPrecision(string unit, decimal quantity, bool allowed)
    {
        using var db = Database();db.Items.Add(new Items { ItemID = 900, ItemName = "QA", CategoryID = 1, IsActive = true, StockUnit = unit });await db.SaveChangesAsync();
        var controller = new StockAdjustmentController(db, new StockService(db));
        var response = await controller.Save(new StockAdjustmentViewModel { AdjustmentNo = "QA", AdjustmentDate = DateTime.Today, BranchId = 1, Status = "Posted", Details = [new() { ItemId = 900, Quantity = quantity, Direction = "Increase" }] });
        if (!allowed) { Assert.IsType<BadRequestObjectResult>(response);Assert.Empty(db.StockAdjustment);Assert.Empty(db.Stock); }
        else { Assert.IsType<JsonResult>(response);Assert.Equal(quantity, (await db.Stock.SingleAsync()).Quantity); }
    }
    [Theory] [InlineData(-1, 10)] [InlineData(1, 101)]
    public async Task InvalidCostDefaults_DoNotChangeSavedSettings(decimal rate, decimal percent)
    {
        using var db = Database();(await db.CostSheetSettings.FirstAsync()).LabourRate = 2;await db.SaveChangesAsync();
        var controller = new CostSheetController(db, new PermissionService(db));
        Assert.IsType<BadRequestObjectResult>(await controller.SaveSettings(new CostSheetSettingsViewModel { LabourRate = rate, DefaultProfitPct = percent }));
        Assert.Equal(2m, (await db.CostSheetSettings.FirstAsync()).LabourRate);
    }
    [Fact] public async Task NegativePartyPagination_UsesSafeFirstPage()
    {
        using var db = Database();db.Parties.Add(new Party { PartyName = "QA", PartyType = "customer", Status = "active" });await db.SaveChangesAsync();
        var response = Assert.IsType<OkObjectResult>(await new PartiesController(db).GetParties(-1, -1));
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        Assert.Equal(1, data.RootElement.GetProperty("page").GetInt32());Assert.Equal(1, data.RootElement.GetProperty("pageSize").GetInt32());
    }
}
