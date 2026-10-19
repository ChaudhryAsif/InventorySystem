using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Tests;

public class ItemSaveTests
{
    private static ApplicationDbContext Database()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.ItemCategory.Add(new ItemCategory { CategoryId = 700, CategoryName = "Paper", IsActive = true });
        db.SaveChanges(); return db;
    }
    private static ItemViewModel NewItem() => new() { ItemID = 0, CategoryID = 700, ItemName = "Paper", ItemType = "RawMaterial", StockUnit = "kg", SalePrice = 0, IsActive = true };

    [Fact]
    public async Task CreateThenEdit_PreservesIdentity_AndDoesNotAddStock()
    {
        await using var db = Database();
        var controller = new ItemController(db);
        var model = NewItem();
        model.Barcode = "QA-PAPER-001";
        model.ReorderLevel = 12;
        Assert.IsType<JsonResult>(await controller.Save(model, null));
        var saved = await db.Items.SingleAsync();
        Assert.True(saved.ItemID > 0);
        Assert.Equal(0m, saved.SalePrice);
        model.ItemID = saved.ItemID; model.ItemName = "Updated paper";
        Assert.IsType<JsonResult>(await controller.Save(model, null));
        Assert.Equal(saved.ItemID, (await db.Items.SingleAsync()).ItemID);
        Assert.Equal("Updated paper", saved.ItemName);
        Assert.Equal("QA-PAPER-001", saved.Barcode);
        Assert.Equal(12, saved.ReorderLevel);
        Assert.Empty(await db.Stock.ToListAsync());
    }

    [Fact]
    public async Task InvalidBinding_DoesNotCreateAnItem()
    {
        await using var db = Database();
        var controller = new ItemController(db);
        controller.ModelState.AddModelError("ItemID", "The value is invalid.");
        Assert.IsType<BadRequestObjectResult>(await controller.Save(NewItem(), null));
        Assert.Empty(await db.Items.ToListAsync());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task NegativeIdentity_IsRejected(long id)
    {
        await using var db = Database();
        var model = NewItem(); model.ItemID = id;
        Assert.IsType<BadRequestObjectResult>(await new ItemController(db).Save(model, null));
        Assert.Empty(await db.Items.ToListAsync());
    }

    [Fact]
    public async Task MissingEditIdentity_DoesNotCreateNewItem()
    {
        await using var db = Database();
        var model = NewItem(); model.ItemID = 12345;
        Assert.IsType<NotFoundResult>(await new ItemController(db).Save(model, null));
        Assert.Empty(await db.Items.ToListAsync());
    }

    [Fact]
    public async Task LoadForEdit_ReturnsCompleteItem_WithLongIdentity()
    {
        await using var db = Database();
        db.Items.Add(new Items { ItemID = 3_000_000_001L, ItemName = "Long ID paper", CategoryID = 700,
            Barcode = "QA-LONG-ID", ReorderLevel = 25, Size = "Medium", Description = "Saved description",
            StockUnit = "kg", ItemType = "RawMaterial", SalePrice = 0, IsActive = true });
        await db.SaveChangesAsync();
        var result = Assert.IsType<JsonResult>(await new ItemController(db).GetItemById(3_000_000_001L));
        var data = System.Text.Json.JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal(3_000_000_001L, data.GetProperty("ItemID").GetInt64());
        Assert.Equal("Long ID paper", data.GetProperty("ItemName").GetString());
        Assert.Equal("QA-LONG-ID", data.GetProperty("Barcode").GetString());
        Assert.Equal("Saved description", data.GetProperty("Description").GetString());
        Assert.Equal(25, data.GetProperty("ReorderLevel").GetDecimal());
    }

    [Fact]
    public async Task Delete_Deactivates_WhilePreservingStockAndItemDetails()
    {
        await using var db = Database();
        var model = NewItem(); model.Barcode = "QA-KEEP";
        var controller = new ItemController(db) { ControllerContext = new ControllerContext {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role,"SuperAdmin")],"Test")) } } };
        await controller.Save(model, null);
        var saved = await db.Items.SingleAsync();
        db.Stock.Add(new Stock { ItemId = saved.ItemID, BranchId = 1, Quantity = 10 });
        await db.SaveChangesAsync();
        await controller.Delete(new DeleteRequest { Id = saved.ItemID });
        Assert.False(saved.IsActive);
        Assert.Single(await db.Items.ToListAsync());
        Assert.Equal("QA-KEEP", saved.Barcode);
        Assert.Equal(10, (await db.Stock.SingleAsync()).Quantity);
        await controller.Delete(new DeleteRequest { Id = saved.ItemID });
        Assert.Equal(10, (await db.Stock.SingleAsync()).Quantity);
    }
}
