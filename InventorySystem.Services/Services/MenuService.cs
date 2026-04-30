using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Core.Services
{
    /// <summary>
    /// Service for retrieving menu items based on user permissions
    /// </summary>
    public interface IMenuService
    {
        Task<List<MenuItem>> GetMenuForUserAsync(int userId);
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
        /// Get menu items accessible by user
        /// </summary>
        public async Task<List<MenuItem>> GetMenuForUserAsync(int userId)
        {
            var userPermissions = await _permissionService.GetUserPermissionsAsync(userId);

            var menuItems = await _context.MenuItems
                .Where(m => m.IsActive && userPermissions.Contains(m.RequiredPermission))
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            return BuildMenuHierarchy(menuItems);
        }

        /// <summary>
        /// Get menu items accessible by role
        /// </summary>
        public async Task<List<MenuItem>> GetMenuForRoleAsync(int roleId)
        {
            var rolePermissions = await _permissionService.GetRolePermissionsAsync(roleId);

            var menuItems = await _context.MenuItems
                .Where(m => m.IsActive && rolePermissions.Contains(m.RequiredPermission))
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            return BuildMenuHierarchy(menuItems);
        }

        /// <summary>
        /// Get all active main menu items
        /// </summary>
        public async Task<List<MenuItem>> GetMainMenuItemsAsync()
        {
            return await _context.MenuItems
                .Where(m => m.IsActive && m.ParentId == null)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();
        }

        /// <summary>
        /// Build menu hierarchy from flat list
        /// </summary>
        private List<MenuItem> BuildMenuHierarchy(List<MenuItem> items)
        {
            var hierarchy = new List<MenuItem>();

            var mainItems = items.Where(i => i.ParentId == null).OrderBy(i => i.SortOrder).ToList();

            foreach (var mainItem in mainItems)
            {
                var children = items.Where(i => i.ParentId == mainItem.Id).OrderBy(i => i.SortOrder).ToList();
                mainItem.Children = children;
                hierarchy.Add(mainItem);
            }

            return hierarchy;
        }
    }
}