using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Tests;

public class DefaultMenuTests
{
    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task FreshDatabase_GetsAgreedLayoutAndAccountsOrder_WithoutChangingPermissions()
    {
        await using var db = Database();
        await db.Database.EnsureCreatedAsync();
        var before = await db.MenuItems.ToDictionaryAsync(x => x.Id, x => (x.Controller ?? "") + "/" + x.Action + ":" + x.RequiredPermission);
        Assert.True(await DefaultMenuSeeder.ApplyAsync(db));
        var all = await db.MenuItems.ToListAsync();
        Assert.Equal(new[] { "Dashboard", "Raw Materials", "Cost Sheet", "Production", "Finished Goods", "Sales", "Accounts" },
            all.Where(x => x.ParentId == null && x.IsActive).OrderBy(x => x.SortOrder).Select(x => x.Name));
        var raw = all.Single(x => x.Name == "Raw Materials");
        Assert.Equal(new[] { "Groups", "Items", "Purchase Invoices", "Raw Material Stock", "Purchase Report" },
            all.Where(x => x.ParentId == raw.Id && x.IsActive).OrderBy(x => x.SortOrder).Select(x => x.Name));
        Assert.Equal(new[] { 5, 21, 22, 25, 27, 39, 38, 36, 40, 23, 24, 26, 28, 29, 30, 37, 42, 43, 20, 44, 41 },
            all.Where(x => x.ParentId == 19).OrderBy(x => x.SortOrder).Select(x => x.Id));
        foreach (var row in all.Where(x => before.ContainsKey(x.Id)))
            Assert.Equal(before[row.Id], (row.Controller ?? "") + "/" + row.Action + ":" + row.RequiredPermission);
        Assert.False(await DefaultMenuSeeder.ApplyAsync(db));
        Assert.Equal(all.Count, await db.MenuItems.CountAsync());
    }

    [Theory]
    [InlineData("name")]
    [InlineData("icon")]
    [InlineData("order")]
    [InlineData("parent")]
    [InlineData("hidden")]
    [InlineData("permission")]
    [InlineData("route")]
    [InlineData("added")]
    [InlineData("deleted")]
    public async Task CustomizedDatabase_IsPreserved(string change)
    {
        await using var db = Database();
        await db.Database.EnsureCreatedAsync();
        var item = await db.MenuItems.SingleAsync(x => x.Id == 2);
        switch (change)
        {
            case "name": item.Name = "My groups"; break;
            case "icon": item.Icon = "⭐"; break;
            case "order": item.SortOrder = 99; break;
            case "parent": item.ParentId = 12; break;
            case "hidden": item.IsActive = false; break;
            case "permission": item.RequiredPermission = "Custom.View"; break;
            case "route": item.Action = "Other"; break;
            case "added": db.MenuItems.Add(new MenuItem { Name = "Custom group" }); break;
            case "deleted": db.MenuItems.Remove(item); break;
        }
        await db.SaveChangesAsync();
        var before = Snapshot(await db.MenuItems.OrderBy(x => x.Id).ToListAsync());
        Assert.False(await DefaultMenuSeeder.ApplyAsync(db));
        Assert.Equal(before, Snapshot(await db.MenuItems.OrderBy(x => x.Id).ToListAsync()));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task MaterialsToPurchaseMenuRow_KeepsDefaultLayoutOnlyWhenItIsTheMigrationRow(bool customized, bool layoutApplied)
    {
        await using var db = Database();
        await db.Database.EnsureCreatedAsync();
        db.MenuItems.Add(new MenuItem { Name = "Materials to Purchase", Icon = "🛒", Controller = "Production", Action = "PurchaseNeeds", ParentId = 16, SortOrder = 3,
            RequiredPermission = customized ? "Report.View" : "Production.View", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Assert.Equal(layoutApplied, await DefaultMenuSeeder.ApplyAsync(db));
        var row = await db.MenuItems.SingleAsync(x => x.Action == "PurchaseNeeds");
        Assert.Equal(16, row.ParentId); Assert.Equal(3, row.SortOrder);
        if (layoutApplied)
            Assert.Equal(new[] { "Production Orders", "Create Production Order", "Materials to Purchase" },
                (await db.MenuItems.Where(x => x.ParentId == 16 && x.IsActive).OrderBy(x => x.SortOrder).ToListAsync()).Select(x => x.Name));
    }

    [Fact]
    public async Task MigrationRenamedGroups_StillAllowsFreshDefaultMenuLayout()
    {
        await using var db = Database();
        await db.Database.EnsureCreatedAsync();
        (await db.MenuItems.SingleAsync(x => x.Id == 2)).Name = "Groups";
        await db.SaveChangesAsync();
        Assert.True(await DefaultMenuSeeder.ApplyAsync(db));
        var group = await db.MenuItems.Include(x => x.Parent).SingleAsync(x => x.Id == 2);
        Assert.Equal("Groups", group.Name);
        Assert.Equal("Raw Materials", group.Parent!.Name);
        Assert.Equal("ItemCategory.View", group.RequiredPermission);
    }

    [Fact]
    public async Task HistoricalStockLinks_AreReusedAndMoved()
    {
        await using var db = Database();
        await db.Database.EnsureCreatedAsync();
        db.MenuItems.AddRange(
            new MenuItem { Id = 80, Name = "Raw Material Stock", Icon = "🧱", Controller = "Report", Action = "RawMaterialStock", ParentId = 31, SortOrder = 5, RequiredPermission = "Report.View" },
            new MenuItem { Id = 81, Name = "Finished Goods Stock", Icon = "📦", Controller = "Report", Action = "FinishedGoodsStock", ParentId = 31, SortOrder = 6, RequiredPermission = "Report.View" });
        await db.SaveChangesAsync();
        Assert.True(await DefaultMenuSeeder.ApplyAsync(db));
        Assert.Equal(1, await db.MenuItems.CountAsync(x => x.Action == "RawMaterialStock"));
        Assert.Equal("Finished Goods", (await db.MenuItems.Include(x => x.Parent).SingleAsync(x => x.Id == 81)).Parent!.Name);
    }

    private static string Snapshot(IEnumerable<MenuItem> rows) => string.Join("\n", rows.Select(x =>
        $"{x.Id}|{x.Name}|{x.Icon}|{x.Controller}|{x.Action}|{x.ParentId}|{x.SortOrder}|{x.IsActive}|{x.RequiredPermission}"));
}
