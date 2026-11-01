using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace InventorySystem.Tests;

public class ManufacturingFlowTests
{
    private static ApplicationDbContext Db()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.Database.EnsureCreated();
        db.ItemCategory.Add(new ItemCategory { CategoryId = 1, CategoryName = "Test category", GroupType="RawMaterial", IsActive = true });
        db.Parties.AddRange(new Party { PartyId=900, PartyCode="RAW-GROUP:1", PartyName="Supplier", PartyType="supplier", Status="active" },
            new Party { PartyId=901, PartyName="Customer", PartyType="customer", Status="active" });
        db.Items.AddRange(new Items { ItemID=900, CategoryID=1, ItemName="Paper", IsActive=true, ItemType="RawMaterial", StockUnit="kg" },
            new Items { ItemID=901, CategoryID=1, ItemName="Glue", IsActive=true, ItemType="RawMaterial", StockUnit="kg" },
            new Items { ItemID=902, CategoryID=1, ItemName="Boxes", IsActive=true, ItemType="FinishedGoods", StockUnit="pcs", SalePrice=150 });
        db.SaveChanges(); return db;
    }
    private static (AccountService accounts, LedgerPostingService posting, StockService stock) Services(ApplicationDbContext db)
    { var accounts=new AccountService(db); return(accounts,new LedgerPostingService(db,accounts),new StockService(db)); }
    private static CostSheetViewModel Sheet() => new() { CustomerId="901", ItemName="Boxes", FinishedItemId=902,
        BranchID=1, Status="Final", SheetDate=DateTime.Today, GlueItemId=901, GlueKgPerBox=0.02m, GlueRate=100,
        ProfitPct=10, Plies=[new() { ItemId=900, KGs=0.3m, Rate=200 },new() { ItemId=900, KGs=0.2m, Rate=200 }] };
    private static async Task<ProductionOrder> Order(ApplicationDbContext db)
    {
        var sheet=Sheet(); var controller=new CostSheetController(db,new PermissionService(db));
        Assert.IsType<OkObjectResult>(await controller.Save(sheet));
        var entity=await db.CostSheet.SingleAsync();
        var production=new ProductionController(db,new StockService(db));
        Assert.IsType<OkObjectResult>(await production.Save(new ProductionOrderViewModel { CostSheetId=entity.CostSheetId,
            CustomerId="901", ProductName="forged", OrderQty=1000, AgreedUnitPrice=200, OrderDate=DateTime.Today, DeliveryDate=DateTime.Today }));
        return await db.ProductionOrder.SingleAsync();
    }
    private static async Task Buy(ApplicationDbContext db,long item,decimal qty,decimal price,int? count=null,string? type=null)
    {
        var s=Services(db);var controller=new PurchaseController(db,s.posting,Options.Create(new AccountingOptions()),s.stock);
        Assert.IsType<OkObjectResult>(await controller.Save(new PurchaseInvoiceViewModel { VendorID="900",BranchID=1,
            PurchaseDate=DateTime.Today,PaymentMode=0,Items=[new() { Itemid=item,Quantity=qty,PurPrice=price,PackageCount=count,PackageType=type }] }));
    }
    private static Task<decimal> Qty(ApplicationDbContext db,long item)=>db.Stock.Where(s=>s.ItemId==item).SumAsync(s=>s.Quantity);
    private static SaleInvoiceViewModel Sale(int mode=0)=>new() { CustomerID="901",BranchID=1,PaymentMode=mode,SaleDate=DateTime.Today,
        Items=[new() { ItemId=902,Quantity=100,SalePrice=150 }] };
    private static SaleController Sales(ApplicationDbContext db,bool gl=true)
    {var s=Services(db);return new SaleController(db,s.posting,Options.Create(new AccountingOptions {PostToGeneralLedger=gl}),s.stock);}

    [Fact] public async Task Purchase_ToEstimatedProduction_ToCreditSale_AndCollection()
    {
        using var db=Db(); await Buy(db,900,1000,200,2,"Reel");await Buy(db,901,100,100,1,"Drum");
        var order=await Order(db); Assert.Equal("901",order.CustomerId);Assert.Equal("Boxes",order.ProductName);
        Assert.Equal(2,await db.ProductionMaterial.CountAsync());
        Assert.Equal(0.5m,(await db.ProductionMaterial.SingleAsync(m=>m.ItemId==900)).KgPerUnit);
        Assert.Equal(2,(await db.PurchaseInvoiceBody.FirstAsync(b=>b.Itemid==900)).PackageCount);
        var service=new EstimatedProductionService(db,new StockService(db));await service.RecordAsync(order.ProductionOrderId,1000);
        Assert.Equal(500,await Qty(db,900));Assert.Equal(80,await Qty(db,901));Assert.Equal(1000,await Qty(db,902));
        Assert.All(await db.ProductionConsumption.ToListAsync(),c=>Assert.True(c.IsEstimated));
        Assert.Equal("Completed",order.Status);Assert.Equal(1000,order.ProducedQty);
        Assert.IsType<OkObjectResult>(await Sales(db).Save(Sale()));Assert.Equal(900,await Qty(db,902));
        var accounts=new AccountService(db); Assert.Equal(15000,(await accounts.GetOutstandingReportAsync("customer")).Single().Balance);
        var controller=new AccountsController(db,accounts) {ControllerContext=new ControllerContext {HttpContext=new DefaultHttpContext()}};
        Assert.IsType<OkObjectResult>(await controller.SavePayment(new PartyPaymentRequest {PartyId=901,Amount=5000,Date=DateTime.Today,Direction="Receipt"}));
        Assert.Equal(10000,(await accounts.GetOutstandingReportAsync("customer")).Single().Balance);
        Assert.Equal(10000,(await accounts.GetPartyStatementAsync(901,null,null)).Last().Balance);
        Assert.Equal(10000,(await accounts.GetAgingReportAsync("customer",DateTime.Today)).Rows.Sum(r=>r.Total));
    }

    [Fact] public async Task CostSheetEdits_DoNotChangeSavedMaterialPlanOrProduct()
    {
        using var db=Db();await Buy(db,900,1000,200);await Buy(db,901,100,100);var order=await Order(db);
        var ply=await db.CostSheetPly.FirstAsync();ply.KGs=100;var sheet=await db.CostSheet.SingleAsync();sheet.GlueKgPerBox=50;sheet.FinalRateWithGST=999;sheet.FinishedItemId=900;
        await db.SaveChangesAsync();await new EstimatedProductionService(db,new StockService(db)).RecordAsync(order.ProductionOrderId,500);
        Assert.Equal(750,await Qty(db,900));Assert.Equal(90,await Qty(db,901));Assert.Equal(500,await Qty(db,902));Assert.NotEqual(999,order.EstimatedRate);
    }
    [Fact] public async Task DuplicatePostingAndReversal_AreRejected_AndHistoryIsKept()
    {
        using var db=Db();await Buy(db,900,1000,200);await Buy(db,901,100,100);var order=await Order(db);
        var service=new EstimatedProductionService(db,new StockService(db));await service.RecordAsync(order.ProductionOrderId,1000);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.RecordAsync(order.ProductionOrderId,1000));
        Assert.Equal(1000,await Qty(db,902));await service.ReverseAsync(order.ProductionOrderId,"Incorrect quantity");
        Assert.Equal(1000,await Qty(db,900));Assert.Equal(100,await Qty(db,901));Assert.Equal(0,await Qty(db,902));
        Assert.Equal("Reversed",order.Status);Assert.Equal(2,await db.ProductionConsumption.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ReverseAsync(order.ProductionOrderId,"Again"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.RecordAsync(order.ProductionOrderId,1000));
    }
    [Fact] public async Task MaterialShortage_DoesNotDeductOtherMaterialsOrCreateOutput()
    {
        using var db=Db();await Buy(db,900,1000,200);await Buy(db,901,1,100);var order=await Order(db);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new EstimatedProductionService(db,new StockService(db)).RecordAsync(order.ProductionOrderId,1000));
        Assert.Equal(1000,await Qty(db,900));Assert.Equal(1,await Qty(db,901));Assert.Equal(0,await Qty(db,902));
        Assert.Empty(await db.ProductionConsumption.ToListAsync());Assert.Equal("Pending",order.Status);
    }
    [Fact] public async Task SoldFinishedGoods_PreventReversal()
    {
        using var db=Db();await Buy(db,900,1000,200);await Buy(db,901,100,100);var order=await Order(db);
        var service=new EstimatedProductionService(db,new StockService(db));await service.RecordAsync(order.ProductionOrderId,1000);
        Assert.IsType<OkObjectResult>(await Sales(db).Save(Sale()));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ReverseAsync(order.ProductionOrderId,"Wrong posting"));
        Assert.Equal(900,await Qty(db,902));Assert.Equal(500,await Qty(db,900));
    }
    [Fact] public async Task Costing_IsCalculatedFromInputs_AndAdhesivesAreSeparate()
    {
        using var db=Db();var sheet=Sheet();sheet.FinalRateWithGST=1;sheet.ManufacturingTotal=99999;
        await CostSheetCalculation.ValidateAndCalculateAsync(db,sheet);
        Assert.Equal(102,sheet.PaperCost);Assert.Equal(112.20m,sheet.FinalRateWithGST);
        sheet.GlueItemId=null;await CostSheetCalculation.ValidateAndCalculateAsync(db,sheet); // Costing no longer requires inventory links.
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task CashSale_IsCleared_WithOrWithoutInvoiceGLPosting(bool gl)
    {
        using var db=Db();await Buy(db,902,200,100);Assert.IsType<OkObjectResult>(await Sales(db,gl).Save(Sale(1)));
        var accounts=new AccountService(db);Assert.Empty(await accounts.GetOutstandingReportAsync("customer"));
        Assert.Equal(0,(await accounts.GetPartyStatementAsync(901,null,null)).Last().Balance);
    }
    [Fact] public async Task OpeningAndCreditPurchase_AppearOnceInDuesAndDatedStatement()
    {
        using var db=Db();(await db.Parties.FindAsync(900))!.OpeningBalance=10000;await db.SaveChangesAsync();await Buy(db,900,100,200);
        var accounts=new AccountService(db);Assert.Equal(30000,(await accounts.GetOutstandingReportAsync("supplier")).Single().Balance);
        Assert.Equal(30000,(await accounts.GetPartyStatementAsync(900,DateTime.Today.AddDays(1),null)).Single().Balance);
        var controller=new AccountsController(db,accounts) {ControllerContext=new ControllerContext {HttpContext=new DefaultHttpContext()}};
        Assert.IsType<OkObjectResult>(await controller.SavePayment(new PartyPaymentRequest {PartyId=900,Amount=5000,Date=DateTime.Today,Direction="Payment"}));
        Assert.Equal(25000,(await accounts.GetOutstandingReportAsync("supplier")).Single().Balance);
    }
    [Fact] public async Task OverpaymentAndFractionalBoxes_AreRejected()
    {
        using var db=Db();var accounts=new AccountService(db);var controller=new AccountsController(db,accounts) {ControllerContext=new ControllerContext {HttpContext=new DefaultHttpContext()}};
        Assert.IsType<BadRequestObjectResult>(await controller.SavePayment(new PartyPaymentRequest {PartyId=901,Amount=1,Date=DateTime.Today}));
        var sale=Sale();sale.Items[0].Quantity=0.5m;await Assert.ThrowsAsync<InvalidOperationException>(()=>InvoiceValidation.Validate(db,sale));
    }
}
