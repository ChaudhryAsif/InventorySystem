using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Services.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InventorySystem.Tests;

public class PurchaseGroupLookupTests
{
    private static ApplicationDbContext Database()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.ItemCategory.AddRange(
            new ItemCategory { CategoryId=11, CategoryName="Paper supplier", GroupType="RawMaterial", IsActive=true, ContactNumber="03001234567", Address="Lahore" },
            new ItemCategory { CategoryId=12, CategoryName="Box customer", GroupType="FinishedGoods", IsActive=true },
            new ItemCategory { CategoryId=13, CategoryName="Inactive supplier", GroupType="RawMaterial", IsActive=false });
        db.Items.Add(new Items { ItemID=1, ItemName="Paper", ItemType="RawMaterial", CategoryID=11, IsActive=true, StockUnit="kg" });
        // Deliberately collide a Party ID with a Group ID: they must never be confused.
        db.Parties.Add(new Party { PartyId=11, PartyName="Unrelated supplier", PartyType="supplier", Status="active" });
        db.SaveChanges(); return db;
    }
    private static PurchaseController Controller(ApplicationDbContext db) => new(db,
        new LedgerPostingService(db,new AccountService(db)), Options.Create(new AccountingOptions { PostToGeneralLedger=false }),new StockService(db));
    private static PurchaseInvoiceViewModel Invoice(string vendor="group:11") => new() {
        VendorID=vendor, BranchID=1, PaymentMode=0, PurchaseDate=DateTime.Today,
        Items=[new() { Itemid=1, Quantity=10, PurPrice=100 }] };

    [Fact]
    public async Task LookupContainsOnlyActiveRawMaterialGroupsAndDoesNotCreateAccounts()
    {
        using var db=Database();
        var result=Assert.IsType<JsonResult>(await Controller(db).GetSupplierGroups());
        var rows=JsonSerializer.SerializeToElement(result.Value).GetProperty("suppliers");
        var row=Assert.Single(rows.EnumerateArray());
        Assert.Equal("group:11",row.GetProperty("partyId").GetString());
        Assert.Equal("Lahore",row.GetProperty("address").GetString());
        Assert.Equal(1,await db.Parties.CountAsync());
    }

    [Fact]
    public async Task SaveAndUpdateReuseAccountingPartyAndKeepStockCorrect()
    {
        using var db=Database(); var controller=Controller(db);
        var model=Invoice(); Assert.IsType<OkObjectResult>(await controller.Save(model));
        var account=await db.Parties.SingleAsync(p=>p.PartyCode=="RAW-GROUP:11");
        Assert.NotEqual(11,account.PartyId);
        Assert.Equal(account.PartyId.ToString(),(await db.PurchaseInvoice.SingleAsync()).VendorID);
        Assert.Equal("03001234567",account.Phone);
        Assert.Equal(10,(await db.Stock.SingleAsync()).Quantity);
        var edit=Invoice(); edit.PurchaseId=(await db.PurchaseInvoice.SingleAsync()).PurchaseId;
        edit.Items[0].Quantity=15;
        db.ItemCategory.Single(g=>g.CategoryId==11).CategoryName="Renamed paper supplier";
        Assert.IsType<OkObjectResult>(await controller.Update(edit));
        Assert.Equal(2,await db.Parties.CountAsync());
        Assert.Equal("Renamed paper supplier",account.PartyName);
        Assert.Equal(15,(await db.Stock.SingleAsync()).Quantity);
        Assert.Equal(account.PartyId.ToString(),(await db.PurchaseInvoice.SingleAsync()).VendorID);
        var second=Invoice(); Assert.IsType<OkObjectResult>(await controller.Save(second));
        Assert.Equal(2,await db.Parties.CountAsync());
        Assert.Equal(25,(await db.Stock.SingleAsync()).Quantity);
    }

    [Theory]
    [InlineData("group:12")] [InlineData("group:13")] [InlineData("group:999")] [InlineData("group:bad")]
    public async Task WrongInactiveOrMissingGroupsCannotBeSaved(string vendor)
    {
        using var db=Database();
        Assert.IsType<BadRequestObjectResult>(await Controller(db).Save(Invoice(vendor)));
        Assert.Empty(db.PurchaseInvoice); Assert.Empty(db.Stock); Assert.Equal(1,await db.Parties.CountAsync());
    }

    [Fact]
    public async Task UnmappedNumericSupplierMustSelectItsGroupBeforeNewPurchase()
    {
        using var db=Database();
        Assert.IsType<BadRequestObjectResult>(await Controller(db).Save(Invoice("11")));
        Assert.Empty(db.PurchaseInvoice);
        Assert.Equal(1,await db.Parties.CountAsync());
    }

    [Fact]
    public async Task GroupCannotReactivateDisabledAccountingParty()
    {
        using var db=Database(); db.Parties.Add(new Party { PartyName="Disabled",PartyCode="RAW-GROUP:11",PartyType="supplier",Status="inactive" });
        await db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>(await Controller(db).Save(Invoice()));
        Assert.Empty(db.PurchaseInvoice);
    }

    [Fact]
    public async Task PurchaseItemLookupFiltersBeforePagingAndUsesActivePurchasePrice()
    {
        using var db=Database();
        db.Items.Single().SalePrice=999;
        db.Items.Add(new Items { ItemID=2, ItemName="Finished box", ItemType="FinishedGoods", CategoryID=12, IsActive=true });
        db.PurchaseInvoice.AddRange(
            new PurchaseInvoice { PurchaseId=1, VendorID="11", BranchID=1, PurchaseDate=DateTime.Today },
            new PurchaseInvoice { PurchaseId=2, VendorID="11", BranchID=1, PurchaseDate=DateTime.Today, IsCancelled=true });
        db.PurchaseInvoiceBody.AddRange(
            new PurchaseInvoiceBody { PurchaseId=1, Itemid=1, PurPrice=12.5m },
            new PurchaseInvoiceBody { PurchaseId=2, Itemid=1, PurPrice=88m });
        db.SaveChanges();
        var result=Assert.IsType<JsonResult>(await new ItemController(db).GetItems(pageSize:1,itemType:"RawMaterial"));
        var payload=JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal(1,payload.GetProperty("totalCount").GetInt32());
        var item=Assert.Single(payload.GetProperty("items").EnumerateArray());
        Assert.Equal("RawMaterial",item.GetProperty("itemType").GetString());
        Assert.Equal(12.5m,item.GetProperty("purchasePrice").GetDecimal());
        Assert.Equal(999,item.GetProperty("salePrice").GetDecimal());
        var empty=Assert.IsType<JsonResult>(await new ItemController(db).GetItems(itemType:"RawMaterial",search:"Finished"));
        Assert.Empty(JsonSerializer.SerializeToElement(empty.Value).GetProperty("items").EnumerateArray());
        Assert.IsType<BadRequestObjectResult>(await new ItemController(db).GetItems(itemType:"invalid"));
    }

    [Fact]
    public async Task CancelledOnlyAndGroupFilterCombineWithBranchAndDateBeforePaging()
    {
        using var db=Database(); db.Parties.Single().PartyCode="RAW-GROUP:11";
        db.Parties.Add(new Party { PartyId=20, PartyName="Inactive supplier", PartyType="supplier",Status="active" });
        db.PurchaseInvoice.AddRange(
            new PurchaseInvoice { PurchaseId=1, VendorID="11", BranchID=1, PurchaseDate=DateTime.Today, BillNo="QA" },
            new PurchaseInvoice { PurchaseId=2, VendorID="11", BranchID=1, PurchaseDate=DateTime.Today, BillNo="QA", IsCancelled=true },
            new PurchaseInvoice { PurchaseId=3, VendorID="20", BranchID=1, PurchaseDate=DateTime.Today, BillNo="QA" },
            new PurchaseInvoice { PurchaseId=4, VendorID="11", BranchID=2, PurchaseDate=DateTime.Today, BillNo="QA", IsCancelled=true });
        db.SaveChanges(); db.RestrictBranches=true; db.AllowedBranchIds=[1];
        var controller=Controller(db);
        JsonElement Data(IActionResult result)=>JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(result).Value);
        var cancelled=Data(controller.GetPurchases("QA",DateTime.Today,DateTime.Today,pageSize:1,cancelledOnly:true,groupId:11));
        Assert.Equal(1,cancelled.GetProperty("totalCount").GetInt32());
        var row=Assert.Single(cancelled.GetProperty("data").EnumerateArray());
        Assert.Equal(2,row.GetProperty("PurchaseId").GetInt32()); Assert.True(row.GetProperty("IsCancelled").GetBoolean());
        var view=JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(controller.GetPurchaseById(2)).Value);
        Assert.True(view.GetProperty("IsCancelled").GetBoolean());
        Assert.IsType<NotFoundResult>(controller.GetPurchaseById(4));
        Assert.IsType<ViewResult>(controller.Print(2));
        Assert.IsType<NotFoundResult>(controller.Print(4));
        var active=Data(controller.GetPurchases(null,null,null,groupId:11));
        Assert.Equal(1,active.GetProperty("totalCount").GetInt32());
        Assert.False(active.GetProperty("data")[0].GetProperty("IsCancelled").GetBoolean());
        Assert.Equal(1,Data(controller.GetPurchases(null,null,null,groupId:13)).GetProperty("totalCount").GetInt32());
        Assert.Equal(3,Data(controller.GetPurchases(null,null,null,includeCancelled:true)).GetProperty("totalCount").GetInt32());
        Assert.IsType<BadRequestObjectResult>(controller.GetPurchases(null,null,null,groupId:12));
        Assert.Equal(0,Data(controller.GetPurchases("missing",null,null,cancelledOnly:true)).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task SavedGroupDropdownIncludesInactiveRawGroupsForHistoricalInvoices()
    {
        using var db=Database();
        var result=Assert.IsType<JsonResult>(await Controller(db).GetSupplierGroups(includeInactive:true));
        var rows=JsonSerializer.SerializeToElement(result.Value).GetProperty("suppliers");
        Assert.Equal(2,rows.GetArrayLength());
        Assert.DoesNotContain(rows.EnumerateArray(),g=>g.GetProperty("groupId").GetInt32()==12);
    }
}



