using InventorySystem.Core.Models;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Data;

// Creates the "Owner (View only)" role once, holding every View permission and nothing else.
// After that the SuperAdmin owns it: permissions ticked or unticked in Admin > Roles are never put back.
public static class OwnerViewRoleSeeder
{
    public const string RoleName = "Owner (View only)";

    public static async Task ApplyAsync(ApplicationDbContext db)
    {
        if (await db.Roles.AnyAsync(r => r.Name == RoleName)) return;
        var role = new Role { Name = RoleName, Description = "Can view everything. Cannot add, edit or delete anything.", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        foreach (var permission in await db.Permissions.Where(p => p.IsActive && p.Action == "View").ToListAsync())
            db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = permission.Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
}
