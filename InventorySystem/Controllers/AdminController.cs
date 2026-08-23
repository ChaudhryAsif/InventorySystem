using InventorySystem.Data;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace InventorySystem.Controllers
{
    [Authorize]
    [Route("Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IPermissionService _permissionService;

        public AdminController(ApplicationDbContext context, IPermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        // ── CHECK ADMIN ──────────────────────────────────────────────────────
        private bool IsAdmin()
        {
            return User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        }

        private IActionResult AdminOnly()
        {
            if (!IsAdmin())
                return Forbid();
            return null!;
        }

        // ── DASHBOARD ────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var check = AdminOnly();
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
            var check = AdminOnly();
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
            var check = AdminOnly();
            if (check != null) return check;

            ViewBag.AllRoles = await _context.Roles.Where(r => r.IsActive).ToListAsync();
            
            return View();
        }

        [HttpPost("Users/Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(AppUser model, [FromForm] List<int> selectedRoles, string password)
        {
            var check = AdminOnly();
            if (check != null) return check;

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
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
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
            var check = AdminOnly();
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
            var check = AdminOnly();
            if (check != null) return check;

            var user = await _context.AppUsers
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound();

            // Update user properties
            if (!string.IsNullOrEmpty(model.FullName))
            {
                user.FullName = model.FullName;
            }

            user.IsActive = model.IsActive;

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
            var check = AdminOnly();
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
            var check = AdminOnly();
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
            var check = AdminOnly();
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
            var check = AdminOnly();
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
            var check = AdminOnly();
            if (check != null) return check;

            var menuItems = await _context.MenuItems
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            return View(menuItems);
        }

        [HttpGet("MenuItems/{id}/Edit")]
        public async Task<IActionResult> EditMenuItem(int id)
        {
            var check = AdminOnly();
            if (check != null) return check;

            var menuItem = await _context.MenuItems.FindAsync(id);
            if (menuItem == null)
                return NotFound();

            ViewBag.AllPermissions = await _context.Permissions
                .Where(p => p.IsActive)
                .OrderBy(p => p.Code)
                .ToListAsync();

            ViewBag.ParentMenuItems = await _context.MenuItems
                .Where(m => m.IsActive && m.ParentId == null && m.Id != id)
                .ToListAsync();

            return View(menuItem);
        }

        [HttpPost("MenuItems/{id}/Edit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMenuItem(int id, MenuItem model)
        {
            var check = AdminOnly();
            if (check != null) return check;

            var menuItem = await _context.MenuItems.FindAsync(id);
            if (menuItem == null)
                return NotFound();

            menuItem.Name = model.Name;
            menuItem.Icon = model.Icon;
            menuItem.Controller = model.Controller;
            menuItem.Action = model.Action;
            menuItem.RequiredPermission = model.RequiredPermission;
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
            var check = AdminOnly();
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