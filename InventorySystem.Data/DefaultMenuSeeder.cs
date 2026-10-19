using System.Data;
using InventorySystem.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Data;

public static class DefaultMenuSeeder
{
    // Apply only to the complete, unchanged historical seed. Any user customization
    // (including hiding, renaming, moving, deleting or adding a menu) opts out.
    public static async Task<bool> ApplyAsync(ApplicationDbContext db)
    {
        var attempt = 0;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A rolled-back attempt must be reloaded before an execution-strategy retry.
            if (attempt++ > 0) db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
            var rows = await db.MenuItems.OrderBy(x => x.Id).ToListAsync();
            var legacy = LegacyMenuSeed.Create();
            var extra = rows.Where(x => !legacy.Any(y => y.Id == x.Id)).ToList();
            if (legacy.Any(expected => !rows.Any(actual => Same(actual, expected))) ||
                extra.Any(x => !IsOriginalStockLink(x)) ||
                extra.GroupBy(x => x.Action).Any(g => g.Count() > 1))
                return false;

            var byId = rows.ToDictionary(x => x.Id);
            MenuItem Group(string name, string icon, int order)
            {
                var group = new MenuItem { Name = name, Icon = icon, SortOrder = order, CreatedAt = DateTime.UtcNow };
                db.MenuItems.Add(group);
                return group;
            }
            var raw = Group("Raw Materials", "🧱", 2);
            var finished = Group("Finished Goods", "📦", 5);
            var sales = Group("Sales", "🛒", 6);
            await db.SaveChangesAsync(); // Database-generated IDs avoid collisions with historical IDs.
            void Move(int id, int? parent, int order, string? name = null, string? icon = null, bool active = true)
            {
                var row = byId[id];
                row.ParentId = parent; row.SortOrder = order; row.IsActive = active;
                if (name != null) row.Name = name;
                if (icon != null) row.Icon = icon;
            }
            Move(1, null, 1);
            Move(2, raw.Id, 1, "Groups");
            Move(3, raw.Id, 2, "Items");
            Move(4, raw.Id, 3, active: false);
            Move(6, raw.Id, 4, "Purchase Invoices");
            Move(33, raw.Id, 6);
            Move(12, null, 3, icon: "📋");
            Move(13, 12, 1, "Cost Sheet List");
            Move(14, 12, 2, "Create Cost Sheet");
            Move(15, 12, 3, "Costing Settings");
            Move(16, null, 4);
            Move(17, 16, 1, "Production Orders");
            Move(18, 16, 2, "Create Production Order");
            Move(7, sales.Id, 1, "Sales Invoices");
            Move(34, sales.Id, 2, "Sales Report", "📈");
            Move(19, null, 7);
            var accounts = new[] { 5, 21, 22, 25, 27, 39, 38, 36, 40, 23, 24, 26, 28, 29, 30, 37, 42, 43, 20, 44, 41 };
            for (var i = 0; i < accounts.Length; i++) Move(accounts[i], 19, i + 1);
            byId[5].Name = "Customers & Suppliers";
            byId[21].Name = "Pay Money (PV)";
            byId[22].Name = "Receive Money (RV)";
            byId[25].Name = "Voucher History";
            Move(9, null, 8, active: false);
            Move(31, null, 9, active: false);
            Move(50, null, 10, active: false);
            Move(8, null, 11, active: false);
            void Stock(string action, string name, string icon, int parent, int order)
            {
                var row = extra.SingleOrDefault(x => x.Action == action);
                if (row == null)
                {
                    row = new MenuItem { Name = name, Controller = "Report", Action = action,
                        RequiredPermission = "Report.View", CreatedAt = DateTime.UtcNow };
                    db.MenuItems.Add(row);
                }
                row.Icon = icon; row.ParentId = parent; row.SortOrder = order;
            }
            Stock("RawMaterialStock", "Raw Material Stock", "🧱", raw.Id, 5);
            Stock("FinishedGoodsStock", "Finished Goods Stock", "📦", finished.Id, 1);
            await db.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();
            return true;
        });
    }

    private static bool Same(MenuItem a, MenuItem b) => a.Id == b.Id &&
        (a.Name == b.Name || a.Id == 2 && a.Name == "Groups" && a.Controller == "Category" && a.Action == "Index") &&
        a.Icon == b.Icon && a.Controller == b.Controller && a.Action == b.Action &&
        a.ParentId == b.ParentId && a.SortOrder == b.SortOrder && a.IsActive == b.IsActive &&
        a.RequiredPermission == b.RequiredPermission;

    private static bool IsOriginalStockLink(MenuItem x) => x.Controller == "Report" &&
        x.ParentId == 31 && x.IsActive && x.RequiredPermission == "Report.View" &&
        ((x.Action == "RawMaterialStock" && x.Name == "Raw Material Stock" && x.Icon == "🧱" && x.SortOrder == 5) ||
         (x.Action == "FinishedGoodsStock" && x.Name == "Finished Goods Stock" && x.Icon == "📦" && x.SortOrder == 6));
}
