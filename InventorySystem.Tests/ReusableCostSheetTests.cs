using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Services.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventorySystem.Tests;

public class ReusableCostSheetTests
{
    private static ApplicationDbContext Database()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.ItemCategory.Add(new ItemCategory { CategoryId = 700, CategoryName = "Test", IsActive = true });
        db.Items.AddRange(new Items { ItemID = 700, CategoryID = 700, ItemName = "Boxes", ItemType = "FinishedGoods", StockUnit = "pcs", IsActive = true },
            new Items { ItemID = 701, CategoryID = 700, ItemName = "Paper", ItemType = "RawMaterial", StockUnit = "kg", IsActive = true });
        db.Parties.AddRange(new Party { PartyId = 700, PartyName = "Customer A", PartyType = "customer", Status = "active" },
            new Party { PartyId = 701, PartyName = "Customer B", PartyType = "customer", Status = "active" },
            new Party { PartyId = 702, PartyName = "Both", PartyType = "both", Status = "active" },
            new Party { PartyId = 703, PartyName = "Supplier", PartyType = "supplier", Status = "active" },
            new Party { PartyId = 704, PartyName = "Inactive customer", PartyType = "customer", Status = "inactive" });
        db.CostSheet.Add(new CostSheet { CostSheetId = 700, CustomerId = "700", ItemName = "Boxes", FinishedItemId = 700, BranchID = 1,
            Status = "Final", FinalRateWithGST = 25, Plies = [new() { ItemId = 701, KGs = .5m, Rate = 10 }] });
        db.Stock.Add(new Stock { ItemId = 701, BranchId = 1, Quantity = 1000 });
        db.SaveChanges(); return db;
    }
    private static CostSheetViewModel Sheet() => new() { ItemName = "Boxes", FinishedItemId = 700, Status = "Final", BranchID = 1,
        Plies = [new() { ItemId = 701, KGs = .5m, Rate = 10 }] };
    private static ProductionOrderViewModel Order(string? customer = "700", decimal price = 20) => new() {
        CustomerId = customer, CostSheetId = 700, OrderQty = 10, OrderDate = DateTime.Today, DeliveryDate = DateTime.Today,
        AgreedUnitPrice = price, AgreedGstPercent = 5 };
    private static CostSheetController Sheets(ApplicationDbContext db) => new(db, new PermissionService(db));
    private static ProductionController Production(ApplicationDbContext db) => new(db, new StockService(db));

    [Fact]
    public async Task FinalSheet_DoesNotRequireOrStoreCustomerAssignment()
    {
        using var db = Database(); var model = Sheet();
        Assert.IsType<OkObjectResult>(await Sheets(db).Save(model));
        var sheet = await db.CostSheet.SingleAsync(s => s.CostSheetId != 700);
        Assert.Null(sheet.CustomerId); Assert.Equal("Final", sheet.Status); Assert.Equal(5, sheet.PaperCost);
    }

    [Fact]
    public async Task NewSheet_IgnoresLegacyCustomerFieldInRequest()
    {
        using var db = Database(); var model = Sheet(); model.CustomerId = "invalid-or-inactive";
        Assert.IsType<OkObjectResult>(await Sheets(db).Save(model));
        Assert.Null((await db.CostSheet.SingleAsync(s => s.CostSheetId != 700)).CustomerId);
    }

    [Fact]
    public async Task SameRecipe_CreatesSeparateCustomerAgreements_AndSalesKeepTheirOwnCustomer()
    {
        using var db = Database(); var controller = Production(db);
        Assert.IsType<OkObjectResult>(await controller.Save(Order("700", 20)));
        Assert.IsType<OkObjectResult>(await controller.Save(Order("701", 22)));
        var orders = await db.ProductionOrder.OrderBy(o => o.ProductionOrderId).ToListAsync();
        Assert.Equal(2, orders.Count); Assert.All(orders, o => Assert.Equal(700, o.CostSheetId));
        Assert.Equal("700", orders[0].CustomerId); Assert.Equal("701", orders[1].CustomerId);
        Assert.Equal(20, orders[0].AgreedUnitPrice); Assert.Equal(22, orders[1].AgreedUnitPrice);
        Assert.All(await db.ProductionMaterial.ToListAsync(), m => Assert.Equal(.5m, m.KgPerUnit));
        Assert.Equal("700", (await db.CostSheet.FindAsync(700))!.CustomerId);
        var service = new EstimatedProductionService(db, new StockService(db));
        foreach (var order in orders) await service.RecordAsync(order.ProductionOrderId, 10);
        var sale = new SaleInvoiceViewModel { ProductionOrderId = orders[1].ProductionOrderId, CustomerID = "701", BranchID = 1,
            SaleDate = DateTime.Today, PaymentMode = 0, GSTPer = 5, Items = [new() { ItemId = 700, Quantity = 2, SalePrice = 22 }] };
        await InvoiceValidation.Validate(db, sale); Assert.Equal(46.20m, sale.NetAmount);
        sale.CustomerID = "700";
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceValidation.Validate(db, sale));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("not-a-number")] [InlineData("999")]
    [InlineData("703")] [InlineData("704")]
    public async Task Order_RejectsMissingInactiveSupplierOrNonexistentCustomer_BeforeSaving(string? customer)
    {
        using var db = Database();
        Assert.IsType<BadRequestObjectResult>(await Production(db).Save(Order(customer)));
        Assert.Empty(db.ProductionOrder); Assert.Empty(db.ProductionMaterial);
    }

    [Theory]
    [InlineData("702", "702")] [InlineData("00701", "701")]
    public async Task Order_AcceptsBothPartyAndNormalizesSelectedId(string requested, string expected)
    {
        using var db = Database(); Assert.IsType<OkObjectResult>(await Production(db).Save(Order(requested)));
        Assert.Equal(expected, (await db.ProductionOrder.SingleAsync()).CustomerId);
    }

    [Fact]
    public async Task PendingOrder_CanChangeCustomerWithoutChangingSharedSheet()
    {
        using var db = Database(); var controller = Production(db); await controller.Save(Order());
        var order = await db.ProductionOrder.SingleAsync(); var model = Order("701", 23); model.ProductionOrderId = order.ProductionOrderId;
        Assert.IsType<OkObjectResult>(await controller.Update(model));
        Assert.Equal("701", order.CustomerId); Assert.Equal(23, order.AgreedUnitPrice);
        Assert.Equal("700", (await db.CostSheet.FindAsync(700))!.CustomerId);
    }

    [Fact]
    public async Task InvalidCustomerEdit_DoesNotChangeExistingAgreementOrRecipe()
    {
        using var db = Database(); var controller = Production(db); await controller.Save(Order());
        var order = await db.ProductionOrder.SingleAsync(); var model = Order("704", 99); model.ProductionOrderId = order.ProductionOrderId;
        Assert.IsType<BadRequestObjectResult>(await controller.Update(model));
        Assert.Equal("700", order.CustomerId); Assert.Equal(20, order.AgreedUnitPrice);
        Assert.Single(db.ProductionMaterial);
    }

    [Fact]
    public async Task EditingLegacySheet_PreservesMetadataAndExistingOrderSnapshot()
    {
        using var db = Database(); await Production(db).Save(Order("701", 22));
        var model = Sheet(); model.CostSheetId = 700; model.CustomerId = "702"; model.Plies[0].KGs = .8m;
        Assert.IsType<OkObjectResult>(await Sheets(db).Save(model));
        var order = await db.ProductionOrder.SingleAsync();
        Assert.Equal("701", order.CustomerId); Assert.Equal(22, order.AgreedUnitPrice);
        Assert.Equal(.5m, (await db.ProductionMaterial.SingleAsync()).KgPerUnit);
        Assert.Equal("700", (await db.CostSheet.FindAsync(700))!.CustomerId);
    }

    [Fact]
    public async Task CompletedOrder_CannotHaveCustomerChanged()
    {
        using var db = Database(); var controller = Production(db); await controller.Save(Order());
        var order = await db.ProductionOrder.SingleAsync();
        await new EstimatedProductionService(db, new StockService(db)).RecordAsync(order.ProductionOrderId, 10);
        var m = Order("701"); m.ProductionOrderId = order.ProductionOrderId;
        var result = Assert.IsType<OkObjectResult>(await controller.Update(m));
        Assert.False(JsonSerializer.SerializeToElement(result.Value).GetProperty("success").GetBoolean());
        Assert.Equal("700", order.CustomerId);
    }
}
