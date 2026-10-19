using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Services.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace InventorySystem.Tests;

public class OtherMaterialsTests
{
    private static ApplicationDbContext Db()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.Database.EnsureCreated();
        db.ItemCategory.Add(new ItemCategory { CategoryId = 1, CategoryName = "Test category", IsActive = true });
        db.Parties.AddRange(new Party { PartyId = 900, PartyName = "Supplier", PartyType = "supplier", Status = "active" },
            new Party { PartyId = 901, PartyName = "Customer", PartyType = "customer", Status = "active" });
        db.Items.AddRange(
            new Items { ItemID = 900, CategoryID = 1, ItemName = "Paper", IsActive = true, ItemType = "RawMaterial", StockUnit = "kg" },
            new Items { ItemID = 901, CategoryID = 1, ItemName = "Glue", IsActive = true, ItemType = "RawMaterial", StockUnit = "kg" },
            new Items { ItemID = 902, CategoryID = 1, ItemName = "Boxes", IsActive = true, ItemType = "FinishedGoods", StockUnit = "pcs", SalePrice = 150 },
            new Items { ItemID = 903, CategoryID = 1, ItemName = "Tape", IsActive = true, ItemType = "RawMaterial", StockUnit = "pcs" },
            new Items { ItemID = 904, CategoryID = 1, ItemName = "Old tape", IsActive = false, ItemType = "RawMaterial", StockUnit = "pcs" },
            new Items { ItemID = 905, CategoryID = 1, ItemName = "Sealant", IsActive = true, ItemType = "RawMaterial", StockUnit = "kg" });
        db.SaveChanges();
        return db;
    }

    private static CostSheetMaterialViewModel M(long id, decimal qty, decimal rate) => new() { ItemId = id, Quantity = qty, Rate = rate };

    private static CostSheetViewModel Sheet(params CostSheetMaterialViewModel[] others) => new()
    {
        ItemName = "Boxes", FinishedItemId = 902, BranchID = 1, Status = "Final", SheetDate = DateTime.Today,
        GlueItemId = 901, GlueKgPerBox = 0.02m, GlueRate = 100, LabourRate = 10,
        Plies = [new() { ItemId = 900, KGs = 0.5m, Rate = 200 }], Materials = others.ToList()
    };

    private static async Task Buy(ApplicationDbContext db, long item, decimal qty, decimal price)
    {
        var accounts = new AccountService(db);
        var controller = new PurchaseController(db, new LedgerPostingService(db, accounts), Options.Create(new AccountingOptions()), new StockService(db));
        Assert.IsType<OkObjectResult>(await controller.Save(new PurchaseInvoiceViewModel
        {
            VendorID = "900", BranchID = 1, PurchaseDate = DateTime.Today, PaymentMode = 0,
            Items = [new() { Itemid = item, Quantity = qty, PurPrice = price }]
        }));
    }

    private static Task<decimal> Qty(ApplicationDbContext db, long item) => db.Stock.Where(s => s.ItemId == item).SumAsync(s => s.Quantity);

    private static async Task<ProductionOrder> OrderFrom(ApplicationDbContext db, CostSheetViewModel sheet)
    {
        Assert.IsType<OkObjectResult>(await new CostSheetController(db, new PermissionService(db)).Save(sheet));
        var entity = await db.CostSheet.SingleAsync();
        Assert.IsType<OkObjectResult>(await new ProductionController(db, new StockService(db)).Save(new ProductionOrderViewModel
        {
            CostSheetId = entity.CostSheetId, CustomerId = "901", OrderQty = 1000, AgreedUnitPrice = 200,
            OrderDate = DateTime.Today, DeliveryDate = DateTime.Today
        }));
        return await db.ProductionOrder.SingleAsync();
    }

    [Fact] public async Task PcsMaterial_AddsCost_ButNotBoxWeight()
    {
        using var db = Db();
        var without = Sheet(); await CostSheetCalculation.ValidateAndCalculateAsync(db, without);
        var with = Sheet(M(903, 2, 5), M(905, 0.1m, 50));
        await CostSheetCalculation.ValidateAndCalculateAsync(db, with);
        Assert.Equal(15, with.PaperCost - without.PaperCost);
        Assert.Equal(without.LabourCost, with.LabourCost);
        Assert.Equal("pcs", with.Materials[0].Unit); Assert.Equal("kg", with.Materials[1].Unit);
        Assert.Equal(10, with.Materials[0].Cost);
    }

    [Fact] public async Task OtherMaterials_RejectInvalidLines()
    {
        using var db = Db();
        foreach (var bad in new CostSheetMaterialViewModel[]
        {
            M(904, 1, 1), M(902, 1, 1),
            M(903, 0, 1), M(999, 1, 1)
        })
            await Assert.ThrowsAsync<InvalidOperationException>(() => CostSheetCalculation.ValidateAndCalculateAsync(db, Sheet(bad)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CostSheetCalculation.ValidateAndCalculateAsync(db,
            Sheet(M(903, 1, 1), M(903, 2, 1))));
    }

    [Fact] public async Task PcsMaterial_IsDeductedInWholePieces_AndRestoredOnReversal()
    {
        using var db = Db();
        await Buy(db, 900, 1000, 200); await Buy(db, 901, 100, 100); await Buy(db, 903, 1000, 5);
        var order = await OrderFrom(db, Sheet(M(903, 0.0015m, 5)));
        var plan = await db.ProductionMaterial.SingleAsync(m => m.ItemId == 903);
        Assert.Equal("pcs", plan.Unit); Assert.Equal(2, plan.RequiredKg);
        var service = new EstimatedProductionService(db, new StockService(db));
        await service.RecordAsync(order.ProductionOrderId, 1000);
        Assert.Equal(998, await Qty(db, 903)); Assert.Equal(500, await Qty(db, 900));
        var used = await db.ProductionConsumption.SingleAsync(c => c.ItemId == 903);
        Assert.Equal("pcs", used.Unit); Assert.Equal(2, used.ActualKg);
        await service.ReverseAsync(order.ProductionOrderId, "Wrong quantity");
        Assert.Equal(1000, await Qty(db, 903)); Assert.Equal(1000, await Qty(db, 900));
    }

    [Fact] public async Task PcsShortage_BlocksProduction_AndChangesNoStock()
    {
        using var db = Db();
        await Buy(db, 900, 1000, 200); await Buy(db, 901, 100, 100); await Buy(db, 903, 1, 5);
        var order = await OrderFrom(db, Sheet(M(903, 0.01m, 5)));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EstimatedProductionService(db, new StockService(db)).RecordAsync(order.ProductionOrderId, 1000));
        Assert.Contains("pcs", error.Message);
        Assert.Equal(1, await Qty(db, 903)); Assert.Equal(1000, await Qty(db, 900)); Assert.Equal(0, await Qty(db, 902));
        Assert.Equal("Pending", order.Status);
    }

    [Fact] public void Required_RoundsKgToGramAndPcsToWhole()
    {
        Assert.Equal(1.235m, EstimatedProductionService.Required(0.0012341m, 1000));
        Assert.Equal(2, EstimatedProductionService.Required(0.0015m, 1000, "pcs"));
        Assert.Equal(10, EstimatedProductionService.Required(0.01m, 1000, "pcs"));
    }
}
