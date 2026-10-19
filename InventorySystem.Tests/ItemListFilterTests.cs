using System.Security.Claims;
using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Tests;

public class ItemListFilterTests
{
    private static ApplicationDbContext Database()
    {
        var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.ItemCategory.AddRange(new ItemCategory { CategoryId=1, CategoryName="Paper group" },new ItemCategory { CategoryId=2, CategoryName="Box group" });
        for(var id=1;id<=60;id++) db.Items.Add(new Items { ItemID=id, CategoryID=1, ItemName="Paper "+id, ItemType="RawMaterial", IsActive=true });
        for(var id=61;id<=63;id++) db.Items.Add(new Items { ItemID=id, CategoryID=2, ItemName="Box "+id, ItemType="FinishedGoods", IsActive=id!=62 });
        db.SaveChanges(); return db;
    }
    private static ItemController Controller(ApplicationDbContext db,string role="Admin") => new(db) {
        ControllerContext=new ControllerContext { HttpContext=new DefaultHttpContext { User=new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Role,role)],"Test")) } } };
    private static JsonElement Data(JsonResult result)=>JsonSerializer.SerializeToElement(result.Value);

    [Theory]
    [InlineData("RawMaterial",60)] [InlineData("FinishedGoods",3)]
    public async Task TypeFilterAppliesBeforePagingAndCountsAllMatchingItems(string type,int total)
    {
        await using var db=Database(); var controller=Controller(db);
        var first=Data(await controller.GetList(pageSize:1,itemType:type));
        var second=Data(await controller.GetList(page:2,pageSize:1,itemType:type));
        Assert.Equal(total,first.GetProperty("totalCount").GetInt32());
        Assert.Single(first.GetProperty("data").EnumerateArray());
        Assert.Equal(type,first.GetProperty("data")[0].GetProperty("ItemType").GetString());
        Assert.Equal(type,second.GetProperty("data")[0].GetProperty("ItemType").GetString());
        Assert.NotEqual(first.GetProperty("data")[0].GetProperty("ItemID").GetInt64(),second.GetProperty("data")[0].GetProperty("ItemID").GetInt64());
    }

    [Fact]
    public async Task TypeStatusAndSearchCombineAndCanBeCleared()
    {
        await using var db=Database(); var controller=Controller(db);
        var filtered=Data(await controller.GetList(itemType:"FinishedGoods",status:"Inactive",search:"bOx GrOuP"));
        Assert.Equal(1,filtered.GetProperty("totalCount").GetInt32());
        Assert.Equal(62,filtered.GetProperty("data")[0].GetProperty("ItemID").GetInt64());
        var empty=Data(await controller.GetList(itemType:"RawMaterial",search:"Box"));
        Assert.Equal(0,empty.GetProperty("totalCount").GetInt32());
        Assert.Equal(63,Data(await controller.GetList()).GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("Paper group",null)] [InlineData("Invalid",null)] [InlineData(null,"Unknown")]
    public async Task InvalidFiltersAreRejected(string? type,string? status)
    {
        await using var db=Database(); Assert.Equal(400,(await Controller(db).GetList(itemType:type,status:status)).StatusCode);
    }

    [Theory]
    [InlineData("Admin")] [InlineData("User")]
    public async Task NonSuperAdminCannotDeleteOrReceiveDeleteCapability(string role)
    {
        await using var db=Database(); var controller=Controller(db,role);
        Assert.False(Data(await controller.GetList()).GetProperty("canDelete").GetBoolean());
        Assert.Equal(403,(await controller.Delete(new DeleteRequest { Id=1 })).StatusCode);
        Assert.True((await db.Items.FindAsync(1L))!.IsActive);
    }

    [Fact]
    public async Task SuperAdminCanDeactivateItemAndRetainHistory()
    {
        await using var db=Database(); var controller=Controller(db,"SuperAdmin");
        Assert.True(Data(await controller.GetList()).GetProperty("canDelete").GetBoolean());
        Assert.True(Data(await controller.Delete(new DeleteRequest { Id=1 })).GetProperty("success").GetBoolean());
        Assert.False((await db.Items.FindAsync(1L))!.IsActive); Assert.Equal(63,await db.Items.CountAsync());
    }
}
