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
using InventorySystem.Authorization;
using InventorySystem.Core.Options;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InventorySystem.Tests;
public class ClientOctober21Tests
{
    private static ApplicationDbContext Db()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w=>w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static DefaultHttpContext Http()=>new(){User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"QA Admin"),new Claim(ClaimTypes.Role,"Admin")},"QA"))};
    private static JsonElement Data(IActionResult result)=>JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(result).Value);
    [Theory][InlineData("Draft")][InlineData("Final")]
    public async Task ImportedCostingSavesWithoutInventoryLinksAndRetainsExcelTotal(string status)
    {
        using var db=Db();using var file=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"TestData/LatestClientCosting.xlsx"));
        var m=CostSheetExcelImport.Read(file).Data;m.Status=status;m.BranchID=1;
        Assert.All(m.Plies,p=>Assert.Null(p.ItemId));
        var controller=new CostSheetController(db,new PermissionService(db));
        Assert.IsType<OkObjectResult>(await controller.Save(m));
        var saved=await db.CostSheet.Include(c=>c.Plies).SingleAsync();
        Assert.Equal(status,saved.Status);Assert.Null(saved.FinishedItemId);Assert.Null(saved.GlueItemId);Assert.Null(saved.SilicateItemId);
        Assert.Equal(61.27m,saved.FinalRateWithGST);Assert.Equal(3,saved.Plies!.Count);Assert.All(saved.Plies,p=>Assert.Null(p.ItemId));Assert.Empty(db.Stock);
        m.CostSheetId=saved.CostSheetId;m.ProfitPct=10;Assert.IsType<OkObjectResult>(await controller.Save(m));Assert.Single(db.CostSheet);Assert.Equal(3,await db.CostSheetPly.CountAsync());
    }
    [Fact]
    public async Task CostingCanSaveUnlinkedLaminationButProductionCannotOmitUnlinkedMaterials()
    {
        using var db=Db();using var file=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"TestData/LatestClientCosting.xlsx"));
        var m=CostSheetExcelImport.Read(file).Data;m.Status="Final";m.BranchID=1;m.HasLamination=true;m.ClientCosting!.GlueLaminationApplications=1;m.ClientCosting.LaminationChemicalApplications=1;
        Assert.IsType<OkObjectResult>(await new CostSheetController(db,new PermissionService(db)).Save(m));
        var saved=await db.CostSheet.Include(c=>c.Plies).Include(c=>c.Materials).SingleAsync();
        var service=new EstimatedProductionService(db,new StockService(db));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SnapshotAsync(new(){CustomerId="1",OrderQty=10},saved));
        db.ItemCategory.Add(new(){CategoryId=1,CategoryName="Boxes",IsActive=true});db.Items.Add(new(){ItemID=1,CategoryID=1,ItemType="FinishedGoods",StockUnit="pcs",IsActive=true,ItemName="Box"});db.Parties.Add(new(){PartyId=1,PartyName="Buyer",PartyType="customer",Status="active"});await db.SaveChangesAsync();saved.FinishedItemId=1;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SnapshotAsync(new(){CustomerId="1",OrderQty=10},saved));Assert.Empty(db.Stock);
    }
    [Theory][InlineData("RawMaterial",1)][InlineData("FinishedGoods",2)]
    public async Task ScopedItemPagesCreateUpdateAndRejectCrossSectionEdits(string type,int group)
    {
        using var db=Db();db.ItemCategory.AddRange(new ItemCategory{CategoryId=1,CategoryName="Raw",GroupType="RawMaterial",IsActive=true},new ItemCategory{CategoryId=2,CategoryName="Finished",GroupType="FinishedGoods",IsActive=true});db.SaveChanges();
        var controller=new ItemController(db){ControllerContext=new(){HttpContext=Http()}};
        var model=new ItemViewModel{CategoryID=group,ItemName="Scoped item",ItemType=type,PageItemType=type,StockUnit=type=="RawMaterial"?"kg":"pcs",IsActive=true};
        Assert.True(Data(await controller.Save(model,null)).GetProperty("success").GetBoolean());var saved=await db.Items.SingleAsync();model.ItemID=saved.ItemID;model.ItemName="Updated";
        Assert.True(Data(await controller.Save(model,null)).GetProperty("success").GetBoolean());Assert.Equal("Updated",saved.ItemName);
        var opposite=type=="RawMaterial"?"FinishedGoods":"RawMaterial";
        Assert.IsType<NotFoundResult>(await controller.GetItemById(saved.ItemID,opposite));
        model.PageItemType=opposite;Assert.IsType<BadRequestObjectResult>(await controller.Save(model,null));Assert.Equal(type,saved.ItemType);
        var rows=Data(await controller.GetList(itemType:type));Assert.Equal(type,rows.GetProperty("data")[0].GetProperty("ItemType").GetString());Assert.Equal(saved.StockUnit,rows.GetProperty("data")[0].GetProperty("StockUnit").GetString());
    }
    [Theory][InlineData("RawMaterial",1)][InlineData("FinishedGoods",2)]
    public async Task StockGroupDropdownContainsOnlyTheReportType(string type,int expected)
    {
        using var db=Db();db.ItemCategory.AddRange(new ItemCategory{CategoryId=1,CategoryName="Raw",GroupType="RawMaterial"},new ItemCategory{CategoryId=2,CategoryName="Finished",GroupType="FinishedGoods"});db.SaveChanges();
        var rows=Data(await new ReportController(db).GetCategoryList(type));Assert.Equal(expected,Assert.Single(rows.EnumerateArray()).GetProperty("value").GetInt32());
        Assert.IsType<BadRequestResult>(await new ReportController(db).GetCategoryList("bad"));
    }
    [Fact]
    public async Task MenuUpgradeMovesPurchasePagesAddsFinishedItemsAndGatePassesOnce()
    {
        using var db=Db();await db.Database.EnsureCreatedAsync();await DefaultMenuSeeder.ApplyAsync(db);await ClientWorkflowMenuSeeder.ApplyAsync(db);
        var invoices=await db.MenuItems.SingleAsync(m=>m.Name=="Invoices");
        Assert.Equal(new[]{"Purchase Invoices","Purchase Report","Sales Invoices","Sales Report"},await db.MenuItems.Where(m=>m.ParentId==invoices.Id&&m.IsActive).OrderBy(m=>m.SortOrder).Select(m=>m.Name).ToArrayAsync());
        Assert.Equal("Finished Goods",(await db.MenuItems.Include(m=>m.Parent).SingleAsync(m=>m.Controller=="Item"&&m.Action=="FinishedGoods")).Parent!.Name);
        Assert.Equal(2,await db.MenuItems.CountAsync(m=>m.Controller=="GatePass"));
        var count=await db.MenuItems.CountAsync();await ClientWorkflowMenuSeeder.ApplyAsync(db);Assert.Equal(count,await db.MenuItems.CountAsync());
        Assert.Single(await db.Permissions.Where(p=>p.Code=="GatePass.View").ToListAsync());
    }
    private static GatePass Pass(string direction)=>new(){Direction=direction,BranchId=1,PartyName="QA Party",PartyAddress="Lahore",PartyPhone="03001234567",VehicleName="Truck",VehicleNumber="ABC-123",DriverName="QA Driver",DriverPhone="03007654321",TransportCompany="QA Transport",BiltyNumber="BLT-1",ArrivalAt=DateTime.Today.AddHours(9),DepartureAt=DateTime.Today.AddHours(10),GrossWeightKg=1500,TareWeightKg=500,NetWeightKg=999,IssuedBy="Store",DriverSignature="Driver",GateKeeper="Guard",ReceivedBy="Receiver",ReceiverReference="REF-1",Remarks="QA pass",Lines=[new(){Description="Cartons",PackingType="3-Ply",PackingQuantity=50,BundleQuantity=2,TotalQuantity=100,Unit="pcs"}]};
    [Theory]
    [InlineData("Save",0,"Create")][InlineData("Save",42,"Edit")][InlineData("Cancel",42,"Edit")][InlineData("Delete",42,"Delete")]
    public async Task GatePassAdminPermissionsPassTheRealOperationFilter(string action,int id,string verb)
    {
        using var db=Db();await db.Database.EnsureCreatedAsync();await DefaultMenuSeeder.ApplyAsync(db);await ClientWorkflowMenuSeeder.ApplyAsync(db);
        var role=await db.Roles.SingleAsync(r=>r.Name=="Admin");
        db.AppUsers.Add(new(){Id=900,Username="gate-qa",FullName="Gate QA",IsActive=true,BranchIds="1"});
        db.UserRoles.Add(new(){UserId=900,RoleId=role.Id});await db.SaveChangesAsync();
        var permissions=new PermissionService(db);
        foreach(var code in new[]{"GatePass.View","GatePass.Create","GatePass.Edit","GatePass.Delete"})Assert.True(await permissions.HasPermissionAsync(900,code));
        var unused=verb=="Create"?"GatePass.Edit":"GatePass.Create";
        db.RolePermissions.RemoveRange(await db.RolePermissions.Where(p=>p.RoleId==role.Id&&p.Permission!.Code==unused).ToListAsync());await db.SaveChangesAsync();db.ChangeTracker.Clear();
        async Task<(bool ran,ActionExecutingContext context)> Run()
        {
            var http=new DefaultHttpContext{User=new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"900")],"QA"))};http.Request.Method="POST";
            var route=new RouteData();route.Values["controller"]="GatePass";route.Values["action"]=action;
            object model=action=="Cancel"?new GatePassController.CancelRequest{Id=id,Revision=1,Reason="QA cancellation"}:new GatePass{Id=id,BranchId=1,PartyName="QA"};
            var context=new ActionExecutingContext(new ActionContext(http,route,new ActionDescriptor(),new ModelStateDictionary()),[],new Dictionary<string,object>{{"model",model}},new object());
            var filter=new OperationFilter(db,permissions,NullLogger<OperationFilter>.Instance,Options.Create(new BranchOptions()));var ran=false;
            await filter.OnActionExecutionAsync(context,()=>{ran=true;return Task.FromResult(new ActionExecutedContext(context,[],context.Controller){Result=new OkObjectResult(new{success=true})});});
            return(ran,context);
        }
        var allowed=await Run();
        // Editing a saved pass is now SuperAdmin-only, even with the Edit permission.
        if(action=="Save"&&id>0){Assert.False(allowed.ran);Assert.Equal(403,Assert.IsType<ObjectResult>(allowed.context.Result).StatusCode);return;}
        Assert.True(allowed.ran);Assert.Null(allowed.context.Result);
        db.RolePermissions.RemoveRange(await db.RolePermissions.Where(p=>p.RoleId==role.Id&&p.Permission!.Code=="GatePass."+verb).ToListAsync());await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var denied=await Run();Assert.False(denied.ran);Assert.IsType<ForbidResult>(denied.context.Result);
        await ClientWorkflowMenuSeeder.ApplyAsync(db);await ClientWorkflowMenuSeeder.ApplyAsync(db);
        Assert.Equal(4,await db.Permissions.CountAsync(p=>p.Module=="GatePass"));
        Assert.Equal(4,await db.RolePermissions.CountAsync(p=>p.RoleId==role.Id&&p.Permission!.Module=="GatePass"));
    }
    [Theory][InlineData("Inward")][InlineData("Outward")]
    public async Task GatePassCreateReadUpdatePrintCancelAndFiltersPreserveAllData(string direction)
    {
        using var db=Db();var c=new GatePassController(db){ControllerContext=new(){HttpContext=Http()}};var model=Pass(direction);
        Assert.IsType<OkObjectResult>(await c.Save(model));var saved=await db.GatePasses.Include(g=>g.Lines).SingleAsync();
        Assert.Equal(1000,saved.NetWeightKg);Assert.Equal("ABC-123",saved.VehicleNumber);Assert.Equal("BLT-1",saved.BiltyNumber);Assert.Equal("REF-1",saved.ReceiverReference);Assert.Equal("QA Admin",saved.CreatedBy);Assert.Equal(100,Assert.Single(saved.Lines).TotalQuantity);
        Assert.Equal(saved.Id,Data(await c.GetById(saved.Id)).GetProperty("Id").GetInt32());Assert.IsType<ViewResult>(await c.Print(saved.Id));
        model.Id=saved.Id;model.Revision=saved.Revision;model.PartyName="Updated Party";model.Lines[0].PackingType="5-Ply";model.Lines[0].TotalQuantity=120;
        Assert.IsType<OkObjectResult>(await c.Save(model));Assert.Equal(2,saved.Revision);Assert.Equal("5-Ply",Assert.Single(saved.Lines).PackingType);Assert.Equal(1,await db.GatePassLines.CountAsync());
        Assert.IsType<ConflictObjectResult>(await c.Save(model));
        var active=Data(await c.GetList(direction,"Updated",DateTime.Today,DateTime.Today));Assert.Equal(1,active.GetProperty("totalCount").GetInt32());
        var numbered=Data(await c.GetList(direction,saved.PassNumber));Assert.Equal(1,numbered.GetProperty("totalCount").GetInt32());
        Assert.Equal(1,numbered.GetProperty("data")[0].GetProperty("ItemCount").GetInt32());
        Assert.IsType<OkObjectResult>(await c.Cancel(new(){Id=saved.Id,Revision=saved.Revision,Reason="QA cancelled"}));
        Assert.True(saved.IsCancelled);Assert.Equal("QA cancelled",saved.CancellationReason);
        Assert.Equal(0,Data(await c.GetList(direction)).GetProperty("totalCount").GetInt32());Assert.Equal(1,Data(await c.GetList(direction,cancelledOnly:true)).GetProperty("totalCount").GetInt32());
        model.Revision=saved.Revision;Assert.IsType<BadRequestObjectResult>(await c.Save(model));Assert.Single(db.GatePasses);Assert.Empty(db.Stock);Assert.Empty(db.AccountLedger);
    }
    [Fact]
    public async Task GatePassRejectsWrongBranchInvalidWeightsAndQuantities()
    {
        using var db=Db();db.RestrictBranches=true;db.AllowedBranchIds=[1];var c=new GatePassController(db){ControllerContext=new(){HttpContext=Http()}};
        var model=Pass("Outward");model.BranchId=2;Assert.IsType<BadRequestObjectResult>(await c.Save(model));
        model=Pass("Outward");model.TareWeightKg=2000;Assert.IsType<BadRequestObjectResult>(await c.Save(model));
        model=Pass("Outward");model.Lines[0].TotalQuantity=0;Assert.IsType<BadRequestObjectResult>(await c.Save(model));
        model=Pass("Outward");model.PartyName=new string('A',201);Assert.IsType<BadRequestObjectResult>(await c.Save(model));
        model=Pass("Outward");model.DepartureAt=model.ArrivalAt!.Value.AddHours(-1);Assert.IsType<BadRequestObjectResult>(await c.Save(model));
        db.GatePasses.Add(new GatePass{Direction="Inward",PartyName="Other branch",BranchId=2});await db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await c.GetById(1));Assert.IsType<NotFoundResult>(await c.Print(1));Assert.Equal(0,Data(await c.GetList("Inward")).GetProperty("totalCount").GetInt32());
        Assert.IsType<NotFoundResult>(await c.Cancel(new(){Id=1,Revision=1,Reason="Not allowed"}));
    }
}
