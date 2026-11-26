using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Data;

// Adds the "Edit saved records" permission for each locked module. It is only created, never granted to any role.
public static class AccessPermissionSeeder
{
    public static async Task ApplyAsync(ApplicationDbContext db)
    {
        var existing = await db.Permissions.Where(p => p.Action == EditSavedPermissions.Action).Select(p => p.Code).ToListAsync();
        foreach (var module in EditSavedPermissions.Modules.Where(m => !existing.Contains(EditSavedPermissions.Code(m))))
            db.Permissions.Add(new()
            {
                Code = EditSavedPermissions.Code(module), Module = module, Action = EditSavedPermissions.Action, IsActive = true, CreatedAt = DateTime.UtcNow,
                Name = "Edit saved records",
                Description = "Change a record after it has been saved. Without this only a SuperAdmin can; delete and re-enter is the alternative."
            });
        await db.SaveChangesAsync();
    }
}
