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

public class GroupContactTests
{
    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static CategoryController Controller(ApplicationDbContext db, string role = "Admin") => new(db) {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role,role)],"Test")) } } };
    private static JsonElement Data(JsonResult r) => JsonSerializer.SerializeToElement(r.Value);
    private static ItemCategoryViewModel Group() => new() { CategoryName="Contact test", GroupType="RawMaterial", IsActive=true,
        Address="  Industrial Estate, Multan  ", ContactNumber="  +92 (312) 777-7167  " };

    [Fact]
    public async Task AdminCanSaveReadEditAndClearContactFields()
    {
        await using var db=Database(); var controller=Controller(db); var m=Group();
        Assert.True(Data(await controller.Save(m)).GetProperty("success").GetBoolean());
        var saved=await db.ItemCategory.SingleAsync();
        Assert.Equal("Industrial Estate, Multan",saved.Address); Assert.Equal("+92 (312) 777-7167",saved.ContactNumber);
        var loaded=Data(Assert.IsType<JsonResult>(await controller.GetById(saved.CategoryId)));
        Assert.Equal(saved.Address,loaded.GetProperty("Address").GetString());
        Assert.Equal(saved.ContactNumber,loaded.GetProperty("ContactNumber").GetString());
        m.CategoryId=saved.CategoryId; m.Address="New location"; m.ContactNumber="0306-7777167";
        Assert.True(Data(await controller.Save(m)).GetProperty("success").GetBoolean());
        Assert.Equal(m.Address,saved.Address); Assert.Equal(m.ContactNumber,saved.ContactNumber);
        m.Address=" "; m.ContactNumber="";
        Assert.True(Data(await controller.Save(m)).GetProperty("success").GetBoolean());
        Assert.Null(saved.Address); Assert.Null(saved.ContactNumber);
    }

    [Theory]
    [InlineData("Admin")] [InlineData("User")] [InlineData("")]
    public async Task NonSuperAdminCannotDeleteEvenThroughDirectRequest(string role)
    {
        await using var db=Database(); db.ItemCategory.Add(new() { CategoryName="Protected" }); await db.SaveChangesAsync();
        var controller=Controller(db,role);
        var response=await controller.Delete((await db.ItemCategory.SingleAsync()).CategoryId);
        Assert.Equal(403,response.StatusCode); Assert.False(Data(response).GetProperty("success").GetBoolean());
        Assert.Single(await db.ItemCategory.ToListAsync());
        Assert.False(Data(Assert.IsType<JsonResult>(controller.GetList())).GetProperty("canDelete").GetBoolean());
    }

    [Fact]
    public async Task SuperAdminCanDeleteUnusedGroup()
    {
        await using var db=Database(); var controller=Controller(db,"SuperAdmin");
        Assert.True(Data(await controller.Save(Group())).GetProperty("success").GetBoolean());
        Assert.True(Data(Assert.IsType<JsonResult>(controller.GetList())).GetProperty("canDelete").GetBoolean());
        Assert.True(Data(await controller.Delete((await db.ItemCategory.SingleAsync()).CategoryId)).GetProperty("success").GetBoolean());
        Assert.Empty(await db.ItemCategory.ToListAsync());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task OverlongContactFieldsDoNotSave(bool address)
    {
        await using var db=Database(); var m=Group();
        if(address) m.Address=new string('a',501); else m.ContactNumber=new string('1',33);
        Assert.False(Data(await Controller(db).Save(m)).GetProperty("success").GetBoolean());
        Assert.Empty(await db.ItemCategory.ToListAsync());
    }

    [Fact]
    public async Task ChangedContactDuringStatusReviewRequiresNewConfirmation()
    {
        await using var db=Database(); var controller=Controller(db); var m=Group();
        await controller.Save(m); m.CategoryId=(await db.ItemCategory.SingleAsync()).CategoryId; m.IsActive=false;
        var preview=Data(await controller.Save(m));
        Assert.True(preview.GetProperty("requiresConfirmation").GetBoolean());
        m.StatusConfirmationToken=preview.GetProperty("confirmationToken").GetString();
        m.Address="Changed after review";
        var next=Data(await controller.Save(m));
        Assert.True(next.GetProperty("requiresConfirmation").GetBoolean());
        Assert.NotEqual(m.StatusConfirmationToken,next.GetProperty("confirmationToken").GetString());
        Assert.True((await db.ItemCategory.SingleAsync()).IsActive);
        m.StatusConfirmationToken=next.GetProperty("confirmationToken").GetString();
        Assert.True(Data(await controller.Save(m)).GetProperty("success").GetBoolean());
        Assert.Equal(m.Address,(await db.ItemCategory.SingleAsync()).Address);
        Assert.False((await db.ItemCategory.SingleAsync()).IsActive);
    }
}
