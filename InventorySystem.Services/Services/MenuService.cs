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
            var userPermissions = await _permissionService.GetUserPermissionsAsync(userId);
            return BuildMenuHierarchy(await LoadActiveItemsAsync(), new HashSet<string>(userPermissions));
        }

        /// <summary>
        /// Get menus for user with explicit SuperAdmin flag.
        /// SuperAdmin sees every active menu item without any permission filtering.
        /// </summary>
        public async Task<List<MenuItem>> GetMenuForUserAsync(int userId, bool isSuperAdmin)
        {
            if (isSuperAdmin)
                return BuildMenuHierarchy(await LoadActiveItemsAsync(), null);

            return await GetMenuForUserAsync(userId);
        }

        public async Task<List<MenuItem>> GetMenuForRoleAsync(int roleId)
        {
            var rolePermissions = await _permissionService.GetRolePermissionsAsync(roleId);
            return BuildMenuHierarchy(await LoadActiveItemsAsync(), new HashSet<string>(rolePermissions));
        }

        public async Task<List<MenuItem>> GetMainMenuItemsAsync()
        {
            return await _context.MenuItems
                .Where(m => m.Controller != "Consume" && !(m.Controller == "Report" && m.Action == "ConsumeReport"))
                .Where(m => m.IsActive && m.ParentId == null)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();
        }

        private Task<List<MenuItem>> LoadActiveItemsAsync()
        {
            return _context.MenuItems
                .Where(m => m.Controller != "Consume" && !(m.Controller == "Report" && m.Action == "ConsumeReport"))
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();
        }

        // permissions null = no filtering; a group shows only if one of its pages is visible.
        private static List<MenuItem> BuildMenuHierarchy(List<MenuItem> items, HashSet<string>? permissions)
        {
            bool CanSee(MenuItem i) => permissions == null || permissions.Contains(i.RequiredPermission);
            bool IsGroup(MenuItem i) => string.IsNullOrEmpty(i.Controller);

            var hierarchy = new List<MenuItem>();
            foreach (var mainItem in items.Where(i => i.ParentId == null).OrderBy(i => i.SortOrder))
            {
                var children = items
                    .Where(i => i.ParentId == mainItem.Id && CanSee(i))
                    .OrderBy(i => i.SortOrder)
                    .ToList();

                if (IsGroup(mainItem))
                {
                    if (children.Count == 0) continue;
                }
                else if (!CanSee(mainItem))
                {
                    continue;
                }

                mainItem.Children = children;
                hierarchy.Add(mainItem);
            }

            return hierarchy;
        }
    }
}
