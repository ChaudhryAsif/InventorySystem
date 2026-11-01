using System.Security.Claims;
using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace InventorySystem.Tests;
public class ClientOctober23Tests
{
    private static ApplicationDbContext Db()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w=>w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static JsonElement Data(IActionResult result)=>JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(result).Value);
    private static GatePassController Gate(ApplicationDbContext db)=>new(db){ControllerContext=new(){HttpContext=new DefaultHttpContext{User=new(new ClaimsIdentity([new Claim(ClaimTypes.Name,"QA")],"QA"))}}};
    private static GatePass Pass(string direction="Outward")=>new(){Direction=direction,PartyName="QA",BranchId=1,DriverName="QA Driver",Lines=[new(){Description="Cartons",Unit="pcs",PackingQuantity=50,BundleQuantity=10,OpenCartonQuantity=3,CalculateFromBundles=true,TotalQuantity=999}]};
    [Theory][InlineData("Inward")][InlineData("Outward")]
    public async Task CartonsCalculateOnServerAndPreserveOpenQuantityWhenEditedAndPrinted(string direction)
    {
        using var db=Db();var c=Gate(db);var m=Pass(direction);Assert.IsType<OkObjectResult>(await c.Save(m));var saved=await db.GatePasses.Include(g=>g.Lines).SingleAsync();var line=Assert.Single(saved.Lines);
        Assert.Equal(503,line.TotalQuantity);Assert.Equal(3,line.OpenCartonQuantity);Assert.True(line.CalculateFromBundles);Assert.Equal("QA Driver",saved.DriverSignature);Assert.Equal("M. Nadeem",saved.IssuedBy);Assert.Equal("M. Yameen",saved.GateKeeper);Assert.Equal("M. Sami Ullah",saved.ReceivedBy);Assert.NotNull(saved.DepartureAt);
        var departure=saved.DepartureAt;m.Id=saved.Id;m.Revision=saved.Revision;m.DepartureAt=null;m.Lines[0].OpenCartonQuantity=7;Assert.IsType<OkObjectResult>(await c.Save(m));Assert.Equal(departure,saved.DepartureAt);Assert.Equal(507,Assert.Single(saved.Lines).TotalQuantity);Assert.IsType<ViewResult>(await c.Print(saved.Id));Assert.Empty(db.Stock);Assert.Empty(db.AccountLedger);
    }
    [Fact]
    public async Task HistoricalTotalsAndStaffRemainUnchangedUntilPackingCalculationIsSelected()
    {
        using var db=Db();var c=Gate(db);var m=Pass();m.Lines[0].CalculateFromBundles=false;m.Lines[0].OpenCartonQuantity=0;m.Lines[0].TotalQuantity=777;m.IssuedBy="Original issuer";m.GateKeeper="Original guard";m.ReceivedBy="Original receiver";m.DepartureAt=new DateTime(2026,10,20,12,0,0);
        Assert.IsType<OkObjectResult>(await c.Save(m));var saved=await db.GatePasses.SingleAsync();m.Id=saved.Id;m.Revision=saved.Revision;m.Remarks="Updated remark";Assert.IsType<OkObjectResult>(await c.Save(m));Assert.Equal(777,Assert.Single(saved.Lines).TotalQuantity);Assert.Equal("Original issuer",saved.IssuedBy);Assert.Equal("Original guard",saved.GateKeeper);Assert.Equal("Original receiver",saved.ReceivedBy);Assert.Equal(m.DepartureAt,saved.DepartureAt);
        m.Revision=saved.Revision;m.Lines[0].CalculateFromBundles=true;Assert.IsType<OkObjectResult>(await c.Save(m));Assert.Equal(500,Assert.Single(saved.Lines).TotalQuantity);
    }
    [Theory][InlineData(-1,2,0)][InlineData(50,1.5,0)][InlineData(50,2,-1)][InlineData(0,2,0)][InlineData(100000000,100000000,0)]
    public async Task InvalidCartonArithmeticIsRejected(decimal packing,decimal bundles,decimal open)
    {
        using var db=Db();var m=Pass();m.Lines[0].PackingQuantity=packing;m.Lines[0].BundleQuantity=bundles;m.Lines[0].OpenCartonQuantity=open;Assert.IsType<BadRequestObjectResult>(await Gate(db).Save(m));Assert.Empty(db.GatePasses);
    }
    [Fact]
    public async Task KgQuantitiesRemainManualAndLooseCartonsCanSaveWithoutBundles()
    {
        using var db=Db();var c=Gate(db);var kg=Pass();kg.Lines[0]=new(){Description="Paper",Unit="kg",TotalQuantity=12.75m};Assert.IsType<OkObjectResult>(await c.Save(kg));Assert.Equal(12.75m,await db.GatePassLines.Select(l=>l.TotalQuantity).SingleAsync());
        kg=Pass();kg.Lines[0].Unit="kg";Assert.IsType<BadRequestObjectResult>(await c.Save(kg));
        var loose=Pass();loose.Lines[0].PackingQuantity=0;loose.Lines[0].BundleQuantity=0;loose.Lines[0].OpenCartonQuantity=8;Assert.IsType<OkObjectResult>(await c.Save(loose));Assert.Contains(db.GatePassLines,l=>l.TotalQuantity==8);
    }
    [Fact]
    public async Task ItemLookupImportsStoredPackingAndPlyWithoutGuessingOtherQuantities()
    {
        using var db=Db();db.ItemCategory.Add(new(){CategoryId=1,CategoryName="Boxes",GroupType="FinishedGoods",IsActive=true});db.Items.AddRange(new Items{ItemID=1,CategoryID=1,ItemName="Carton 3-Ply",Packing="50",ItemType="FinishedGoods",StockUnit="pcs",IsActive=true},new Items{ItemID=2,CategoryID=1,ItemName="Carton",Packing="5-Ply",ItemType="FinishedGoods",StockUnit="pcs",IsActive=true});await db.SaveChangesAsync();
        var rows=Data(await Gate(db).GetItemOptions()).GetProperty("data");var first=rows.EnumerateArray().Single(r=>r.GetProperty("Key").GetString()=="item-1");Assert.Equal("3-Ply",first.GetProperty("PackingType").GetString());Assert.Equal(50,first.GetProperty("PackingQuantity").GetDecimal());
    }
    [Fact]
    public async Task FinishedGroupCustomerLookupIsReadOnlyAndSaveCreatesOneValidReusableAccount()
    {
        using var db=Db();db.ItemCategory.AddRange(new ItemCategory{CategoryId=1,CategoryName="Customer group",GroupType="FinishedGoods",IsActive=true,ContactNumber="03001234567",Address="Group address"},new ItemCategory{CategoryId=2,CategoryName="Raw supplier",GroupType="RawMaterial",IsActive=true},new ItemCategory{CategoryId=3,CategoryName="Inactive customer",GroupType="FinishedGoods",IsActive=false});
        db.Items.AddRange(new Items{ItemID=1,CategoryID=1,ItemName="Box",ItemType="FinishedGoods",StockUnit="pcs",IsActive=true},new Items{ItemID=2,CategoryID=2,ItemName="Paper",ItemType="RawMaterial",StockUnit="kg",IsActive=true});db.CostSheet.Add(new(){CostSheetId=1,Status="Final",FinishedItemId=1,ItemName="Box",BranchID=1,Plies=[new(){ItemId=2,KGs=.5m,Rate=10}]});await db.SaveChangesAsync();
        var c=new ProductionController(db,new StockService(db));var rows=Data(await c.GetCustomerOptions()).GetProperty("customers");Assert.Equal("group:1",Assert.Single(rows.EnumerateArray()).GetProperty("selectionId").GetString());Assert.Empty(db.Parties);
        var model=new ProductionOrderViewModel{CustomerId="group:1",CostSheetId=1,OrderQty=10,OrderDate=DateTime.Today,DeliveryDate=DateTime.Today,AgreedUnitPrice=20};Assert.IsType<OkObjectResult>(await c.Save(model));var party=await db.Parties.SingleAsync();Assert.Equal("FG-GROUP:1",party.PartyCode);Assert.Equal("customer",party.PartyType);Assert.Equal("Group address",party.Address);var order=await db.ProductionOrder.SingleAsync();Assert.Equal(party.PartyId.ToString(),order.CustomerId);
        model.ProductionOrderId=order.ProductionOrderId;model.CustomerId="group:1";model.AgreedUnitPrice=25;Assert.IsType<OkObjectResult>(await c.Update(model));Assert.Single(db.Parties);Assert.Equal(25,order.AgreedUnitPrice);Assert.Equal(party.PartyId,Data(await c.GetCustomerOptions(order.ProductionOrderId)).GetProperty("customers")[0].GetProperty("partyId").GetInt32());Assert.IsType<ViewResult>(await c.Print(order.ProductionOrderId));Assert.Empty(db.Stock);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>FinishedGoodsCustomers.ResolveAsync(db,"group:2"));await Assert.ThrowsAsync<InvalidOperationException>(()=>FinishedGoodsCustomers.ResolveAsync(db,"group:3"));
    }
    [Fact]
    public async Task ExistingCustomerAccountIsReusedAndGroupChangesSyncOnlyWhenSaving()
    {
        using var db=Db();db.ItemCategory.Add(new(){CategoryId=1,CategoryName="Buyer",GroupType="FinishedGoods",IsActive=true,Address="New address"});db.Parties.Add(new(){PartyId=9,PartyName="Buyer",PartyType="both",Status="active",OpeningBalance=123});await db.SaveChangesAsync();
        Assert.Equal("9",await FinishedGoodsCustomers.ResolveAsync(db,"group:1"));Assert.Single(db.Parties);Assert.Equal(123,(await db.Parties.SingleAsync()).OpeningBalance);
        (await db.ItemCategory.SingleAsync()).CategoryName="Renamed buyer";await db.SaveChangesAsync();Assert.Equal("9",await FinishedGoodsCustomers.ResolveAsync(db,"group:1"));Assert.Equal("Renamed buyer",(await db.Parties.SingleAsync()).PartyName);
    }
    [Fact]
    public void PackingMigrationAddsOnlyBackwardsCompatibleColumnsAndMatchesTheModel()
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SchemaOnly;Trusted_Connection=True").Options);
        Assert.False(db.Database.HasPendingModelChanges());var script=db.GetService<IMigrator>().GenerateScript("20261021074640_GatePassDocuments","20261023051850_ClientOctober23GatePassPacking");Assert.Contains("ADD [OpenCartonQuantity] decimal(18,3) NOT NULL DEFAULT 0",script);Assert.Contains("ADD [CalculateFromBundles] bit NOT NULL DEFAULT CAST(0 AS bit)",script);Assert.DoesNotContain("DROP TABLE",script);Assert.DoesNotContain("UPDATE [GatePassLines]",script);
    }
}
