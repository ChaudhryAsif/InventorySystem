using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Tests;
public class RecordPrintTests
{
    private static ApplicationDbContext Seeded()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.ItemCategory.AddRange(new ItemCategory { CategoryId = 1, CategoryName = "Mill", GroupType = "RawMaterial", IsActive = true },
            new ItemCategory { CategoryId = 2, CategoryName = "Cartons", GroupType = "FinishedGoods", IsActive = false });
        db.Items.AddRange(new Items { ItemID = 1, CategoryID = 1, ItemName = "Paper", ItemType = "RawMaterial", StockUnit = "kg", IsActive = true, SalePrice = 400 },
            new Items { ItemID = 2, CategoryID = 1, ItemName = "Glue", ItemType = "RawMaterial", StockUnit = "kg", IsActive = false },
            new Items { ItemID = 3, CategoryID = 2, ItemName = "Box", ItemType = "FinishedGoods", StockUnit = "pcs", IsActive = true });
        db.SaveChanges();
        return db;
    }
    private static PrintPage<T> Page<T>(IActionResult result) => (PrintPage<T>)((ViewResult)result).Model!;

    [Fact] public async Task ItemPrintListFollowsTypeStatusAndSearch()
    {
        var c = new ItemController(Seeded());
        Assert.Equal(2, Page<ItemPrintRow>(await c.Print(null, "RawMaterial", null, null, null)).Rows.Count);
        Assert.Equal(["Paper"], Page<ItemPrintRow>(await c.Print(null, "RawMaterial", null, "Active", null)).Rows.Select(r => r.ItemName));
        Assert.Equal(["Glue"], Page<ItemPrintRow>(await c.Print(null, "RawMaterial", "glu", null, null)).Rows.Select(r => r.ItemName));
    }
    [Fact] public async Task ItemPrintSelectedReturnsOnlyTickedRowsWithGroupName()
    {
        var page = Page<ItemPrintRow>(await new ItemController(Seeded()).Print("2, 3", "RawMaterial", null, null, null, detail: true));
        var row = Assert.Single(page.Rows);
        Assert.Equal("Glue", row.ItemName); Assert.Equal("Mill", row.CategoryName); Assert.True(page.Detail);
    }
    [Fact] public async Task ItemPrintRejectsBadFiltersAndIgnoresBadIds()
    {
        var c = new ItemController(Seeded());
        Assert.IsType<BadRequestResult>(await c.Print(null, "Nope", null, null, null));
        Assert.Empty(Page<ItemPrintRow>(await c.Print("abc,-1", null, null, null, null)).Rows);
    }
    [Fact] public async Task GroupPrintCountsItemsAndKeepsTheGroupType()
    {
        var c = new CategoryController(Seeded());
        var rows = Page<GroupPrintRow>(await c.Print(null, "RawMaterial")).Rows;
        var mill = Assert.Single(rows);
        Assert.Equal(2, mill.ItemCount);
        Assert.Equal("Cartons", Assert.Single(Page<GroupPrintRow>(await c.Print("2", null)).Rows).CategoryName);
        Assert.IsType<BadRequestResult>(await c.Print(null, "Nope"));
    }
}
