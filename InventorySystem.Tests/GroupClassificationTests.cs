using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventorySystem.Tests;

public class GroupClassificationTests
{
    private static ApplicationDbContext Database()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.ItemCategory.AddRange(
            new ItemCategory { CategoryId = 100, CategoryName = "Paper", GroupType = "RawMaterial", IsActive = true },
            new ItemCategory { CategoryId = 200, CategoryName = "Boxes", GroupType = "FinishedGoods", IsActive = true },
            new ItemCategory { CategoryId = 300, CategoryName = "Legacy mixed", IsActive = true });
        db.SaveChanges(); return db;
    }
    private static JsonElement Data(JsonResult response) => JsonSerializer.SerializeToElement(response.Value);
    private static ItemCategoryViewModel Group(int id, string? type) => new()
        { CategoryId = id, CategoryName = "Updated group", GroupType = type, IsActive = true };
    private static ItemViewModel Item(int groupId, string type) => new()
        { CategoryID = groupId, ItemName = "Test item", ItemType = type, StockUnit = type == "FinishedGoods" ? "pcs" : "kg", IsActive = true };

    [Theory]
    [InlineData("RawMaterial")]
    [InlineData("FinishedGoods")]
    public async Task Group_CreateReadUpdateDelete_PreservesType(string type)
    {
        await using var db = Database(); var controller = new CategoryController(db) {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role,"SuperAdmin")],"Test")) } } };
        Assert.True(Data(await controller.Save(Group(0, type))).GetProperty("success").GetBoolean());
        var saved = await db.ItemCategory.SingleAsync(g => g.CategoryId != 100 && g.CategoryId != 200 && g.CategoryId != 300);
        Assert.Equal(type, saved.GroupType);
        var loaded = Data(Assert.IsType<JsonResult>(await controller.GetById(saved.CategoryId)));
        Assert.Equal(type, loaded.GetProperty("GroupType").GetString());
        var model = Group(saved.CategoryId, type == "RawMaterial" ? "FinishedGoods" : "RawMaterial");
        model.CategoryName = "Renamed group";
        Assert.True(Data(await controller.Save(model)).GetProperty("success").GetBoolean());
        Assert.Equal(model.GroupType, saved.GroupType); Assert.Equal("Renamed group", saved.CategoryName);
        Assert.True(Data(await controller.Delete(saved.CategoryId)).GetProperty("success").GetBoolean());
        Assert.Null(await db.ItemCategory.FindAsync(saved.CategoryId));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("Other")]
    public async Task NewGroup_RequiresSupportedType(string? type)
    {
        await using var db = Database();
        Assert.False(Data(await new CategoryController(db).Save(Group(0, type))).GetProperty("success").GetBoolean());
        Assert.Equal(3, await db.ItemCategory.CountAsync());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task GroupTypeChange_RejectsIncompatibleLinkedItemsIncludingInactive(bool active)
    {
        await using var db = Database();
        db.Items.Add(new Items { CategoryID = 100, ItemName = "Paper", ItemType = "RawMaterial", StockUnit = "kg", IsActive = active });
        await db.SaveChangesAsync();
        Assert.False(Data(await new CategoryController(db).Save(Group(100, "FinishedGoods"))).GetProperty("success").GetBoolean());
        Assert.Equal("RawMaterial", (await db.ItemCategory.FindAsync(100))!.GroupType);
        Assert.Equal("RawMaterial", (await db.Items.SingleAsync()).ItemType);
    }

    [Fact]
    public async Task LegacyMixedGroup_CannotBeClassifiedUntilConflictingItemsAreMoved()
    {
        await using var db = Database();
        db.Items.AddRange(new Items { CategoryID = 300, ItemName = "Paper", ItemType = "RawMaterial", StockUnit = "kg" },
            new Items { CategoryID = 300, ItemName = "Box", ItemType = "FinishedGoods", StockUnit = "pcs" });
        await db.SaveChangesAsync();
        Assert.False(Data(await new CategoryController(db).Save(Group(300, "RawMaterial"))).GetProperty("success").GetBoolean());
        Assert.Null((await db.ItemCategory.FindAsync(300))!.GroupType);
        (await db.Items.SingleAsync(i => i.ItemType == "FinishedGoods")).CategoryID = 200;
        await db.SaveChangesAsync();
        Assert.True(Data(await new CategoryController(db).Save(Group(300, "RawMaterial"))).GetProperty("success").GetBoolean());
    }

    [Theory]
    [InlineData(100, "RawMaterial", true)] [InlineData(200, "FinishedGoods", true)]
    [InlineData(100, "FinishedGoods", false)] [InlineData(200, "RawMaterial", false)]
    [InlineData(300, "RawMaterial", true)] [InlineData(300, "FinishedGoods", true)]
    public async Task ItemCreation_EnforcesClassifiedGroup_AndSupportsUnclassifiedLegacy(int groupId, string type, bool allowed)
    {
        await using var db = Database();
        var result = await new ItemController(db).Save(Item(groupId, type), null);
        if (allowed) { Assert.IsType<JsonResult>(result); Assert.Equal(type, (await db.Items.SingleAsync()).ItemType); }
        else { Assert.IsType<BadRequestObjectResult>(result); Assert.Empty(await db.Items.ToListAsync()); }
    }

    [Fact]
    public async Task ItemEdit_CanMoveToMatchingGroup_ButRejectsMismatchedTypeWithoutChangingSavedData()
    {
        await using var db = Database(); var controller = new ItemController(db); var model = Item(100, "RawMaterial");
        await controller.Save(model, null); var saved = await db.Items.SingleAsync(); model.ItemID = saved.ItemID;
        model.CategoryID = 200;
        Assert.IsType<BadRequestObjectResult>(await controller.Save(model, null));
        Assert.Equal(100, saved.CategoryID); Assert.Equal("RawMaterial", saved.ItemType);
        // Moving a legacy item's existing type to the correct classified group is supported.
        saved.CategoryID = 300; saved.ItemType = "FinishedGoods"; saved.StockUnit = "pcs"; await db.SaveChangesAsync();
        model.ItemType = "FinishedGoods"; model.StockUnit = "pcs";
        Assert.IsType<JsonResult>(await controller.Save(model, null)); Assert.Equal(200, saved.CategoryID);
    }

    [Fact]
    public async Task TypeChangeDuringStatusReview_RequiresNewConfirmation()
    {
        await using var db = Database(); var controller = new CategoryController(db); var model = Group(100, "RawMaterial");
        model.IsActive = false;
        var preview = Data(await controller.Save(model));
        model.StatusConfirmationToken = preview.GetProperty("confirmationToken").GetString(); model.GroupType = "FinishedGoods";
        var refreshed = Data(await controller.Save(model));
        Assert.True(refreshed.GetProperty("requiresConfirmation").GetBoolean());
        Assert.NotEqual(model.StatusConfirmationToken, refreshed.GetProperty("confirmationToken").GetString());
        Assert.Equal("RawMaterial", (await db.ItemCategory.FindAsync(100))!.GroupType);
    }
}
