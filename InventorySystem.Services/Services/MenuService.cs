using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Core.Services
{
    public interface IMenuService
    {
        Task<List<MenuItem>> GetMenuForUserAsync(int userId);
        Task<List<MenuItem>> GetMenuForUserAsync(int userId, bool isSuperAdmin);
        Task<List<MenuItem>> GetMenuForRoleAsync(int roleId);
        Task<List<MenuItem>> GetMainMenuItemsAsync();
    }

    public class MenuService : IMenuService
    {
        private readonly ApplicationDbContext _context;
        private readonly IPermissionService _permissionService;

        public MenuService(ApplicationDbContext context, IPermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        /// <summary>
        /// Get menus for user — SuperAdmin resolved automatically via PermissionService.
        /// </summary>
        public async Task<List<MenuItem>> GetMenuForUserAsync(int userId)
        {
            // PermissionService returns ALL codes for SuperAdmin automatically
            var userPermissions = await _permissionService.GetUserPermissionsAsync(userId);

            var menuItems = await _context.MenuItems
                .Where(m => m.IsActive && userPermissions.Contains(m.RequiredPermission))
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            return BuildMenuHierarchy(menuItems);
        }

        /// <summary>
        /// Get menus for user with explicit SuperAdmin flag.
        /// SuperAdmin sees every active menu item without any permission filtering.
        /// </summary>
        public async Task<List<MenuItem>> GetMenuForUserAsync(int userId, bool isSuperAdmin)
        {
            if (isSuperAdmin)
            {
                var allItems = await _context.MenuItems
                    .Where(m => m.IsActive)
                    .OrderBy(m => m.SortOrder)
                    .ToListAsync();

                return BuildMenuHierarchy(allItems);
            }

            return await GetMenuForUserAsync(userId);
        }

        public async Task<List<MenuItem>> GetMenuForRoleAsync(int roleId)
        {
            var rolePermissions = await _permissionService.GetRolePermissionsAsync(roleId);

            var menuItems = await _context.MenuItems
                .Where(m => m.IsActive && rolePermissions.Contains(m.RequiredPermission))
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            return BuildMenuHierarchy(menuItems);
        }

        public async Task<List<MenuItem>> GetMainMenuItemsAsync()
        {
            return await _context.MenuItems
                .Where(m => m.IsActive && m.ParentId == null)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();
        }

        private List<MenuItem> BuildMenuHierarchy(List<MenuItem> items)
        {
            var hierarchy = new List<MenuItem>();
            var mainItems = items.Where(i => i.ParentId == null).OrderBy(i => i.SortOrder).ToList();

            foreach (var mainItem in mainItems)
            {
                mainItem.Children = items
                    .Where(i => i.ParentId == mainItem.Id)
                    .OrderBy(i => i.SortOrder)
                    .ToList();
                hierarchy.Add(mainItem);
            }

            return hierarchy;
        }
    }
}