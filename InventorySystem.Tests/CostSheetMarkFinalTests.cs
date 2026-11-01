using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Tests;
public class CostSheetMarkFinalTests
{
    private static ApplicationDbContext Database()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    [Fact] public async Task DraftWithPaperWeightBecomesFinal()
    {
        await using var db=Database();
        db.CostSheet.Add(new CostSheet{CostSheetId=1,Status="Draft",Plies=[new CostSheetPly{SrNo=1,KGs=0.5m}]});db.SaveChanges();
        var controller=new CostSheetController(db,new PermissionService(db));
        Assert.IsType<OkObjectResult>(await controller.MarkFinal(1));
        Assert.Equal("Final",(await db.CostSheet.SingleAsync()).Status);
    }
    [Fact] public async Task DraftWithoutPaperWeightStaysDraft()
    {
        await using var db=Database();
        db.CostSheet.Add(new CostSheet{CostSheetId=1,Status="Draft",Plies=[new CostSheetPly{SrNo=1,KGs=0}]});db.SaveChanges();
        var controller=new CostSheetController(db,new PermissionService(db));
        Assert.IsType<BadRequestObjectResult>(await controller.MarkFinal(1));
        Assert.Equal("Draft",(await db.CostSheet.SingleAsync()).Status);
    }
    [Fact] public async Task MissingSheetIsNotFound()
    {
        await using var db=Database();
        Assert.IsType<NotFoundResult>(await new CostSheetController(db,new PermissionService(db)).MarkFinal(99));
    }
}
