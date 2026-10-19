using System.Security.Claims;
using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InventorySystem.Tests;

public class MenuBuilderSafetyTests
{
    private static ApplicationDbContext Db()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.MenuItems.AddRange(
            new MenuItem { Id = 901, Name = "Raw Material", Icon = "📦" },
            new MenuItem { Id = 902, Name = "Empty group" },
            new MenuItem { Id = 903, Name = "Item", Controller = "Item", Action = "Index", RequiredPermission = "Item.View", ParentId = 901 },
            new MenuItem { Id = 904, Name = "Purchase", Controller = "Purchase", Action = "Index", RequiredPermission = "Purchase.View" });
        db.SaveChanges();
        return db;
    }
    private static AdminController Controller(ApplicationDbContext db, string role = "SuperAdmin")
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, role) }, "Test")) };
        return new AdminController(db, new PermissionService(db), Options.Create(new BranchOptions())) {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NoTempData()) };
    }
    private class NoTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private static bool Success(IActionResult response) => JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(response).Value).GetProperty("success").GetBoolean();
    private static string Version(ApplicationDbContext db) => AdminController.MenuVersion(db.MenuItems.ToList());
    private static AdminController.MenuOrderInput Order(string version, bool reverse = false) => new() { Version = version, Items = reverse
        ? new() { new() { Id = 904 }, new() { Id = 902, Children = new() { 903 } }, new() { Id = 901 } }
        : new() { new() { Id = 901, Children = new() { 903 } }, new() { Id = 902 }, new() { Id = 904 } } };

    [Fact]
    public async Task SaveOrder_MovesPagesAndPreservesRoutesPermissionsAndVisibility()
    {
        using var db = Db(); var controller = Controller(db);
        Assert.True(Success(await controller.SaveMenuOrder(Order(Version(db), true))));
        var page = (await db.MenuItems.FindAsync(903))!;
        Assert.Equal(902, page.ParentId); Assert.Equal("Item", page.Controller); Assert.Equal("Index", page.Action);
        Assert.Equal("Item.View", page.RequiredPermission); Assert.True(page.IsActive);
        Assert.Equal(1, (await db.MenuItems.FindAsync(904))!.SortOrder);
    }

    [Fact]
    public async Task SaveOrder_RejectsStaleOrMissingVersion_WithoutOverwriting()
    {
        using var db = Db(); var controller = Controller(db); var oldVersion = Version(db);
        Assert.True(Success(await controller.SaveMenuOrder(Order(oldVersion, true))));
        var savedVersion = Version(db);
        Assert.False(Success(await controller.SaveMenuOrder(Order(oldVersion))));
        Assert.False(Success(await controller.SaveMenuOrder(Order(""))));
        Assert.Equal(savedVersion, Version(db));
    }

    [Theory]
    [InlineData("name")] [InlineData("active")] [InlineData("parent")] [InlineData("permission")] [InlineData("icon")]
    public async Task SaveOrder_RejectsOtherSettingsChangedSinceLoad(string field)
    {
        using var db = Db(); var controller = Controller(db); var oldVersion = Version(db);
        var page = (await db.MenuItems.FindAsync(903))!;
        switch (field) { case "name": page.Name = "Renamed"; break; case "active": page.IsActive = false; break;
            case "parent": page.ParentId = 902; break; case "permission": page.RequiredPermission = "Other.View"; break; case "icon": page.Icon = "X"; break; }
        await db.SaveChangesAsync(); var savedVersion = Version(db);
        Assert.False(Success(await controller.SaveMenuOrder(Order(oldVersion, true))));
        Assert.Equal(savedVersion, Version(db));
    }

    [Fact]
    public async Task EditGroup_RejectsNestingEvenWhenEmpty()
    {
        using var db = Db(); var controller = Controller(db); var before = Version(db);
        await controller.EditMenuItem(902, new() { Name = "Empty group", ParentId = 901, IsActive = true, Version = before });
        Assert.Equal(before, Version(db)); Assert.Contains("top level", controller.TempData["Error"]!.ToString());
    }

    [Fact]
    public async Task EditPage_IgnoresForgedRouteFields_AndCannotBeDeleted()
    {
        using var db = Db(); var controller = Controller(db);
        var input = JsonSerializer.Deserialize<AdminController.MenuItemEditInput>(JsonSerializer.Serialize(new {
            Name = "Purchasing", Controller = "", Action = "", RequiredPermission = "", IsActive = true, Version = Version(db) }))!;
        await controller.EditMenuItem(904, input);
        var page = (await db.MenuItems.FindAsync(904))!;
        Assert.Equal("Purchasing", page.Name); Assert.Equal("Purchase", page.Controller); Assert.Equal("Index", page.Action);
        Assert.Equal("Purchase.View", page.RequiredPermission);
        Assert.False(Success(await controller.DeleteMenuGroup(904))); Assert.NotNull(await db.MenuItems.FindAsync(904));
    }

    [Fact]
    public async Task Edit_RejectsStaleForm_WithoutChangingNewSettings()
    {
        using var db = Db(); var controller = Controller(db); var old = Version(db);
        (await db.MenuItems.FindAsync(904))!.Name = "Updated by another admin"; await db.SaveChangesAsync(); var saved = Version(db);
        await controller.EditMenuItem(904, new() { Name = "Old tab", Version = old, IsActive = true });
        Assert.Equal(saved, Version(db)); Assert.Contains("Reload", controller.TempData["Error"]!.ToString());
    }

    [Theory]
    [InlineData("   ")] [InlineData("Raw Material")]
    public async Task EditGroup_RejectsBlankOrDuplicateName(string name)
    {
        using var db = Db(); var controller = Controller(db); var before = Version(db);
        await controller.EditMenuItem(902, new() { Name = name, Version = before, IsActive = true });
        Assert.Equal(before, Version(db)); Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task Group_CannotBecomePage_WhenForgedRouteIsPosted()
    {
        using var db = Db(); var controller = Controller(db);
        var input = JsonSerializer.Deserialize<AdminController.MenuItemEditInput>(JsonSerializer.Serialize(new {
            Name = "Group renamed", Controller = "Sale", Action = "Index", RequiredPermission = "Sale.View", IsActive = true, Version = Version(db) }))!;
        await controller.EditMenuItem(902, input);
        var group = (await db.MenuItems.FindAsync(902))!; Assert.Null(group.Controller); Assert.Null(group.Action); Assert.Equal("", group.RequiredPermission);
    }

    [Fact]
    public async Task Delete_RejectsNonEmptyGroup_AllowsEmptyGroup()
    {
        using var db = Db(); var controller = Controller(db);
        Assert.False(Success(await controller.DeleteMenuGroup(901)));
        Assert.True(Success(await controller.DeleteMenuGroup(902)));
        Assert.NotNull(await db.MenuItems.FindAsync(903));
    }

    [Fact]
    public async Task SaveOrder_RejectsInvalidTree_WithoutPartialUpdates()
    {
        using var db = Db(); var controller = Controller(db); var before = Version(db); var order = Order(before, true);
        order.Items![1].Children!.Add(902);
        Assert.False(Success(await controller.SaveMenuOrder(order))); Assert.Equal(before, Version(db));
        order.Items = new() { null! };
        Assert.False(Success(await controller.SaveMenuOrder(order))); Assert.Equal(before, Version(db));
    }

    [Theory]
    [InlineData("User")] [InlineData("Admin")]
    public async Task OrdinaryUser_CannotChangeMenus(string role)
    {
        using var db = Db(); var controller = Controller(db, role); var before = Version(db);
        Assert.IsType<ForbidResult>(await controller.MenuItems());
        Assert.IsType<ForbidResult>(await controller.EditMenuItem(904));
        Assert.IsType<ForbidResult>(await controller.SaveMenuOrder(Order(before)));
        Assert.IsType<ForbidResult>(await controller.CreateMenuGroup(new() { Name = "Forbidden" }));
        Assert.IsType<ForbidResult>(await controller.DeleteMenuGroup(902));
        Assert.IsType<ForbidResult>(await controller.EditMenuItem(904, new() { Name = "Forbidden", Version = before }));
        Assert.Equal(before, Version(db));
    }

    [Fact]
    public async Task LimitedRole_SeesOnlyPermittedPages_InVisibleGroups()
    {
        using var db = Db();
        db.Roles.Add(new Role { Id = 901, Name = "Limited" });
        db.Permissions.Add(new Permission { Id = 901, Code = "Item.View", Name = "View items" });
        db.RolePermissions.Add(new RolePermission { Id = 901, RoleId = 901, PermissionId = 901 });
        db.MenuItems.Add(new MenuItem { Id = 905, Name = "Purchase under group", Controller = "Purchase", RequiredPermission = "Purchase.View", ParentId = 901 });
        await db.SaveChangesAsync();
        var service = new MenuService(db, new PermissionService(db));
        var visible = await service.GetMenuForRoleAsync(901);
        Assert.Equal(901, Assert.Single(visible).Id);
        Assert.Equal(903, Assert.Single(visible[0].Children!).Id);
        (await db.MenuItems.FindAsync(901))!.IsActive = false; await db.SaveChangesAsync();
        Assert.Empty(await service.GetMenuForRoleAsync(901));
    }

    [Fact]
    public async Task CreateMoveHideRestoreDelete_Lifecycle_PreservesPages()
    {
        using var db = Db(); var controller = Controller(db);
        Assert.True(Success(await controller.CreateMenuGroup(new() { Name = " New catalog " })));
        Assert.False(Success(await controller.CreateMenuGroup(new() { Name = "new catalog" })));
        var group = await db.MenuItems.SingleAsync(m => m.Name == "New catalog");
        var input = Order(Version(db)); input.Items!.Add(new() { Id = group.Id, Children = new() { 903 } }); input.Items[0].Children!.Clear();
        Assert.True(Success(await controller.SaveMenuOrder(input)));
        await controller.EditMenuItem(group.Id, new() { Name = group.Name, Version = Version(db), IsActive = false });
        Assert.False(group.IsActive); Assert.True((await db.MenuItems.FindAsync(903))!.IsActive);
        await controller.EditMenuItem(group.Id, new() { Name = group.Name, Version = Version(db), IsActive = true });
        Assert.True(group.IsActive);
        Assert.False(Success(await controller.DeleteMenuGroup(group.Id)));
        await controller.EditMenuItem(903, new() { Name = "Item", Version = Version(db), IsActive = true });
        Assert.True(Success(await controller.DeleteMenuGroup(group.Id)));
        Assert.NotNull(await db.MenuItems.FindAsync(903));
    }
}
