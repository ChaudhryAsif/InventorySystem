using System.Security.Claims;
using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace InventorySystem.Tests;
public class ItemUnitImportTests
{
    private static ApplicationDbContext Db()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w=>w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static GatePassController Gate(ApplicationDbContext db)=>new(db){ControllerContext=new(){HttpContext=new DefaultHttpContext{User=new(new ClaimsIdentity([new Claim(ClaimTypes.Name,"QA")],"QA"))}}};
    private static async Task Seed(ApplicationDbContext db){db.ItemCategory.Add(new(){CategoryId=1,CategoryName="Group",GroupType="FinishedGoods",IsActive=true});db.Items.Add(new(){ItemID=1,CategoryID=1,ItemName="Saved item",ItemType="FinishedGoods",StockUnit="kg",SalePrice=125,IsActive=true});db.PurchaseInvoice.AddRange(new PurchaseInvoice{PurchaseId=1,BranchID=1},new PurchaseInvoice{PurchaseId=2,BranchID=1,IsCancelled=true});db.PurchaseInvoiceBody.AddRange(new PurchaseInvoiceBody{Srno=1,PurchaseId=1,Itemid=1,PurPrice=80},new PurchaseInvoiceBody{Srno=2,PurchaseId=2,Itemid=1,PurPrice=999});await db.SaveChangesAsync();}
    [Fact]
    public async Task LookupImportsSavedUnitAndPricesExcludingCancelledPurchases(){using var db=Db();await Seed(db);var result=Assert.IsType<JsonResult>(await Gate(db).GetItemOptions());var row=JsonSerializer.SerializeToElement(result.Value).GetProperty("data")[0];Assert.Equal(1,row.GetProperty("ItemId").GetInt64());Assert.Equal("kg",row.GetProperty("Unit").GetString());Assert.Equal(80,row.GetProperty("PurchasePrice").GetDecimal());Assert.Equal(125,row.GetProperty("SalePrice").GetDecimal());}
    [Theory][InlineData("Inward")][InlineData("Outward")]
    public async Task LinkedItemSnapshotsSurviveMasterPriceChangesAndReopening(string direction){using var db=Db();await Seed(db);var c=Gate(db);var m=new GatePass{Direction=direction,PartyName="QA",Lines=[new(){ItemId=1,Description="Forged name",Unit="kg",UnitPrice=80,TotalQuantity=12.75m}]};Assert.IsType<OkObjectResult>(await c.Save(m));var saved=await db.GatePasses.Include(g=>g.Lines).SingleAsync();var line=Assert.Single(saved.Lines);Assert.Equal("Saved item",line.Description);Assert.Equal(1,line.ItemId);Assert.Equal(80,line.UnitPrice);Assert.Equal(12.75m,line.TotalQuantity);(await db.Items.SingleAsync()).SalePrice=500;await db.SaveChangesAsync();m.Id=saved.Id;m.Revision=saved.Revision;m.Lines[0].Id=line.Id;Assert.IsType<OkObjectResult>(await c.Save(m));Assert.Equal(80,Assert.Single(saved.Lines).UnitPrice);Assert.Equal("kg",Assert.Single(saved.Lines).Unit);Assert.IsType<ViewResult>(await c.Print(saved.Id));Assert.Empty(db.Stock);Assert.Empty(db.AccountLedger);}
    [Theory][InlineData("pcs",1,80)][InlineData("kg",999,80)][InlineData("kg",1,-1)]
    public async Task ForgedUnitMissingItemAndNegativePriceAreRejected(string unit,long itemId,decimal price){using var db=Db();await Seed(db);var m=new GatePass{PartyName="QA",Lines=[new(){ItemId=itemId,Description="QA",Unit=unit,UnitPrice=price,TotalQuantity=1}]};Assert.IsType<BadRequestObjectResult>(await Gate(db).Save(m));Assert.Empty(db.GatePasses);}
    [Fact]
    public async Task FinishedGoodsCanUseKgAndPurchaseHistoryPreventsReinterpretingUnits(){using var db=Db();db.ItemCategory.Add(new(){CategoryId=1,CategoryName="FG",GroupType="FinishedGoods",IsActive=true});await db.SaveChangesAsync();var c=new ItemController(db);var m=new ItemViewModel{CategoryID=1,ItemName="Kg goods",ItemType="FinishedGoods",StockUnit="kg",IsActive=true};Assert.True(JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(await c.Save(m,null)).Value).GetProperty("success").GetBoolean());var item=await db.Items.SingleAsync();Assert.Equal("kg",item.StockUnit);db.PurchaseInvoiceBody.Add(new(){Srno=1,PurchaseId=1,Itemid=item.ItemID});await db.SaveChangesAsync();m.ItemID=item.ItemID;m.StockUnit="pcs";Assert.IsType<BadRequestObjectResult>(await c.Save(m,null));Assert.Equal("kg",item.StockUnit);}
}

