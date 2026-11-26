using InventorySystem.Core.Models;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Data;

public static class AuditSaveExtensions
{
    // Saves the change together with its access-log line. If the log table has not been installed yet (SQL script not run),
    // the change itself must still go through, so the log line is dropped and the rest is saved.
    public static async Task SaveChangesWithAuditAsync(this ApplicationDbContext db)
    {
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is Microsoft.Data.SqlClient.SqlException { Number: 208 } && db.ChangeTracker.Entries<AdminAuditLog>().Any(e => e.State == EntityState.Added))
        {
            foreach (var entry in db.ChangeTracker.Entries<AdminAuditLog>().Where(e => e.State == EntityState.Added).ToList()) entry.State = EntityState.Detached;
            await db.SaveChangesAsync();
        }
    }
}
