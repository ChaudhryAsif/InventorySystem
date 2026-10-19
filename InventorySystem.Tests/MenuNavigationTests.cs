using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Tests;

public class MenuNavigationTests
{
    [Fact]
    public async Task RestoredLegacyMenus_AreHidden_WhileProductionAndStockReportsRemain()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.MenuItems.AddRange(
            new MenuItem { Id = 9001, Name = "Retired", Controller = "Consume", Action = "Index" },
            new MenuItem { Id = 9002, Name = "Retired report", Controller = "Report", Action = "ConsumeReport" },
            new MenuItem { Id = 9003, Name = "Production", Controller = "Production", Action = "Consume" },
            new MenuItem { Id = 9004, Name = "Stock", Controller = "Report", Action = "StockReport" });
        await db.SaveChangesAsync();
        var service = new MenuService(db, new PermissionService(db));
        foreach (var items in new[] { await service.GetMenuForUserAsync(1, true), await service.GetMainMenuItemsAsync() })
        {
            Assert.DoesNotContain(items, x => x.Id == 9001 || x.Id == 9002);
            Assert.Contains(items, x => x.Id == 9003);
            Assert.Contains(items, x => x.Id == 9004);
        }
    }

    private static List<MenuItem> SampleMenu() => new()
    {
        new MenuItem { Id = 1, Name = "Raw Material", SortOrder = 1 },
        new MenuItem { Id = 2, Name = "Empty", SortOrder = 2 },
        new MenuItem { Id = 3, Name = "Item Category", Controller = "Category", Action = "Index", RequiredPermission = "ItemCategory.View", ParentId = 1, SortOrder = 1 },
        new MenuItem { Id = 4, Name = "Item Management", Controller = "Item", Action = "Index", RequiredPermission = "Item.View", ParentId = 1, SortOrder = 2 },
        new MenuItem { Id = 5, Name = "Sale", Controller = "Sale", Action = "Index", RequiredPermission = "Sale.View", SortOrder = 3 },
    };

    [Fact]
    public async Task Group_ShowsOnlyWithVisiblePages_AndKeepsPagePermissions()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.MenuItems.AddRange(SampleMenu());
        await db.SaveChangesAsync();
        var service = new MenuService(db, new PermissionService(db));

        var admin = await service.GetMenuForUserAsync(1, true);
        Assert.Equal(new[] { 1, 5 }, admin.Select(x => x.Id));
        Assert.Equal(new[] { 3, 4 }, admin[0].Children!.Select(x => x.Id));

        var role = await service.GetMenuForRoleAsync(0);
        Assert.Empty(role);
    }

    [Fact]
    public void ValidateMenuTree_AcceptsValidAndRejectsBadNesting()
    {
        var all = SampleMenu();
        var ok = new List<AdminController.MenuNodeDto>
        {
            new() { Id = 1, Children = new() { 4, 3 } }, new() { Id = 2 }, new() { Id = 5 }
        };
        Assert.Null(AdminController.ValidateMenuTree(ok, all));

        var pageInPage = new List<AdminController.MenuNodeDto>
        {
            new() { Id = 1, Children = new() { 3 } }, new() { Id = 2 }, new() { Id = 5, Children = new() { 4 } }
        };
        Assert.NotNull(AdminController.ValidateMenuTree(pageInPage, all));

        var groupInGroup = new List<AdminController.MenuNodeDto>
        {
            new() { Id = 1, Children = new() { 2, 3, 4 } }, new() { Id = 5 }
        };
        Assert.NotNull(AdminController.ValidateMenuTree(groupInGroup, all));

        var duplicate = new List<AdminController.MenuNodeDto>
        {
            new() { Id = 1, Children = new() { 3, 3, 4 } }, new() { Id = 2 }, new() { Id = 5 }
        };
        Assert.NotNull(AdminController.ValidateMenuTree(duplicate, all));

        var missing = new List<AdminController.MenuNodeDto> { new() { Id = 1, Children = new() { 3, 4 } }, new() { Id = 5 } };
        Assert.NotNull(AdminController.ValidateMenuTree(missing, all));
    }
}
