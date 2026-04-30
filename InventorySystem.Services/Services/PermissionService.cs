using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace InventorySystem.Core.Services
{
    /// <summary>
    /// Service for checking and managing user permissions
    /// </summary>
    public interface IPermissionService
    {
        Task<bool> HasPermissionAsync(int userId, string permissionCode);
        Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permissionCode);
        Task<List<string>> GetUserPermissionsAsync(int userId);
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
        /// Check if user has specific permission by userId
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

            // Check if user's any role has this permission
            return user.UserRoles?.Any(ur =>
                ur.Role?.IsActive == true &&
                ur.Role.RolePermissions?.Any(rp =>
                    rp.Permission?.Code == permissionCode &&
                    rp.Permission.IsActive) == true) ?? false;
        }

        /// <summary>
        /// Check if user has specific permission by ClaimsPrincipal
        /// </summary>
        public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permissionCode)
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim?.Value, out int userId))
                return false;

            return await HasPermissionAsync(userId, permissionCode);
        }

        /// <summary>
        /// Get all permissions for a user
        /// </summary>
        public async Task<List<string>> GetUserPermissionsAsync(int userId)
        {
            var permissions = await _context.AppUsers
                .Where(u => u.Id == userId && u.IsActive)
                .SelectMany(u => u.UserRoles!)
                .Where(ur => ur.Role!.IsActive)
                .SelectMany(ur => ur.Role!.RolePermissions!)
                .Where(rp => rp.Permission!.IsActive)
                .Select(rp => rp.Permission!.Code)
                .Distinct()
                .ToListAsync();

            return permissions;
        }

        /// <summary>
        /// Get all permissions for a role
        /// </summary>
        public async Task<List<string>> GetRolePermissionsAsync(int roleId)
        {
            var permissions = await _context.Roles
                .Where(r => r.Id == roleId && r.IsActive)
                .SelectMany(r => r.RolePermissions!)
                .Where(rp => rp.Permission!.IsActive)
                .Select(rp => rp.Permission!.Code)
                .Distinct()
                .ToListAsync();

            return permissions;
        }

        /// <summary>
        /// Get permission by code
        /// </summary>
        public async Task<Permission?> GetPermissionByCodeAsync(string code)
        {
            return await _context.Permissions
                .FirstOrDefaultAsync(p => p.Code == code && p.IsActive);
        }

        /// <summary>
        /// Assign permission to role
        /// </summary>
        public async Task AssignPermissionToRoleAsync(int roleId, int permissionId)
        {
            var exists = await _context.RolePermissions
                .AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

            if (!exists)
            {
                _context.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permissionId,
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Remove permission from role
        /// </summary>
        public async Task RemovePermissionFromRoleAsync(int roleId, int permissionId)
        {
            var rolePermission = await _context.RolePermissions
                .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

            if (rolePermission != null)
            {
                _context.RolePermissions.Remove(rolePermission);
                await _context.SaveChangesAsync();
            }
        }
    }
}