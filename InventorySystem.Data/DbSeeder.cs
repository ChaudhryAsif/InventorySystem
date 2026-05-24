using InventorySystem.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Data
{
    /// <summary>
    /// Seeds the SuperAdmin role and user at application startup if they don't already exist.
    /// </summary>
    public static class DbSeeder
    {
        public static async Task SeedSuperAdminAsync(ApplicationDbContext db)
        {
            // ── 1. Ensure SuperAdmin role exists ─────────────────────────────────
            var superAdminRole = await db.Roles
                .FirstOrDefaultAsync(r => r.Name == "SuperAdmin");

            if (superAdminRole == null)
            {
                superAdminRole = new Role
                {
                    Name        = "SuperAdmin",
                    Description = "Super Administrator — unrestricted access to everything",
                    IsActive    = true,
                    CreatedAt   = DateTime.UtcNow
                };
                db.Roles.Add(superAdminRole);
                await db.SaveChangesAsync();
            }

            // ── 2. Ensure SuperAdmin user exists ─────────────────────────────────
            var superAdminUser = await db.AppUsers
                .FirstOrDefaultAsync(u => u.Username == "superadmin");

            if (superAdminUser == null)
            {
                // Default credentials → username: superadmin | password: SuperAdmin@123
                var passwordHash = BCrypt.Net.BCrypt.HashPassword("SuperAdmin@123", workFactor: 11);

                superAdminUser = new AppUser
                {
                    Username       = "superadmin",
                    PasswordHash   = passwordHash,
                    FullName       = "Super Administrator",
                    Role           = "SuperAdmin",       // legacy field
                    IsActive       = true,
                    CreatedAt      = DateTime.UtcNow,
                    PrimaryRoleId  = superAdminRole.Id
                };
                db.AppUsers.Add(superAdminUser);
                await db.SaveChangesAsync();
            }

            // ── 3. Ensure UserRole mapping exists ────────────────────────────────
            bool mappingExists = await db.UserRoles
                .AnyAsync(ur => ur.UserId == superAdminUser.Id && ur.RoleId == superAdminRole.Id);

            if (!mappingExists)
            {
                db.UserRoles.Add(new UserRole
                {
                    UserId     = superAdminUser.Id,
                    RoleId     = superAdminRole.Id,
                    AssignedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
        }
    }
}