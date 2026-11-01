using System.Security.Claims;
using System.Text.Json;
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
public class ClientOctober25Tests
{
    private static ApplicationDbContext Db()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w=>w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static JsonElement Data(IActionResult r)=>JsonSerializer.SerializeToElement(r is JsonResult j?j.Value:((ObjectResult)r).Value);
    private static SaleController Sale(ApplicationDbContext db)=>new(db,new LedgerPostingService(db,new AccountService(db)),Options.Create(new AccountingOptions{PostToGeneralLedger=false}),new StockService(db));
    private static async Task Seed(ApplicationDbContext db){
        db.ItemCategory.AddRange(new(){CategoryId=1,CategoryName="Raw A",GroupType="RawMaterial",IsActive=true},new(){CategoryId=2,CategoryName="Buyer A",GroupType="FinishedGoods",IsActive=true},new(){CategoryId=3,CategoryName="Buyer B",GroupType="FinishedGoods",IsActive=true});
        db.Items.AddRange(new(){ItemID=1,CategoryID=1,ItemName="Paper",IsActive=true,ItemType="RawMaterial",StockUnit="kg"},new(){ItemID=2,CategoryID=2,ItemName="Box",IsActive=true,ItemType="FinishedGoods",StockUnit="pcs"},new(){ItemID=3,CategoryID=3,ItemName="Foreign box",IsActive=true,ItemType="FinishedGoods",StockUnit="pcs"},new(){ItemID=4,CategoryID=1,ItemName="Tape",IsActive=true,ItemType="RawMaterial",StockUnit="in"},new(){ItemID=5,CategoryID=1,ItemName="Glue",IsActive=true,ItemType="RawMaterial",StockUnit="kg"},new(){ItemID=6,CategoryID=1,ItemName="Special glue",MaterialRole="LaminationGlue",IsActive=true,ItemType="RawMaterial",StockUnit="kg"});await db.SaveChangesAsync();
    }
    [Theory][InlineData("Glue",null,"Glue")][InlineData("Lamination glue",null,"LaminationGlue")][InlineData("Lamination Chemicals",null,"LaminationChemical")][InlineData("Glue tape",null,"General")][InlineData("Glue","General","General")][InlineData("Custom","Silicate","Silicate")]
    public void MaterialRoleUsesExplicitMappingOrExactKnownName(string name,string? role,string expected)=>Assert.Equal(expected,MaterialRoles.Resolve(role,name));
    [Theory][InlineData("RawMaterial",1)][InlineData("FinishedGoods",2)]
    public async Task ItemGroupFilteringScopesBeforePaginationAndCombinesSearch(string type,int group){using var db=Db();await Seed(db);var c=new ItemController(db);var rows=Data(await c.GetList(itemType:type,groupId:group)).GetProperty("data");Assert.NotEmpty(rows.EnumerateArray());Assert.All(rows.EnumerateArray(),r=>Assert.Equal(group,r.GetProperty("CategoryId").GetInt32()));Assert.Empty(Data(await c.GetList(itemType:type,groupId:type=="RawMaterial"?2:1)).GetProperty("data").EnumerateArray());}
    [Fact]
    public async Task InchesSaveReadAndHistoricalUnitChangeAreProtected(){using var db=Db();await Seed(db);var c=new ItemController(db);var model=new ItemViewModel{CategoryID=1,ItemName="New inches",ItemType="RawMaterial",PageItemType="RawMaterial",StockUnit="in",IsActive=true};Assert.True(Data(await c.Save(model,null)).GetProperty("success").GetBoolean());var item=await db.Items.SingleAsync(i=>i.ItemName==model.ItemName);Assert.Equal("in",item.StockUnit);db.SaleInvoiceBody.Add(new(){SaleId=10,ItemId=item.ItemID,Quantity=1});await db.SaveChangesAsync();model.ItemID=item.ItemID;model.StockUnit="pcs";Assert.IsType<BadRequestObjectResult>(await c.Save(model,null));Assert.Equal("in",item.StockUnit);}
    [Fact]
    public async Task AdhesiveRoleRejectsNonKgAndPersistsCorrectMapping(){using var db=Db();await Seed(db);var c=new ItemController(db);var m=new ItemViewModel{CategoryID=1,ItemName="Custom adhesive",ItemType="RawMaterial",StockUnit="in",MaterialRole="Glue",IsActive=true};Assert.IsType<BadRequestObjectResult>(await c.Save(m,null));m.StockUnit="kg";Assert.True(Data(await c.Save(m,null)).GetProperty("success").GetBoolean());Assert.Equal("Glue",(await db.Items.SingleAsync(i=>i.ItemName==m.ItemName)).MaterialRole);}
    [Theory][InlineData("Inward")][InlineData("Outward")]
    public async Task GatePassSupportsActualInchesWithoutCartonArithmetic(string direction){using var db=Db();await Seed(db);var c=new GatePassController(db){ControllerContext=new(){HttpContext=new DefaultHttpContext{User=new(new ClaimsIdentity([new Claim(ClaimTypes.Name,"QA")],"QA"))}}};var m=new GatePass{Direction=direction,PartyName="Raw A",GroupId=1,Lines=[new(){ItemId=4,Description="Tape",Unit="in",TotalQuantity=12.375m,UnitPrice=2}]};Assert.IsType<OkObjectResult>(await c.Save(m));Assert.Equal(12.375m,(await db.GatePassLines.SingleAsync()).TotalQuantity);Assert.Contains(Data(await c.GetItemOptions(groupId:1)).GetProperty("data").EnumerateArray(),i=>i.GetProperty("ItemId").GetInt64()==4);Assert.Empty(db.Stock);}
    [Fact]
    public async Task SalesCustomerGroupsAreReadOnlyAndOnlyFinishedGoods(){using var db=Db();await Seed(db);var rows=Data(await Sale(db).GetCustomerGroups()).GetProperty("customers");Assert.Equal(2,rows.GetArrayLength());Assert.All(rows.EnumerateArray(),r=>Assert.StartsWith("group:",r.GetProperty("partyId").GetString()));Assert.Empty(db.Parties);}
    [Fact]
    public async Task SalesLookupFiltersGroupSearchTypedIdAndPagination(){using var db=Db();await Seed(db);var c=Sale(db);Assert.Single(Data(await c.GetItemOptions("group:2")).GetProperty("items").EnumerateArray());Assert.Empty(Data(await c.GetItemOptions("group:2",itemId:3)).GetProperty("items").EnumerateArray());Assert.Empty(Data(await c.GetItemOptions("group:2",search:"Foreign")).GetProperty("items").EnumerateArray());Assert.IsType<BadRequestObjectResult>(await c.GetItemOptions("group:1"));for(var i=20;i<46;i++)db.Items.Add(new(){ItemID=i,CategoryID=2,ItemName="Box "+i,ItemType="FinishedGoods",StockUnit="pcs",IsActive=true});await db.SaveChangesAsync();Assert.All(Data(await c.GetItemOptions("group:2",page:2,pageSize:20)).GetProperty("items").EnumerateArray(),i=>Assert.Equal(2,i.GetProperty("categoryID").GetInt32()));}
    [Theory][InlineData(1)][InlineData(3)]
    public async Task GroupSalesRejectForeignItemsBeforeCreatingAccountingParty(long item){using var db=Db();await Seed(db);var m=new SaleInvoiceViewModel{CustomerID="group:2",SaleDate=DateTime.Today,BranchID=1,PaymentMode=0,Items=[new(){ItemId=item,Quantity=1,SalePrice=10}]};Assert.IsType<BadRequestObjectResult>(await Sale(db).Save(m));Assert.Empty(db.Parties);Assert.Empty(db.SaleInvoice);Assert.Empty(db.Stock);}
    [Fact]
    public async Task GroupSalesReuseCanonicalPartyAndEnforceGroupOnEdit(){using var db=Db();await Seed(db);db.Stock.Add(new(){ItemId=2,BranchId=1,Quantity=10});await db.SaveChangesAsync();var c=Sale(db);var m=new SaleInvoiceViewModel{CustomerID="group:2",SaleDate=DateTime.Today,BranchID=1,PaymentMode=0,Items=[new(){ItemId=2,Desc="Box",Quantity=2,SalePrice=10}]};Assert.IsType<OkObjectResult>(await c.Save(m));Assert.Equal("FG-GROUP:2",(await db.Parties.SingleAsync()).PartyCode);m.SaleId=(await db.SaleInvoice.SingleAsync()).SaleId;m.Items[0].ItemId=3;Assert.IsType<BadRequestObjectResult>(await c.Update(m));Assert.Equal(8,(await db.Stock.SingleAsync()).Quantity);Assert.Equal(2,(await db.SaleInvoiceBody.SingleAsync()).ItemId);}
    [Theory][InlineData(3,false,1,0)][InlineData(5,true,2,1)]
    public async Task AutomaticApplicationsAreRecomputedOnServer(int count,bool lam,int expected,int expectedLam){var m=new CostSheetViewModel{BoxStyle="Regular Slotted Carton (RSC)",Length=300,Width=200,Height=100,MOQ=1,SizeUnit="mm",HasLamination=lam,ClientCosting=new(){Version=2,AutomaticApplications=true,GlueApplications=99,SilicateApplications=99,GlueLaminationApplications=99,LaminationChemicalApplications=99},Plies=Enumerable.Range(0,count).Select(_=>new CostSheetPlyViewModel{GSM=100,Rate=10}).ToList()};ClientCartonCalculation.Prepare(m);Assert.Equal(expected,m.ClientCosting.GlueApplications);Assert.Equal(expected,m.ClientCosting.SilicateApplications);Assert.Equal(expectedLam,m.ClientCosting.GlueLaminationApplications);Assert.Equal(expectedLam,m.ClientCosting.LaminationChemicalApplications);}
    [Fact]
    public async Task MaterialOptionsExposeRolesAndLatestUncancelledPurchaseRates(){using var db=Db();await Seed(db);db.PurchaseInvoice.AddRange(new(){PurchaseId=1,BranchID=1},new(){PurchaseId=2,BranchID=1,IsCancelled=true});db.PurchaseInvoiceBody.AddRange(new(){Srno=1,PurchaseId=1,Itemid=5,PurPrice=25},new(){Srno=2,PurchaseId=2,Itemid=5,PurPrice=99});await db.SaveChangesAsync();var c=new CostSheetController(db,new PermissionService(db));var rows=Data(await c.GetMaterialOptions()).GetProperty("items");var glue=rows.EnumerateArray().Single(i=>i.GetProperty("id").GetInt64()==5);Assert.Equal("Glue",glue.GetProperty("materialRole").GetString());Assert.Equal(25,glue.GetProperty("lastPurchasePrice").GetDecimal());Assert.Equal("LaminationGlue",rows.EnumerateArray().Single(i=>i.GetProperty("id").GetInt64()==6).GetProperty("materialRole").GetString());}
    [Fact]
    public async Task InchesOtherMaterialAddsCostWithoutAddingKgAndProductionConsumesExactLength()
    {
        using var db=Db();await Seed(db);db.Parties.Add(new(){PartyId=7,PartyName="Buyer",PartyType="customer",Status="active"});
        db.Stock.AddRange(new(){ItemId=1,BranchId=1,Quantity=100},new(){ItemId=4,BranchId=1,Quantity=50});await db.SaveChangesAsync();
        var sheet=new CostSheetViewModel{ItemName="Inch tape boxes",FinishedItemId=2,Status="Final",BranchID=1,Plies=[new(){ItemId=1,KGs=.5m,Rate=10}],Materials=[new(){ItemId=4,Quantity=1.2375m,Rate=2}]};
        Assert.IsType<OkObjectResult>(await new CostSheetController(db,new PermissionService(db)).Save(sheet));
        var saved=await db.CostSheet.Include(c=>c.Materials).SingleAsync();Assert.Equal("in",Assert.Single(saved.Materials).Unit);
        var pc=new ProductionController(db,new StockService(db));Assert.IsType<OkObjectResult>(await pc.Save(new(){CostSheetId=saved.CostSheetId,CustomerId="7",OrderQty=10,AgreedUnitPrice=20,OrderDate=DateTime.Today,DeliveryDate=DateTime.Today}));
        var order=await db.ProductionOrder.SingleAsync();var service=new EstimatedProductionService(db,new StockService(db));await service.RecordAsync(order.ProductionOrderId,10);
        Assert.Equal(37.625m,(await db.Stock.SingleAsync(st=>st.ItemId==4)).Quantity);Assert.Equal("in",(await db.ProductionConsumption.SingleAsync(p=>p.ItemId==4)).Unit);
        await service.ReverseAsync(order.ProductionOrderId,"QA reversal");Assert.Equal(50,(await db.Stock.SingleAsync(st=>st.ItemId==4)).Quantity);
    }
    [Fact]
    public async Task ClassifiedAdhesivesRejectForeignUseAndLegacyApplicationsRemainUnchanged()
    {
        using var db=Db();await Seed(db);
        var legacy=new CostSheetViewModel{BoxStyle="Regular Slotted Carton (RSC)",Length=300,Width=200,Height=100,MOQ=1,SizeUnit="mm",ClientCosting=new(){Version=2,GlueApplications=2,SilicateApplications=2},Plies=Enumerable.Range(0,3).Select(_=>new CostSheetPlyViewModel{GSM=100,Rate=10}).ToList()};
        ClientCartonCalculation.Prepare(legacy);Assert.Equal(2,legacy.ClientCosting.GlueApplications);
        legacy.ItemName="Mislinked";legacy.Status="Draft";legacy.ClientCosting.AutomaticApplications=true;legacy.GlueItemId=6;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>CostSheetCalculation.ValidateAndCalculateAsync(db,legacy));
    }

}
