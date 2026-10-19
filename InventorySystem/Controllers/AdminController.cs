using InventorySystem.Data;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using Microsoft.Extensions.Options;
using InventorySystem.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace InventorySystem.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    [Route("Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IPermissionService _permissionService;
        private readonly BranchOptions _branches;

        public AdminController(ApplicationDbContext context, IPermissionService permissionService, IOptions<BranchOptions> branches)
        {
            _context = context;
            _branches = branches.Value;
            _permissionService = permissionService;
        }

        // ── CHECK SUPERADMIN ──────────────────────────────────────────────────────
        private bool IsSuperAdmin()
        {
            return User.IsInRole("SuperAdmin");
        }

        private IActionResult SuperAdminOnly()
        {
            if (!IsSuperAdmin())
                return Forbid();
            return null!;
        }

        // ── DASHBOARD ────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var stats = new
            {
                TotalUsers = await _context.AppUsers.CountAsync(),
                TotalRoles = await _context.Roles.CountAsync(),
                TotalPermissions = await _context.Permissions.CountAsync(),
                TotalMenuItems = await _context.MenuItems.CountAsync()
            };

            ViewBag.Stats = stats;
            return View();
        }

        // ── USERS ────────────────────────────────────────────────────────────
        [HttpGet("Users")]
        public async Task<IActionResult> Users()
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var users = await _context.AppUsers
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .Include(u => u.PrimaryRole)
                .OrderBy(u => u.Username)
                .ToListAsync();

            return View(users);
        }

        [HttpGet("Users/Create")]
        public async Task<IActionResult> CreateUser()
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            ViewBag.AllRoles = await _context.Roles.Where(r => r.IsActive).ToListAsync();
            
            return View();
        }

        [HttpPost("Users/Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(AppUser model, [FromForm] List<int> selectedRoles, string password)
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            if (_branches.MultiBranchEnabled && (string.IsNullOrWhiteSpace(model.BranchIds) || !System.Text.RegularExpressions.Regex.IsMatch(model.BranchIds, @"^[1-9][0-9]*(,[1-9][0-9]*)*$") || model.BranchIds.Length > 200))
                return BadRequest("Enter valid comma-separated branch IDs.");
            // Validate form data
            if (string.IsNullOrWhiteSpace(model.Username))
            {
                ModelState.AddModelError("Username", "Username is required.");
            }

            if (string.IsNullOrWhiteSpace(model.FullName))
            {
                ModelState.AddModelError("FullName", "Full name is required.");
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError("password", "Password is required.");
            }
            else if (password.Length < 6)
            {
                ModelState.AddModelError("password", "Password must be at least 6 characters long.");
            }

            // Check if username already exists
            var userExists = await _context.AppUsers.AnyAsync(u => u.Username == model.Username);
            if (userExists)
            {
                ModelState.AddModelError("Username", "Username already exists.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.AllRoles = await _context.Roles.Where(r => r.IsActive).ToListAsync();
                return View(model);
            }

            // Hash password
            var user = new AppUser
            {
                Username = model.Username,
                FullName = model.FullName,
                BranchIds = _branches.MultiBranchEnabled ? model.BranchIds : BranchOptions.DefaultBranchId.ToString(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role = "", // No legacy role when no roles were assigned.
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            // Add roles
            if (selectedRoles?.Count > 0)
            {
                user.PrimaryRoleId = selectedRoles.First();
                user.Role = (await _context.Roles.FindAsync(user.PrimaryRoleId))?.Name ?? "Admin";
            }

            _context.AppUsers.Add(user);
            await _context.SaveChangesAsync();

            // Add user roles
            foreach (var roleId in selectedRoles ?? new List<int>())
            {
                var roleExists = await _context.Roles.AnyAsync(r => r.Id == roleId);
                if (roleExists)
                {
                    _context.UserRoles.Add(new UserRole
                    {
                        UserId = user.Id,
                        RoleId = roleId,
                        AssignedAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"User '{model.Username}' created successfully!";
            return RedirectToAction("Users");
        }

        [HttpGet("Users/{id}/Edit")]
        public async Task<IActionResult> EditUser(int id)
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var user = await _context.AppUsers
                .Include(u => u.UserRoles)
                .Include(u => u.PrimaryRole)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound();

            ViewBag.AllRoles = await _context.Roles.Where(r => r.IsActive).ToListAsync();
            ViewBag.UserRoleIds = user.UserRoles?.Select(ur => ur.RoleId).ToList() ?? new List<int>();

            return View(user);
        }

        [HttpPost("Users/{id}/Edit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(int id, AppUser model, [FromForm] List<int> selectedRoles)
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var user = await _context.AppUsers
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound();

            if (_branches.MultiBranchEnabled && (string.IsNullOrWhiteSpace(model.BranchIds) || !System.Text.RegularExpressions.Regex.IsMatch(model.BranchIds, @"^[1-9][0-9]*(,[1-9][0-9]*)*$") || model.BranchIds.Length > 200))
                return BadRequest("Enter valid comma-separated branch IDs.");
            // Update user properties
            if (!string.IsNullOrEmpty(model.FullName))
            {
                user.FullName = model.FullName;
            }

            user.IsActive = model.IsActive;
            if (_branches.MultiBranchEnabled) user.BranchIds = model.BranchIds;

            // Update roles
            _context.UserRoles.RemoveRange(user.UserRoles!);

            foreach (var roleId in selectedRoles ?? new List<int>())
            {
                var roleExists = await _context.Roles.AnyAsync(r => r.Id == roleId);
                if (roleExists)
                {
                    _context.UserRoles.Add(new UserRole
                    {
                        UserId = user.Id,
                        RoleId = roleId,
                        AssignedAt = DateTime.UtcNow
                    });
                }
            }

            // Rebuild both representations; removing roles must revoke the old primary role too.
            user.PrimaryRoleId = null;
            user.Role = "";
            // Set primary role
            if (selectedRoles?.Count > 0)
            {
                user.PrimaryRoleId = selectedRoles.First();
                user.Role = (await _context.Roles.FindAsync(user.PrimaryRoleId))?.Name ?? "Admin";
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Users");
        }

        // ── ROLES ────────────────────────────────────────────────────────────
        [HttpGet("Roles")]
        public async Task<IActionResult> Roles()
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var roles = await _context.Roles
                .Include(r => r.RolePermissions)
                .OrderBy(r => r.Name)
                .ToListAsync();

            return View(roles);
        }

        [HttpGet("Roles/{id}/Permissions")]
        public async Task<IActionResult> ManageRolePermissions(int id)
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var role = await _context.Roles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (role == null)
                return NotFound();

            var allPermissions = await _context.Permissions
                .Where(p => p.IsActive)
                .OrderBy(p => p.Module)
                .ThenBy(p => p.Action)
                .ToListAsync();

            var rolePermissionIds = role.RolePermissions?.Select(rp => rp.PermissionId).ToList() ?? new List<int>();

            ViewBag.Role = role;
            ViewBag.RolePermissionIds = rolePermissionIds;
            ViewBag.GroupedPermissions = allPermissions.GroupBy(p => p.Module);

            return View(allPermissions);
        }

        [HttpPost("Roles/{id}/Permissions")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveRolePermissions(int id, [FromForm] List<int> permissions)
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var role = await _context.Roles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (role == null)
                return NotFound();

            // Remove old permissions
            _context.RolePermissions.RemoveRange(role.RolePermissions!);

            // Add new permissions
            foreach (var permissionId in permissions ?? new List<int>())
            {
                var permExists = await _context.Permissions.AnyAsync(p => p.Id == permissionId);
                if (permExists)
                {
                    _context.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.Id,
                        PermissionId = permissionId,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Role permissions updated successfully!";
            return RedirectToAction("Roles");
        }

        // ── PERMISSIONS ──────────────────────────────────────────────────────
        [HttpGet("Permissions")]
        public async Task<IActionResult> Permissions()
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var permissions = await _context.Permissions
                .OrderBy(p => p.Module)
                .ThenBy(p => p.Action)
                .ToListAsync();

            return View(permissions);
        }

        // ── MENU ITEMS ───────────────────────────────────────────────────────
        [HttpGet("MenuItems")]
        public async Task<IActionResult> MenuItems()
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var menuItems = await _context.MenuItems
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            ViewBag.MenuVersion = MenuVersion(menuItems);
            return View(menuItems);
        }

        [HttpGet("MenuItems/{id}/Edit")]
        public async Task<IActionResult> EditMenuItem(int id)
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var menuItem = await _context.MenuItems.FindAsync(id);
            if (menuItem == null)
                return NotFound();

            ViewBag.MenuVersion = MenuVersion(await _context.MenuItems.AsNoTracking().ToListAsync());
            ViewBag.ParentMenuItems = await _context.MenuItems
                .Where(m => m.IsActive && m.ParentId == null && m.Id != id && (m.Controller == null || m.Controller == ""))
                .ToListAsync();

            return View(menuItem);
        }

        public class MenuNodeDto
        {
            public int Id { get; set; }
            public List<int>? Children { get; set; }
        }

        public class MenuOrderInput
        {
            public string? Version { get; set; }
            public List<MenuNodeDto>? Items { get; set; }
        }

        // Edit input without [Required] on the permission, since groups have none.
        public class MenuItemEditInput
        {
            [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(100)]
            public string Name { get; set; } = string.Empty;
            [System.ComponentModel.DataAnnotations.MaxLength(500)]
            public string? Icon { get; set; }
            public int? ParentId { get; set; }
            public int SortOrder { get; set; }
            public bool IsActive { get; set; }
            public string? Version { get; set; }
        }

        public class MenuGroupDto
        {
            public string? Name { get; set; }
            public string? Icon { get; set; }
        }

        private static bool IsMenuGroup(MenuItem m) => string.IsNullOrEmpty(m.Controller);

        // Includes display and structural settings, so a stale editor cannot undo another save.
        // The global OperationFilter checks and writes inside one serializable transaction.
        public static string MenuVersion(IEnumerable<MenuItem> items)
        {
            var snapshot = items.OrderBy(m => m.Id).Select(m => new {
                m.Id, m.Name, m.Icon, m.Controller, m.Action, m.RequiredPermission,
                m.ParentId, m.SortOrder, m.IsActive, m.CreatedAt
            });
            var bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(snapshot));
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }

        // Returns an error message, or null when the whole tree is valid.
        public static string? ValidateMenuTree(List<MenuNodeDto> tree, List<MenuItem> all)
        {
            var byId = all.ToDictionary(m => m.Id);
            var seen = new HashSet<int>();

            foreach (var node in tree)
            {
                if (node == null) return "Invalid menu entry. Reload the page and try again.";
                if (!byId.TryGetValue(node.Id, out var top)) return "A menu item no longer exists. Reload the page and try again.";
                if (!seen.Add(node.Id)) return $"'{top.Name}' appears more than once.";

                var children = node.Children ?? new List<int>();
                if (children.Count > 0 && !IsMenuGroup(top))
                    return $"'{top.Name}' is a page and cannot contain other pages.";

                foreach (var childId in children)
                {
                    if (!byId.TryGetValue(childId, out var child)) return "A menu item no longer exists. Reload the page and try again.";
                    if (!seen.Add(childId)) return $"'{child.Name}' appears more than once.";
                    if (IsMenuGroup(child)) return $"Group '{child.Name}' cannot be placed inside another group.";
                }
            }

            if (seen.Count != all.Count) return "The menu changed since this page loaded. Reload the page and try again.";
            return null;
        }

        [HttpPost("MenuItems/SaveOrder")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveMenuOrder([FromBody] MenuOrderInput input)
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var all = await _context.MenuItems.ToListAsync();
            if (input == null || input.Version != MenuVersion(all))
                return Json(new { success = false, message = "The menu changed since this page loaded. Reload before saving; your changes have not been applied." });
            var tree = input.Items ?? new List<MenuNodeDto>();
            var error = ValidateMenuTree(tree, all);
            if (error != null) return Json(new { success = false, message = error });

            var byId = all.ToDictionary(m => m.Id);
            for (var i = 0; i < tree!.Count; i++)
            {
                var top = byId[tree[i].Id];
                top.ParentId = null;
                top.SortOrder = i + 1;

                var children = tree[i].Children ?? new List<int>();
                for (var j = 0; j < children.Count; j++)
                {
                    var child = byId[children[j]];
                    child.ParentId = top.Id;
                    child.SortOrder = j + 1;
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost("MenuItems/CreateGroup")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateMenuGroup([FromBody] MenuGroupDto model)
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var name = model?.Name?.Trim() ?? "";
            if (name.Length == 0 || name.Length > 100)
                return Json(new { success = false, message = "Enter a name of 1 to 100 characters." });

            var exists = await _context.MenuItems.AnyAsync(m =>
                (m.Controller == null || m.Controller == "") && m.Name.ToLower() == name.ToLower());
            if (exists) return Json(new { success = false, message = $"A group named '{name}' already exists." });

            var maxOrder = await _context.MenuItems.Where(m => m.ParentId == null).MaxAsync(m => (int?)m.SortOrder) ?? 0;
            var icon = (model!.Icon ?? "").Trim();
            if (icon.Length == 0) icon = "📁";

            _context.MenuItems.Add(new MenuItem
            {
                Name = name,
                Icon = icon.Length > 500 ? icon[..500] : icon,
                SortOrder = maxOrder + 1,
                RequiredPermission = "",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost("MenuItems/{id}/DeleteGroup")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMenuGroup(int id)
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var group = await _context.MenuItems.FindAsync(id);
            if (group == null) return Json(new { success = false, message = "Group not found." });
            if (!IsMenuGroup(group)) return Json(new { success = false, message = "Only groups can be deleted, not pages." });
            if (await _context.MenuItems.AnyAsync(m => m.ParentId == id))
                return Json(new { success = false, message = $"'{group.Name}' still contains pages. Move them out first." });

            _context.MenuItems.Remove(group);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost("MenuItems/{id}/Edit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMenuItem(int id, MenuItemEditInput model)
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var menuItem = await _context.MenuItems.FindAsync(id);
            if (menuItem == null)
                return NotFound();

            var all = await _context.MenuItems.ToListAsync();
            var group = IsMenuGroup(menuItem);
            var parent = model.ParentId.HasValue ? await _context.MenuItems.FindAsync(model.ParentId.Value) : null;
            string? invalid = null;
            if (model.Version != MenuVersion(all)) invalid = "The menu changed since this page loaded. Reload before editing; your changes have not been applied.";
            else if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.Name) || model.Name.Trim().Length > 100 || model.Icon?.Length > 500) invalid = "Enter a name of 1 to 100 characters and an icon of at most 500 characters.";
            else if (group && model.ParentId.HasValue) invalid = "Main menu groups must stay at the top level. Only pages can be moved into a group.";
            else if (model.ParentId == id) invalid = "A menu item cannot be its own parent.";
            else if (parent != null && (parent.ParentId != null || !IsMenuGroup(parent))) invalid = "Pages can only be placed inside a top-level group.";
            else if (model.ParentId.HasValue && parent == null) invalid = "Parent menu not found.";
            if (invalid == null && group && all.Any(m => m.Id != id && IsMenuGroup(m) && string.Equals(m.Name.Trim(), model.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                invalid = "A main menu with this name already exists.";
            if (invalid != null)
            {
                TempData["Error"] = invalid;
                return RedirectToAction("EditMenuItem", new { id });
            }

            menuItem.Name = model.Name.Trim();
            menuItem.Icon = string.IsNullOrWhiteSpace(model.Icon) ? (IsMenuGroup(menuItem) ? "📁" : "📄") : model.Icon.Trim();
            menuItem.SortOrder = model.SortOrder;
            menuItem.IsActive = model.IsActive;
            menuItem.ParentId = model.ParentId;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Menu item updated successfully!";
            return RedirectToAction("MenuItems");
        }

        // ── USER ACTIVITY ────────────────────────────────────────────────────
        [HttpGet("Report")]
        public async Task<IActionResult> Report()
        {
            var check = SuperAdminOnly();
            if (check != null) return check;

            var report = new
            {
                Users = await _context.AppUsers.CountAsync(u => u.IsActive),
                InactiveUsers = await _context.AppUsers.CountAsync(u => !u.IsActive),
                Roles = await _context.Roles.CountAsync(r => r.IsActive),
                Permissions = await _context.Permissions.CountAsync(p => p.IsActive),
                MenuItems = await _context.MenuItems.CountAsync(m => m.IsActive)
            };

            return View(report);
        }
    }
}
