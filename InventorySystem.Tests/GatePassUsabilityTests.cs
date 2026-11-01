using System.Security.Claims;
using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventorySystem.Tests;

public class GatePassUsabilityTests
{
    private static ApplicationDbContext Db()=>new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w=>w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static GatePassController Controller(ApplicationDbContext db)=>new(db){ControllerContext=new(){HttpContext=new DefaultHttpContext{
        User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name,"QA operator"),new Claim(ClaimTypes.Role,"Admin")],"QA"))}}};
    private static JsonElement Data(IActionResult result)=>JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(result).Value);
    private static ItemCategory Group(int id,string type="RawMaterial",bool? active=true)=>new(){CategoryId=id,CategoryName=$"Group {id:D3}",GroupType=type,IsActive=active,ContactNumber="03001112222",Address="Group address"};
    [Fact]
    public async Task PartyLookupPagesAcrossMasterPartiesAndGroupsWithoutLosingRows()
    {
        using var db=Db();
        for(var i=1;i<=45;i++)db.Parties.Add(new(){PartyId=i,PartyName=$"Party {i:D3}",PartyType="supplier",Status="active"});
        for(var i=1;i<=25;i++)db.ItemCategory.Add(Group(i));
        await db.SaveChangesAsync();var c=Controller(db);var keys=new List<string>();
        for(var page=1;page<=4;page++)
        {
            var data=Data(await c.GetPartyOptions(page:page));keys.AddRange(data.GetProperty("data").EnumerateArray().Select(r=>r.GetProperty("Key").GetString()!));
            Assert.Equal(page<4,data.GetProperty("hasMore").GetBoolean());
        }
        Assert.Equal(70,keys.Count);Assert.Equal(70,keys.Distinct().Count());Assert.Contains("group-25",keys);
        Assert.Empty(Data(await c.GetPartyOptions(page:5)).GetProperty("data").EnumerateArray());
    }
    [Fact]
    public async Task PartyLookupIncludesContactsFiltersInactiveAndAvoidsDuplicatePurchaseBridges()
    {
        using var db=Db();db.ItemCategory.AddRange(Group(1),Group(2,"FinishedGoods"),Group(3,active:false));
        db.Parties.AddRange(new Party{PartyId=1,PartyName="Supplier",PartyType="supplier",Status="active",Phone="0421234567",Mobile="",Address="Master address"},
            new Party{PartyId=2,PartyName="Both",PartyType="both",Status="active"},new Party{PartyId=3,PartyName="Inactive",PartyType="customer",Status="inactive"},
            new Party{PartyId=4,PartyName="Group 001",PartyCode="RAW-GROUP:1",PartyType="supplier",Status="active"});
        await db.SaveChangesAsync();var c=Controller(db);
        var suppliers=Data(await c.GetPartyOptions(type:"supplier")).GetProperty("data");Assert.Equal(2,suppliers.GetArrayLength());
        var supplier=suppliers.EnumerateArray().Single(p=>p.GetProperty("Name").GetString()=="Supplier");
        Assert.Equal("0421234567",supplier.GetProperty("Phone").GetString());Assert.Equal("Master address",supplier.GetProperty("Address").GetString());
        var group=Assert.Single(Data(await c.GetPartyOptions(search:"0300111",type:"RawMaterial")).GetProperty("data").EnumerateArray());
        Assert.Equal("Group address",group.GetProperty("Address").GetString());Assert.Equal("group-1",group.GetProperty("Key").GetString());
        Assert.Equal("group-2",Assert.Single(Data(await c.GetPartyOptions(type:"FinishedGoods")).GetProperty("data").EnumerateArray()).GetProperty("Key").GetString());
        Assert.IsType<BadRequestResult>(await c.GetPartyOptions(type:"invalid"));
    }
    [Fact]
    public async Task ItemLookupFindsLaterPagesBarcodeAndGroupWithCorrectUnits()
    {
        using var db=Db();db.ItemCategory.AddRange(Group(1),Group(2,"FinishedGoods"),Group(3,active:false));
        for(var i=1;i<=52;i++)db.Items.Add(new(){ItemID=i,CategoryID=1,ItemName=$"Paper {i:D3}",IsActive=true,StockUnit="kg",Barcode=$"CODE{i}"});
        db.Items.AddRange(new Items{ItemID=53,CategoryID=2,ItemName="Finished box",IsActive=true,ItemType="FinishedGoods",StockUnit="pcs"},
            new Items{ItemID=54,CategoryID=1,ItemName="Disabled",IsActive=false,StockUnit="kg"},new Items{ItemID=55,CategoryID=3,ItemName="Inactive group",IsActive=true,StockUnit="kg"});
        await db.SaveChangesAsync();var c=Controller(db);
        Assert.Equal(13,Data(await c.GetItemOptions(page:3)).GetProperty("data").GetArrayLength());
        var item=Assert.Single(Data(await c.GetItemOptions(search:"CODE52")).GetProperty("data").EnumerateArray());Assert.Equal("kg",item.GetProperty("Unit").GetString());
        Assert.Equal(20,Data(await c.GetItemOptions(search:"Group 001")).GetProperty("data").GetArrayLength());
        item=Assert.Single(Data(await c.GetItemOptions(type:"FinishedGoods")).GetProperty("data").EnumerateArray());Assert.Equal("pcs",item.GetProperty("Unit").GetString());
        Assert.Empty(Data(await c.GetItemOptions(search:"Disabled")).GetProperty("data").EnumerateArray());Assert.Empty(Data(await c.GetItemOptions(search:"Inactive group")).GetProperty("data").EnumerateArray());
        Assert.IsType<BadRequestResult>(await c.GetItemOptions(type:"invalid"));
    }
    [Theory][InlineData("Inward")][InlineData("Outward")]
    public async Task ListFiltersPreserveManualHistoricalPartiesAndRespectBranches(string direction)
    {
        using var db=Db();db.RestrictBranches=true;db.AllowedBranchIds=[1];
        db.GatePasses.AddRange(new GatePass{Direction=direction,PartyName="Historical party",BranchId=1},
            new GatePass{Direction=direction,PartyName="Historical party",BranchId=1,IsCancelled=true},
            new GatePass{Direction=direction,PartyName="Different party",BranchId=1},
            new GatePass{Direction=direction,PartyName="Other branch",BranchId=2},
            new GatePass{Direction=direction=="Inward"?"Outward":"Inward",PartyName="Other direction",BranchId=1});
        await db.SaveChangesAsync();var c=Controller(db);
        Assert.Equal(1,Data(await c.GetList(direction,party:"Historical party",status:"active")).GetProperty("totalCount").GetInt32());
        Assert.Equal(1,Data(await c.GetList(direction,party:"Historical party",status:"cancelled")).GetProperty("totalCount").GetInt32());
        Assert.Equal(2,Data(await c.GetList(direction,party:"Historical party",status:"all")).GetProperty("totalCount").GetInt32());
        Assert.Equal(2,Data(await c.GetPassPartyOptions(direction)).GetProperty("data").GetArrayLength());
        Assert.Single(Data(await c.GetPassPartyOptions(direction,search:"Historical")).GetProperty("data").EnumerateArray());
        Assert.IsType<BadRequestObjectResult>(await c.GetList(direction,status:"invalid"));
        Assert.IsType<BadRequestObjectResult>(await c.GetList(direction,from:DateTime.Today,to:DateTime.Today.AddDays(-1)));
        Assert.IsType<BadRequestResult>(await c.GetPassPartyOptions("invalid"));
    }
    [Fact]
    public async Task TransportSuggestionsAreUniqueAndDoNotExposeAnotherBranchOrCancelledPass()
    {
        using var db=Db();db.RestrictBranches=true;db.AllowedBranchIds=[1];
        db.GatePasses.AddRange(new GatePass{PartyName="A",VehicleNumber="ABC-123",DriverName="Driver A",BranchId=1},
            new GatePass{PartyName="B",VehicleNumber="ABC-123",BranchId=1},
            new GatePass{PartyName="C",VehicleNumber="Private",BranchId=2},
            new GatePass{PartyName="D",VehicleNumber="Cancelled",BranchId=1,IsCancelled=true});
        await db.SaveChangesAsync();var data=Data(await Controller(db).GetSuggestions());
        Assert.Equal("ABC-123",Assert.Single(data.GetProperty("vehicleNumber").EnumerateArray()).GetString());
        Assert.Equal("Driver A",Assert.Single(data.GetProperty("driverName").EnumerateArray()).GetString());
    }
    [Theory][InlineData("Inward")][InlineData("Outward")]
    public async Task SavingASelectedOrManualSnapshotDoesNotChangeMastersStockOrOldPasses(string direction)
    {
        using var db=Db();db.Parties.Add(new(){PartyId=1,PartyName="Original party",Address="Original address",PartyType="customer",Status="active"});await db.SaveChangesAsync();
        var c=Controller(db);var model=new GatePass{PartyName="Original party",PartyAddress="Address on pass",PartyPhone="03001234567",Direction=direction,BranchId=1,
            GrossWeightKg=150,TareWeightKg=50,Lines=[new(){Description="Manual non-stock goods",TotalQuantity=15,PackingQuantity=2,BundleQuantity=3,Unit="pcs"}]};
        Assert.IsType<OkObjectResult>(await c.Save(model));var saved=await db.GatePasses.SingleAsync();
        Assert.Equal(100,saved.NetWeightKg);Assert.Equal(15,Assert.Single(saved.Lines).TotalQuantity); // No invented packing multiplier.
        (await db.Parties.SingleAsync()).PartyName="Renamed party";await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var reopened=Data(await c.GetById(saved.Id));Assert.Equal("Original party",reopened.GetProperty("PartyName").GetString());Assert.Equal("Address on pass",reopened.GetProperty("PartyAddress").GetString());
        Assert.IsType<ViewResult>(await c.Print(saved.Id));Assert.Single(db.Parties);Assert.Empty(db.Items);Assert.Empty(db.Stock);Assert.Empty(db.AccountLedger);
    }
}
