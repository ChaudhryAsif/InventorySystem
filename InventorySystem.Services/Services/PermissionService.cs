using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace InventorySystem.Core.Services
{
    public interface IPermissionService
    {
        Task<bool> HasPermissionAsync(int userId, string permissionCode);
        Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permissionCode);
        Task<List<string>> GetUserPermissionsAsync(int userId);
        Task<List<string>> GetAllPermissionCodesAsync();
        Task<List<string>> GetRolePermissionsAsync(int roleId);
        Task<Permission?> GetPermissionByCodeAsync(string code);
        Task AssignPermissionToRoleAsync(int roleId, int permissionId);
        Task RemovePermissionFromRoleAsync(int roleId, int permissionId);
    }

    public class PermissionService : IPermissionService
    {
        private readonly ApplicationDbContext _context;

        public PermissionService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Check if user has a specific permission.
        /// SuperAdmin role always returns true.
        /// </summary>
        public async Task<bool> HasPermissionAsync(int userId, string permissionCode)
        {
            var user = await _context.AppUsers
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .ThenInclude(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
                .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

            if (user == null) return false;

            // ── SuperAdmin gets full access ──
            bool isSuperAdmin = user.UserRoles?.Any(ur =>
                ur.Role?.Name == "SuperAdmin" && ur.Role.IsActive) == true;
            if (isSuperAdmin) return true;

            return user.UserRoles?.Any(ur =>
                ur.Role?.IsActive == true &&
                ur.Role.RolePermissions?.Any(rp =>
                    rp.Permission?.Code == permissionCode &&
                    rp.Permission.IsActive) == true) ?? false;
        }

        /// <summary>
        /// Check if user has a specific permission via ClaimsPrincipal.
        /// SuperAdmin claim always returns true.
        /// </summary>
        public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permissionCode)
        {
            // ── SuperAdmin bypass via claim ──
            if (user.FindFirst(ClaimTypes.Role)?.Value == "SuperAdmin")
                return true;

            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim?.Value, out int userId))
                return false;

            return await HasPermissionAsync(userId, permissionCode);
        }

        /// <summary>
        /// Get all permissions for a user.
        /// SuperAdmin receives ALL permission codes in the system.
        /// </summary>
        public async Task<List<string>> GetUserPermissionsAsync(int userId)
        {
            var user = await _context.AppUsers
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

            if (user == null) return new List<string>();

            // ── SuperAdmin gets every permission code ──
            bool isSuperAdmin = user.UserRoles?.Any(ur =>
                ur.Role?.Name == "SuperAdmin" && ur.Role.IsActive) == true;
            if (isSuperAdmin)
                return await GetAllPermissionCodesAsync();

            return await _context.AppUsers
                .Where(u => u.Id == userId && u.IsActive)
                .SelectMany(u => u.UserRoles!)
                .Where(ur => ur.Role!.IsActive)
                .SelectMany(ur => ur.Role!.RolePermissions!)
                .Where(rp => rp.Permission!.IsActive)
                .Select(rp => rp.Permission!.Code)
                .Distinct()
                .ToListAsync();
        }

        /// <summary>
        /// Returns all active permission codes in the system.
        /// </summary>
        public async Task<List<string>> GetAllPermissionCodesAsync()
        {
            return await _context.Permissions
                .Where(p => p.IsActive)
                .Select(p => p.Code)
                .Distinct()
                .ToListAsync();
        }

        public async Task<List<string>> GetRolePermissionsAsync(int roleId)
        {
            return await _context.Roles
                .Where(r => r.Id == roleId && r.IsActive)
                .SelectMany(r => r.RolePermissions!)
                .Where(rp => rp.Permission!.IsActive)
                .Select(rp => rp.Permission!.Code)
                .Distinct()
                .ToListAsync();
        }

        public async Task<Permission?> GetPermissionByCodeAsync(string code)
        {
            return await _context.Permissions
                .FirstOrDefaultAsync(p => p.Code == code && p.IsActive);
        }

        public async Task AssignPermissionToRoleAsync(int roleId, int permissionId)
        {
            var exists = await _context.RolePermissions
                .AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

            if (!exists)
            {
                _context.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permissionId
                });
                await _context.SaveChangesAsync();
            }
        }

        public async Task RemovePermissionFromRoleAsync(int roleId, int permissionId)
        {
            var rp = await _context.RolePermissions
                .FirstOrDefaultAsync(r => r.RoleId == roleId && r.PermissionId == permissionId);

            if (rp != null)
            {
                _context.RolePermissions.Remove(rp);
                await _context.SaveChangesAsync();
            }
        }
    }
}