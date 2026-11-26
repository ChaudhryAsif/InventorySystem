using System.Security.Claims;
using System.Text;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Controllers;

// Roles, the permission grid, user access tools and the access log. SuperAdmin only, like the rest of the Admin area.
public partial class AdminController
{
    // These roles are created by the system and looked up by name, so they can be tuned but never renamed, switched off or deleted.
    // SuperAdmin always has everything, so its permissions are not editable at all.
    private const string SuperAdminRoleName = "SuperAdmin";
    private static readonly HashSet<string> SystemRoles = new(StringComparer.OrdinalIgnoreCase) { SuperAdminRoleName, "Admin", OwnerViewRoleSeeder.RoleName };
    private static readonly string[] StandardVerbs = ["View", "Create", "Edit", "Delete", "Post", EditSavedPermissions.Action];

    public static readonly Dictionary<string, string> ModuleTitles = new()
    {
        ["Dashboard"] = "Dashboard", ["Item"] = "Items", ["ItemCategory"] = "Item Groups", ["Parties"] = "Parties", ["Purchase"] = "Purchase Invoices",
        ["PurchaseReturn"] = "Purchase Returns", ["Sale"] = "Sale Invoices", ["SaleReturn"] = "Sale Returns", ["StockAdjustment"] = "Stock Adjustment",
        ["CostSheet"] = "Cost Sheets", ["Production"] = "Production", ["Accounts"] = "Accounts", ["Report"] = "Reports", ["GatePass"] = "Gate Pass",
        ["WhatsApp"] = "WhatsApp Chatbot", ["WhatsAppGroup"] = "WhatsApp Group Sending"
    };

    private int? ActorId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private void Audit(string action, string target, string? detail = null) => _context.AdminAuditLogs.Add(new()
    {
        ActorId = ActorId, ActorName = User.Identity?.Name ?? "", Action = action, Target = target.Length > 200 ? target[..200] : target,
        Detail = detail is { Length: > 2000 } ? detail[..2000] : detail, CreatedUtc = DateTime.UtcNow
    });
    private static bool IsSystemRole(Role role) => SystemRoles.Contains(role.Name);
    private static bool IsSuperAdminRole(Role role) => role.Name.Equals(SuperAdminRoleName, StringComparison.OrdinalIgnoreCase);

    // ── view models ──────────────────────────────────────────────────────────
    public sealed record RoleRow(Role Role, int Users, int Permissions, bool IsSystem);
    public sealed class RoleInput
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int? CopyFromRoleId { get; set; }
    }
    public sealed class PermCell { public int Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string Description { get; set; } = ""; public string Action { get; set; } = ""; public bool Checked { get; set; } }
    public sealed class ModuleRow { public string Module { get; set; } = ""; public string Title { get; set; } = ""; public List<PermCell> Cells { get; set; } = []; }
    public sealed record RoleChoice(int Id, string Name, int[] PermissionIds);
    public sealed class PermissionGrid { public Role Role { get; set; } = null!; public bool IsSystem { get; set; } public int Users { get; set; } public List<ModuleRow> Modules { get; set; } = []; public List<RoleChoice> CopyFrom { get; set; } = []; }
    public sealed class PasswordInput { public string? NewPassword { get; set; } public string? ConfirmPassword { get; set; } }
    public sealed class AccessSummary { public AppUser User { get; set; } = null!; public bool IsSuperAdmin { get; set; } public List<string> Roles { get; set; } = []; public List<(string Module, List<string> Verbs)> Modules { get; set; } = []; }
    public sealed class AccessLogPage { public List<AdminAuditLog> Rows { get; set; } = []; public int Page { get; set; } public int Pages { get; set; } public int Total { get; set; } public string? Query { get; set; } public string? Action { get; set; } public List<string> Actions { get; set; } = []; }

    // ── ROLES ────────────────────────────────────────────────────────────────
    [HttpGet("Roles")]
    public async Task<IActionResult> Roles()
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var roles = await _context.Roles.OrderBy(r => r.Name).ToListAsync();
        var users = await _context.UserRoles.GroupBy(u => u.RoleId).Select(g => new { RoleId = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.RoleId, x => x.N);
        var perms = await _context.RolePermissions.GroupBy(p => p.RoleId).Select(g => new { RoleId = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.RoleId, x => x.N);
        return View(roles.Select(r => new RoleRow(r, users.GetValueOrDefault(r.Id), perms.GetValueOrDefault(r.Id), IsSystemRole(r))).ToList());
    }

    private async Task<string?> ValidateRoleNameAsync(RoleInput input, int? ignoreId)
    {
        var name = (input.Name ?? "").Trim();
        if (name.Length < 2 || name.Length > 100) return "Enter a role name of 2 to 100 characters.";
        if (name.Equals(SuperAdminRoleName, StringComparison.OrdinalIgnoreCase)) return "That name is reserved for the SuperAdmin role.";
        if (await _context.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower() && r.Id != (ignoreId ?? 0))) return "A role with that name already exists.";
        if ((input.Description ?? "").Length > 500) return "The description can be at most 500 characters.";
        return null;
    }

    [HttpGet("Roles/Create")]
    public async Task<IActionResult> CreateRole()
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        ViewBag.Roles = await _context.Roles.OrderBy(r => r.Name).ToListAsync();
        return View("RoleForm", new RoleInput());
    }

    [HttpPost("Roles/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRole(RoleInput input)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var error = await ValidateRoleNameAsync(input, null);
        if (error != null) { ModelState.AddModelError("Name", error); ViewBag.Roles = await _context.Roles.OrderBy(r => r.Name).ToListAsync(); return View("RoleForm", input); }
        var role = new Role { Name = input.Name!.Trim(), Description = (input.Description ?? "").Trim(), IsActive = true, CreatedAt = DateTime.UtcNow };
        _context.Roles.Add(role); await _context.SaveChangesWithAuditAsync();
        var detail = "No permissions yet.";
        if (input.CopyFromRoleId is int source)
        {
            var from = await _context.Roles.FindAsync(source);
            if (from != null && !IsSuperAdminRole(from))
            {
                var ids = await _context.RolePermissions.Where(p => p.RoleId == source).Select(p => p.PermissionId).ToListAsync();
                foreach (var id in ids) _context.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = id, CreatedAt = DateTime.UtcNow });
                detail = $"Started from '{from.Name}' ({ids.Count} permissions).";
            }
        }
        Audit("Role created", role.Name, detail); await _context.SaveChangesWithAuditAsync();
        TempData["Success"] = $"Role '{role.Name}' created. Choose what it can do below.";
        return RedirectToAction(nameof(ManageRolePermissions), new { id = role.Id });
    }

    [HttpGet("Roles/{id:int}/Edit")]
    public async Task<IActionResult> EditRole(int id)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var role = await _context.Roles.FindAsync(id); if (role == null) return NotFound();
        if (IsSuperAdminRole(role)) { TempData["Error"] = "The SuperAdmin role cannot be changed."; return RedirectToAction(nameof(Roles)); }
        ViewBag.IsSystem = IsSystemRole(role);
        return View("RoleForm", new RoleInput { Id = role.Id, Name = role.Name, Description = role.Description });
    }

    [HttpPost("Roles/{id:int}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditRole(int id, RoleInput input)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var role = await _context.Roles.FindAsync(id); if (role == null) return NotFound();
        if (IsSuperAdminRole(role)) { TempData["Error"] = "The SuperAdmin role cannot be changed."; return RedirectToAction(nameof(Roles)); }
        var system = IsSystemRole(role); input.Id = id;
        if (system) input.Name = role.Name;   // system roles keep their name
        var error = await ValidateRoleNameAsync(input, id);
        if (error != null) { ModelState.AddModelError("Name", error); ViewBag.IsSystem = system; return View("RoleForm", input); }
        var oldName = role.Name; var newName = input.Name!.Trim(); var newDescription = (input.Description ?? "").Trim();
        if (oldName == newName && role.Description == newDescription) { TempData["Success"] = "No changes to save."; return RedirectToAction(nameof(Roles)); }
        role.Name = newName; role.Description = newDescription;
        // The older single-role column mirrors the name for the primary role, so keep it in step.
        foreach (var user in await _context.AppUsers.Where(u => u.PrimaryRoleId == id).ToListAsync()) user.Role = newName;
        Audit("Role changed", newName, oldName != newName ? $"Renamed from '{oldName}'." : "Description changed.");
        await _context.SaveChangesWithAuditAsync();
        TempData["Success"] = $"Role '{newName}' saved." + (oldName != newName ? " People already signed in keep the old name until they sign in again." : "");
        return RedirectToAction(nameof(Roles));
    }

    [HttpPost("Roles/{id:int}/Duplicate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DuplicateRole(int id)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var role = await _context.Roles.FindAsync(id); if (role == null) return NotFound();
        if (IsSuperAdminRole(role)) { TempData["Error"] = "The SuperAdmin role cannot be copied."; return RedirectToAction(nameof(Roles)); }
        var name = role.Name + " (copy)"; var n = 2;
        while (await _context.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower())) name = $"{role.Name} (copy {n++})";
        var copy = new Role { Name = name, Description = role.Description, IsActive = true, CreatedAt = DateTime.UtcNow };
        _context.Roles.Add(copy); await _context.SaveChangesWithAuditAsync();
        var ids = await _context.RolePermissions.Where(p => p.RoleId == id).Select(p => p.PermissionId).ToListAsync();
        foreach (var pid in ids) _context.RolePermissions.Add(new() { RoleId = copy.Id, PermissionId = pid, CreatedAt = DateTime.UtcNow });
        Audit("Role created", copy.Name, $"Copy of '{role.Name}' ({ids.Count} permissions)."); await _context.SaveChangesWithAuditAsync();
        TempData["Success"] = $"Created '{copy.Name}'. Rename it and adjust its permissions.";
        return RedirectToAction(nameof(EditRole), new { id = copy.Id });
    }

    [HttpPost("Roles/{id:int}/Toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleRole(int id)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var role = await _context.Roles.FindAsync(id); if (role == null) return NotFound();
        if (IsSystemRole(role)) { TempData["Error"] = $"'{role.Name}' is a system role and cannot be switched off."; return RedirectToAction(nameof(Roles)); }
        role.IsActive = !role.IsActive;
        var users = await _context.UserRoles.CountAsync(u => u.RoleId == id);
        Audit(role.IsActive ? "Role activated" : "Role deactivated", role.Name, $"{users} user(s) hold this role.");
        await _context.SaveChangesWithAuditAsync();
        TempData["Success"] = role.IsActive ? $"'{role.Name}' is active again." : $"'{role.Name}' is switched off. Its {users} user(s) lose its access until it is switched on again.";
        return RedirectToAction(nameof(Roles));
    }

    [HttpPost("Roles/{id:int}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRole(int id)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var role = await _context.Roles.FindAsync(id); if (role == null) return NotFound();
        if (IsSystemRole(role)) { TempData["Error"] = $"'{role.Name}' is a system role and cannot be deleted."; return RedirectToAction(nameof(Roles)); }
        var users = await _context.UserRoles.CountAsync(u => u.RoleId == id) + await _context.AppUsers.CountAsync(u => u.PrimaryRoleId == id && !u.UserRoles!.Any(r => r.RoleId == id));
        if (users > 0) { TempData["Error"] = $"{users} user(s) still have '{role.Name}'. Move them to another role first (Admin, Users)."; return RedirectToAction(nameof(Roles)); }
        _context.RolePermissions.RemoveRange(_context.RolePermissions.Where(p => p.RoleId == id));
        _context.Roles.Remove(role);
        Audit("Role deleted", role.Name); await _context.SaveChangesWithAuditAsync();
        TempData["Success"] = $"Role '{role.Name}' deleted.";
        return RedirectToAction(nameof(Roles));
    }

    // ── PERMISSION GRID ──────────────────────────────────────────────────────
    [HttpGet("Roles/{id:int}/Permissions")]
    public async Task<IActionResult> ManageRolePermissions(int id)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var role = await _context.Roles.FindAsync(id); if (role == null) return NotFound();
        if (IsSuperAdminRole(role)) { TempData["Error"] = "SuperAdmin always has full access. There is nothing to choose."; return RedirectToAction(nameof(Roles)); }
        var held = (await _context.RolePermissions.Where(p => p.RoleId == id).Select(p => p.PermissionId).ToListAsync()).ToHashSet();
        var all = await _context.Permissions.Where(p => p.IsActive).OrderBy(p => p.Module).ThenBy(p => p.Action).ToListAsync();
        var grid = new PermissionGrid { Role = role, IsSystem = IsSystemRole(role), Users = await _context.UserRoles.CountAsync(u => u.RoleId == id) };
        foreach (var group in all.GroupBy(p => p.Module).OrderBy(g => ModuleTitles.GetValueOrDefault(g.Key, g.Key)))
            grid.Modules.Add(new ModuleRow
            {
                Module = group.Key, Title = ModuleTitles.GetValueOrDefault(group.Key, group.Key),
                Cells = group.Select(p => new PermCell { Id = p.Id, Code = p.Code, Name = p.Name, Description = p.Description, Action = p.Action, Checked = held.Contains(p.Id) }).ToList()
            });
        var others = await _context.Roles.Where(r => r.Id != id && r.IsActive).OrderBy(r => r.Name).ToListAsync();
        var rolePermissions = await _context.RolePermissions.Where(p => p.RoleId != id).Select(p => new { p.RoleId, p.PermissionId }).ToListAsync();
        grid.CopyFrom = others.Where(r => !IsSuperAdminRole(r)).Select(r => new RoleChoice(r.Id, r.Name, rolePermissions.Where(p => p.RoleId == r.Id).Select(p => p.PermissionId).ToArray())).ToList();
        return View(grid);
    }

    [HttpPost("Roles/{id:int}/Permissions")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRolePermissions(int id, [FromForm] List<int> permissions)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var role = await _context.Roles.FindAsync(id); if (role == null) return NotFound();
        if (IsSuperAdminRole(role)) { TempData["Error"] = "SuperAdmin always has full access and cannot be changed."; return RedirectToAction(nameof(Roles)); }
        var all = await _context.Permissions.Where(p => p.IsActive).ToListAsync();
        var wanted = all.Where(p => (permissions ?? []).Contains(p.Id)).ToList();
        // A write right is useless without View, and "Edit saved records" is useless without Edit: add what is missing instead of saving a half-working role.
        var codes = wanted.Select(p => p.Code).ToHashSet();
        var added = new List<string>();
        foreach (var module in wanted.Select(p => p.Module).Distinct().ToList())
        {
            if (codes.Contains(EditSavedPermissions.Code(module)) && all.FirstOrDefault(p => p.Code == module + ".Edit") is { } edit && codes.Add(edit.Code)) { wanted.Add(edit); added.Add(edit.Code); }
            if (all.FirstOrDefault(p => p.Code == module + ".View") is { } view && codes.Add(view.Code)) { wanted.Add(view); added.Add(view.Code); }
        }
        var before = (await _context.RolePermissions.Where(p => p.RoleId == id).ToListAsync());
        var beforeIds = before.Select(p => p.PermissionId).ToHashSet(); var afterIds = wanted.Select(p => p.Id).ToHashSet();
        var removed = before.Where(p => !afterIds.Contains(p.PermissionId)).ToList();
        var toAdd = wanted.Where(p => !beforeIds.Contains(p.Id)).ToList();
        if (removed.Count == 0 && toAdd.Count == 0) { TempData["Success"] = "No changes to save."; return RedirectToAction(nameof(Roles)); }
        _context.RolePermissions.RemoveRange(removed);
        foreach (var p in toAdd) _context.RolePermissions.Add(new RolePermission { RoleId = id, PermissionId = p.Id, CreatedAt = DateTime.UtcNow });
        var byId = all.ToDictionary(p => p.Id, p => p.Code);
        string List(IEnumerable<string> items) { var l = items.OrderBy(x => x).ToList(); return l.Count <= 30 ? string.Join(", ", l) : string.Join(", ", l.Take(30)) + $" and {l.Count - 30} more"; }
        var detail = new StringBuilder();
        if (toAdd.Count > 0) detail.Append("Added: ").Append(List(toAdd.Select(p => p.Code))).Append(". ");
        if (removed.Count > 0) detail.Append("Removed: ").Append(List(removed.Select(p => byId.GetValueOrDefault(p.PermissionId, "#" + p.PermissionId)))).Append('.');
        Audit("Permissions changed", role.Name, detail.ToString().Trim());
        await _context.SaveChangesWithAuditAsync();
        var users = await _context.UserRoles.CountAsync(u => u.RoleId == id);
        TempData["Success"] = $"Permissions for '{role.Name}' saved ({toAdd.Count} added, {removed.Count} removed)."
            + (added.Count > 0 ? $" View or Edit was added automatically where a module needed it ({added.Count})." : "") + (users > 0 ? $" It applies to {users} user(s) on their next click." : "");
        return RedirectToAction(nameof(Roles));
    }

    // ── USERS ────────────────────────────────────────────────────────────────
    [HttpGet("Users")]
    public async Task<IActionResult> Users(string? q = null, int? roleId = null, string? status = null)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var query = _context.AppUsers.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).Include(u => u.PrimaryRole).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim(); query = query.Where(u => u.Username.Contains(term) || u.FullName.Contains(term)); }
        if (roleId is > 0) query = query.Where(u => u.UserRoles!.Any(r => r.RoleId == roleId));
        if (status == "active") query = query.Where(u => u.IsActive); else if (status == "inactive") query = query.Where(u => !u.IsActive);
        ViewBag.Q = q; ViewBag.RoleId = roleId; ViewBag.Status = status ?? "all";
        ViewBag.Roles = await _context.Roles.OrderBy(r => r.Name).ToListAsync();
        ViewBag.Me = ActorId;
        return View(await query.OrderBy(u => u.Username).ToListAsync());
    }

    [HttpGet("Users/{id:int}/Access")]
    public async Task<IActionResult> UserAccess(int id)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var user = await _context.AppUsers.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.Id == id); if (user == null) return NotFound();
        var summary = new AccessSummary { User = user, Roles = user.UserRoles?.Where(r => r.Role != null).Select(r => r.Role!.Name + (r.Role.IsActive ? "" : " (switched off)")).OrderBy(n => n).ToList() ?? [] };
        summary.IsSuperAdmin = user.UserRoles?.Any(r => r.Role is { IsActive: true } && IsSuperAdminRole(r.Role)) == true;
        if (!summary.IsSuperAdmin)
        {
            var held = (await _permissionService.GetUserPermissionsAsync(id)).Where(c => user.IsActive).ToList();
            foreach (var group in held.GroupBy(c => c.Split('.')[0]).OrderBy(g => ModuleTitles.GetValueOrDefault(g.Key, g.Key)))
                summary.Modules.Add((ModuleTitles.GetValueOrDefault(group.Key, group.Key), group.Select(c => c.Split('.')[1]).OrderBy(v => Array.IndexOf(StandardVerbs, v) is var i && i < 0 ? 99 : i).ToList()));
        }
        return View(summary);
    }

    [HttpGet("Users/{id:int}/Password")]
    public async Task<IActionResult> ResetPassword(int id)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var user = await _context.AppUsers.FindAsync(id); if (user == null) return NotFound();
        ViewBag.UserName = user.Username; ViewBag.FullName = user.FullName; ViewBag.UserId = id;
        return View(new PasswordInput());
    }

    [HttpPost("Users/{id:int}/Password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int id, PasswordInput input)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var user = await _context.AppUsers.FindAsync(id); if (user == null) return NotFound();
        ViewBag.UserName = user.Username; ViewBag.FullName = user.FullName; ViewBag.UserId = id;
        if (string.IsNullOrEmpty(input.NewPassword) || input.NewPassword.Length < 6) ModelState.AddModelError("NewPassword", "The password must be at least 6 characters long.");
        else if (input.NewPassword != input.ConfirmPassword) ModelState.AddModelError("ConfirmPassword", "The two passwords do not match.");
        if (!ModelState.IsValid) return View(new PasswordInput());
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(input.NewPassword);
        Audit("Password reset", user.Username, "A new password was set by the SuperAdmin.");   // the password itself is never recorded
        await _context.SaveChangesWithAuditAsync();
        TempData["Success"] = $"New password set for '{user.Username}'. Give it to them privately.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("Users/{id:int}/Enable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnableUser(int id)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        var user = await _context.AppUsers.FindAsync(id); if (user == null) return NotFound();
        user.IsActive = true; Audit("User enabled", user.Username); await _context.SaveChangesWithAuditAsync();
        TempData["Success"] = $"'{user.Username}' can sign in again.";
        return RedirectToAction(nameof(Users));
    }

    // ── ACCESS LOG ───────────────────────────────────────────────────────────
    [HttpGet("AccessLog")]
    public async Task<IActionResult> AccessLog(string? q = null, string? kind = null, int page = 1)
    {
        var check = SuperAdminOnly(); if (check != null) return check;
        try
        {
            var query = _context.AdminAuditLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(l => l.Action == kind);   // not named "action": that is a reserved route value
            if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim(); query = query.Where(l => l.Target.Contains(term) || l.ActorName.Contains(term) || (l.Detail ?? "").Contains(term)); }
            const int size = 50; var total = await query.CountAsync(); var pages = Math.Max(1, (int)Math.Ceiling(total / (double)size)); page = Math.Clamp(page, 1, pages);
            return View(new AccessLogPage
            {
                Rows = await query.OrderByDescending(l => l.Id).Skip((page - 1) * size).Take(size).ToListAsync(), Page = page, Pages = pages, Total = total, Query = q, Action = kind,
                Actions = await _context.AdminAuditLogs.Select(l => l.Action).Distinct().OrderBy(a => a).ToListAsync()
            });
        }
        catch (System.Data.Common.DbException)
        {
            // The log table is installed by AccessControl.sql; until then the page explains instead of failing.
            ViewBag.Missing = true;
            return View(new AccessLogPage { Page = 1, Pages = 1 });
        }
    }
}
