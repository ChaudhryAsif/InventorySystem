using System.Security.Claims;
using InventorySystem.Authorization;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
namespace InventorySystem.Tests;

// Roles, the permission grid, user access tools, the access log, change password and "Edit saved records".
public class AccessControlTests
{
    private sealed class MemoryTempData : ITempDataProvider
    {
        private Dictionary<string, object?> data = [];
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>(data);
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) => data = new(values);
    }

    private static async Task<ApplicationDbContext> SeededDb()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
        await db.Database.EnsureCreatedAsync();
        await DefaultMenuSeeder.ApplyAsync(db); await ClientWorkflowMenuSeeder.ApplyAsync(db); await AccountsFolderSeeder.ApplyAsync(db);
        await AccessPermissionSeeder.ApplyAsync(db); await OwnerViewRoleSeeder.ApplyAsync(db);
        if (!await db.Roles.AnyAsync(r => r.Name == "SuperAdmin")) { db.Roles.Add(new Role { Name = "SuperAdmin", Description = "Everything", IsActive = true, CreatedAt = DateTime.UtcNow }); await db.SaveChangesAsync(); }
        return db;
    }
    private static async Task<AppUser> AddUser(ApplicationDbContext db, string name, params string[] roles)
    {
        var user = new AppUser { Username = name, FullName = name + " Name", IsActive = true, BranchIds = "1", CreatedAt = DateTime.UtcNow, PasswordHash = BCrypt.Net.BCrypt.HashPassword("Old@1234"), Role = "" };
        db.AppUsers.Add(user); await db.SaveChangesAsync();
        foreach (var role in roles) db.UserRoles.Add(new() { UserId = user.Id, RoleId = (await db.Roles.SingleAsync(r => r.Name == role)).Id, AssignedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); return user;
    }
    private static async Task<Role> AddRole(ApplicationDbContext db, string name, params string[] codes)
    {
        var role = new Role { Name = name, Description = "test", IsActive = true, CreatedAt = DateTime.UtcNow }; db.Roles.Add(role); await db.SaveChangesAsync();
        foreach (var code in codes) db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = (await db.Permissions.SingleAsync(p => p.Code == code)).Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); return role;
    }
    private static async Task<List<string>> Held(ApplicationDbContext db, int roleId) => await db.RolePermissions.Where(r => r.RoleId == roleId).Select(r => r.Permission!.Code).OrderBy(c => c).ToListAsync();
    private static int[] Ids(ApplicationDbContext db, params string[] codes) => db.Permissions.Where(p => codes.Contains(p.Code)).Select(p => p.Id).ToArray();

    // The controller as the SuperAdmin signed in as `actor`.
    private static AdminController Admin(ApplicationDbContext db, AppUser actor)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.Id.ToString()), new Claim(ClaimTypes.Name, actor.Username), new Claim(ClaimTypes.Role, "SuperAdmin")], "Test")) };
        return new AdminController(db, new PermissionService(db), Microsoft.Extensions.Options.Options.Create(new BranchOptions())) { ControllerContext = new() { HttpContext = http }, TempData = new TempDataDictionary(http, new MemoryTempData()) };
    }
    private static async Task<(AdminController Controller, AppUser Actor)> AsSuperAdmin(ApplicationDbContext db) { var actor = await AddUser(db, "boss", "SuperAdmin"); return (Admin(db, actor), actor); }
    private static string Msg(Controller c, string key) => c.TempData[key]?.ToString() ?? "";
    private static async Task<List<AdminAuditLog>> Log(ApplicationDbContext db) => await db.AdminAuditLogs.OrderBy(l => l.Id).ToListAsync();

    // ---- roles ----
    [Fact] public async Task TheRoleList_ShowsUsersAndPermissionCounts_AndMarksSystemRoles()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var role = await AddRole(db, "Manager", "Sale.View", "Sale.Create"); await AddUser(db, "m1", "Manager");
        var rows = Assert.IsAssignableFrom<List<AdminController.RoleRow>>(Assert.IsType<ViewResult>(await c.Roles()).Model);
        var manager = rows.Single(r => r.Role.Id == role.Id); Assert.Equal(1, manager.Users); Assert.Equal(2, manager.Permissions); Assert.False(manager.IsSystem);
        Assert.True(rows.Single(r => r.Role.Name == "Admin").IsSystem); Assert.True(rows.Single(r => r.Role.Name == OwnerViewRoleSeeder.RoleName).IsSystem); Assert.True(rows.Single(r => r.Role.Name == "SuperAdmin").IsSystem);
    }
    [Fact] public async Task CreateRole_MakesAnActiveRole_LogsIt_AndOpensThePermissionGrid()
    {
        using var db = await SeededDb(); var (c, actor) = await AsSuperAdmin(db);
        var result = Assert.IsType<RedirectToActionResult>(await c.CreateRole(new() { Name = "  Manager  ", Description = "Runs the floor" }));
        var role = await db.Roles.SingleAsync(r => r.Name == "Manager"); Assert.True(role.IsActive); Assert.Equal("Runs the floor", role.Description);
        Assert.Equal(nameof(AdminController.ManageRolePermissions), result.ActionName); Assert.Equal(role.Id, result.RouteValues!["id"]);
        var log = Assert.Single(await Log(db), l => l.Action == "Role created"); Assert.Equal("Manager", log.Target); Assert.Equal("boss", log.ActorName); Assert.Equal(actor.Id, log.ActorId);
    }
    [Theory]
    [InlineData("")] [InlineData(" ")] [InlineData("A")] [InlineData("SuperAdmin")] [InlineData("superadmin")] [InlineData("admin")] [InlineData("ADMIN")]
    public async Task CreateRole_RefusesBadOrTakenNames(string name)
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var before = await db.Roles.CountAsync();
        var view = Assert.IsType<ViewResult>(await c.CreateRole(new() { Name = name })); Assert.Equal("RoleForm", view.ViewName); Assert.True(c.ModelState["Name"]!.Errors.Count > 0);
        Assert.Equal(before, await db.Roles.CountAsync()); Assert.DoesNotContain(await Log(db), l => l.Action == "Role created");
    }
    [Fact] public async Task CreateRole_CanStartFromACopy_ButNeverFromSuperAdmin()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var source = await AddRole(db, "Source", "Item.View", "Item.Create", "Sale.View");
        await c.CreateRole(new() { Name = "Copy A", CopyFromRoleId = source.Id });
        Assert.Equal(["Item.Create", "Item.View", "Sale.View"], await Held(db, (await db.Roles.SingleAsync(r => r.Name == "Copy A")).Id));
        await c.CreateRole(new() { Name = "Copy B", CopyFromRoleId = (await db.Roles.SingleAsync(r => r.Name == "SuperAdmin")).Id });
        Assert.Empty(await Held(db, (await db.Roles.SingleAsync(r => r.Name == "Copy B")).Id));
    }
    [Fact] public async Task EditRole_RenamesACustomRole_AndKeepsTheOldRoleColumnInStep()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var role = await AddRole(db, "Manager"); var user = await AddUser(db, "m1", "Manager"); user.PrimaryRoleId = role.Id; user.Role = "Manager"; await db.SaveChangesAsync();
        Assert.IsType<RedirectToActionResult>(await c.EditRole(role.Id, new() { Name = "Supervisor", Description = "New text" }));
        Assert.Equal("Supervisor", (await db.Roles.FindAsync(role.Id))!.Name); Assert.Equal("Supervisor", (await db.AppUsers.FindAsync(user.Id))!.Role);
        Assert.Contains("Renamed from 'Manager'", (await Log(db)).Single(l => l.Action == "Role changed").Detail);
    }
    [Fact] public async Task EditRole_SystemRolesKeepTheirName_AndSuperAdminCannotBeEditedAtAll()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var admin = await db.Roles.SingleAsync(r => r.Name == "Admin"); var super = await db.Roles.SingleAsync(r => r.Name == "SuperAdmin");
        await c.EditRole(admin.Id, new() { Name = "Renamed", Description = "Only the text changes" });
        Assert.Equal("Admin", (await db.Roles.FindAsync(admin.Id))!.Name); Assert.Equal("Only the text changes", (await db.Roles.FindAsync(admin.Id))!.Description);
        Assert.IsType<RedirectToActionResult>(await c.EditRole(super.Id, new() { Name = "X" })); Assert.Equal("SuperAdmin", (await db.Roles.FindAsync(super.Id))!.Name); Assert.Contains("cannot be changed", Msg(c, "Error"));
        var other = await AddRole(db, "Other"); var clash = Assert.IsType<ViewResult>(await c.EditRole(other.Id, new() { Name = "admin" })); Assert.Equal("RoleForm", clash.ViewName);
    }
    [Fact] public async Task DuplicateRole_CopiesThePermissions_WithAUniqueName()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var role = await AddRole(db, "Manager", "Sale.View", "Sale.Edit");
        await c.DuplicateRole(role.Id); await c.DuplicateRole(role.Id);
        Assert.Equal(["Manager", "Manager (copy 2)", "Manager (copy)"], await db.Roles.Where(r => r.Name.StartsWith("Manager")).OrderBy(r => r.Name).Select(r => r.Name).ToListAsync());
        Assert.Equal(["Sale.Edit", "Sale.View"], await Held(db, (await db.Roles.SingleAsync(r => r.Name == "Manager (copy)")).Id));
        await c.DuplicateRole((await db.Roles.SingleAsync(r => r.Name == "SuperAdmin")).Id); Assert.False(await db.Roles.AnyAsync(r => r.Name == "SuperAdmin (copy)"));
    }
    [Fact] public async Task ToggleRole_SwitchesACustomRole_ButNotASystemRole()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var role = await AddRole(db, "Manager"); await AddUser(db, "m1", "Manager");
        await c.ToggleRole(role.Id); Assert.False((await db.Roles.FindAsync(role.Id))!.IsActive); Assert.Contains("1 user(s) lose", Msg(c, "Success"));
        await c.ToggleRole(role.Id); Assert.True((await db.Roles.FindAsync(role.Id))!.IsActive);
        var admin = await db.Roles.SingleAsync(r => r.Name == "Admin"); await c.ToggleRole(admin.Id); Assert.True((await db.Roles.FindAsync(admin.Id))!.IsActive); Assert.Contains("system role", Msg(c, "Error"));
        Assert.Equal(["Role activated", "Role deactivated"], (await Log(db)).Where(l => l.Action.StartsWith("Role ")).Select(l => l.Action).OrderBy(a => a).ToList());
    }
    [Fact] public async Task DeleteRole_OnlyWhenNoOneHasIt_AndNeverForSystemRoles()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var used = await AddRole(db, "Used", "Sale.View"); await AddUser(db, "u1", "Used"); var free = await AddRole(db, "Free", "Sale.View");
        await c.DeleteRole(used.Id); Assert.NotNull(await db.Roles.FindAsync(used.Id)); Assert.Contains("still have", Msg(c, "Error"));
        await c.DeleteRole(free.Id); Assert.Null(await db.Roles.FindAsync(free.Id)); Assert.Empty(await db.RolePermissions.Where(r => r.RoleId == free.Id).ToListAsync());
        foreach (var name in new[] { "Admin", "SuperAdmin", OwnerViewRoleSeeder.RoleName }) { var role = await db.Roles.SingleAsync(r => r.Name == name); await c.DeleteRole(role.Id); Assert.NotNull(await db.Roles.FindAsync(role.Id)); }
        Assert.Contains(await Log(db), l => l.Action == "Role deleted" && l.Target == "Free");
    }

    // ---- the permission grid ----
    [Fact] public async Task TheGrid_ListsEveryModule_WithWhatTheRoleHolds_AndEditSavedColumns()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var role = await AddRole(db, "Manager", "Sale.View", "Sale.Create");
        var grid = Assert.IsType<AdminController.PermissionGrid>(Assert.IsType<ViewResult>(await c.ManageRolePermissions(role.Id)).Model);
        var sale = grid.Modules.Single(m => m.Module == "Sale"); Assert.Equal("Sale Invoices", sale.Title);
        Assert.True(sale.Cells.Single(x => x.Action == "View").Checked); Assert.True(sale.Cells.Single(x => x.Action == "Create").Checked); Assert.False(sale.Cells.Single(x => x.Action == "Edit").Checked);
        Assert.Contains(sale.Cells, x => x.Action == "EditSaved"); Assert.Contains(grid.Modules, m => m.Module == "WhatsAppGroup");
        Assert.DoesNotContain(grid.CopyFrom, r => r.Id == role.Id); Assert.DoesNotContain(grid.CopyFrom, r => r.Name == "SuperAdmin"); Assert.Contains(grid.CopyFrom, r => r.Name == "Admin");
    }
    [Fact] public async Task TheGrid_IsNotOfferedForSuperAdmin()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var super = await db.Roles.SingleAsync(r => r.Name == "SuperAdmin");
        Assert.IsType<RedirectToActionResult>(await c.ManageRolePermissions(super.Id)); Assert.IsType<RedirectToActionResult>(await c.SaveRolePermissions(super.Id, Ids(db, "Sale.View").ToList()));
        Assert.Empty(await Held(db, super.Id)); Assert.Contains("full access", Msg(c, "Error"));
    }
    [Fact] public async Task Saving_AddsAndRemovesOnlyTheDifference_AndLogsIt()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var role = await AddRole(db, "Manager", "Sale.View", "Sale.Create", "Item.View");
        await c.SaveRolePermissions(role.Id, Ids(db, "Sale.View", "Sale.Create", "Sale.Edit", "Item.View").ToList());
        Assert.Equal(["Item.View", "Sale.Create", "Sale.Edit", "Sale.View"], await Held(db, role.Id));
        await c.SaveRolePermissions(role.Id, Ids(db, "Sale.View", "Item.View").ToList());
        Assert.Equal(["Item.View", "Sale.View"], await Held(db, role.Id));
        var logs = (await Log(db)).Where(l => l.Action == "Permissions changed").ToList(); Assert.Equal(2, logs.Count);
        Assert.Equal("Added: Sale.Edit.", logs[0].Detail); Assert.Equal("Removed: Sale.Create, Sale.Edit.", logs[1].Detail);
    }
    [Fact] public async Task Saving_NothingNew_SaysSo_AndWritesNoLogLine()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var role = await AddRole(db, "Manager", "Sale.View");
        await c.SaveRolePermissions(role.Id, Ids(db, "Sale.View").ToList()); Assert.Contains("No changes", Msg(c, "Success")); Assert.DoesNotContain(await Log(db), l => l.Action == "Permissions changed");
    }
    [Fact] public async Task Saving_AWriteRightWithoutView_AddsViewAutomatically()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var role = await AddRole(db, "Manager");
        await c.SaveRolePermissions(role.Id, Ids(db, "Sale.Delete", "Accounts.Post").ToList());
        Assert.Equal(["Accounts.Post", "Accounts.View", "Sale.Delete", "Sale.View"], await Held(db, role.Id)); Assert.Contains("added automatically", Msg(c, "Success"));
    }
    [Fact] public async Task Saving_EditSavedWithoutEdit_AddsEditAndView()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var role = await AddRole(db, "Manager");
        await c.SaveRolePermissions(role.Id, Ids(db, "Sale.EditSaved").ToList());
        Assert.Equal(["Sale.Edit", "Sale.EditSaved", "Sale.View"], await Held(db, role.Id));
    }
    [Fact] public async Task Saving_IgnoresIdsThatAreNotPermissions()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var role = await AddRole(db, "Manager");
        await c.SaveRolePermissions(role.Id, [999999, .. Ids(db, "Item.View")]); Assert.Equal(["Item.View"], await Held(db, role.Id));
    }

    // ---- users ----
    [Fact] public async Task TheUserList_FiltersByTextRoleAndStatus()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); await AddRole(db, "Manager"); await AddUser(db, "alice", "Manager"); var bob = await AddUser(db, "bob", "Manager"); bob.IsActive = false; await AddUser(db, "carol", "Admin"); await db.SaveChangesAsync();
        static List<string> Names(IActionResult r) => Assert.IsAssignableFrom<IEnumerable<AppUser>>(Assert.IsType<ViewResult>(r).Model).Select(u => u.Username).OrderBy(n => n).ToList();
        Assert.Subset(Names(await c.Users()).ToHashSet(), new HashSet<string> { "alice", "bob", "boss", "carol" });
        Assert.Equal(["alice"], Names(await c.Users(q: "ali"))); Assert.Equal(["alice", "bob"], Names(await c.Users(roleId: (await db.Roles.SingleAsync(r => r.Name == "Manager")).Id)));
        Assert.Equal(["bob"], Names(await c.Users(status: "inactive"))); Assert.DoesNotContain("bob", Names(await c.Users(status: "active")));
    }
    [Fact] public async Task TheAccessSummary_ShowsWhatTheUserCanDo_AndFullAccessForSuperAdmin()
    {
        using var db = await SeededDb(); var (c, boss) = await AsSuperAdmin(db); await AddRole(db, "Manager", "Sale.View", "Sale.Create", "Item.View"); var user = await AddUser(db, "m1", "Manager");
        var summary = Assert.IsType<AdminController.AccessSummary>(Assert.IsType<ViewResult>(await c.UserAccess(user.Id)).Model);
        Assert.False(summary.IsSuperAdmin); Assert.Equal(["Manager"], summary.Roles);
        Assert.Equal(["View", "Create"], summary.Modules.Single(m => m.Module == "Sale Invoices").Verbs); Assert.Equal(["View"], summary.Modules.Single(m => m.Module == "Items").Verbs);
        var super = Assert.IsType<AdminController.AccessSummary>(Assert.IsType<ViewResult>(await c.UserAccess(boss.Id)).Model); Assert.True(super.IsSuperAdmin); Assert.Empty(super.Modules);
    }
    [Fact] public async Task ResetPassword_SetsANewPassword_ChecksIt_AndNeverLogsIt()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var user = await AddUser(db, "m1");
        Assert.IsType<ViewResult>(await c.ResetPassword(user.Id, new() { NewPassword = "abc", ConfirmPassword = "abc" })); Assert.True(BCrypt.Net.BCrypt.Verify("Old@1234", (await db.AppUsers.FindAsync(user.Id))!.PasswordHash));
        c.ModelState.Clear(); Assert.IsType<ViewResult>(await c.ResetPassword(user.Id, new() { NewPassword = "Brand@123", ConfirmPassword = "Different" })); Assert.True(BCrypt.Net.BCrypt.Verify("Old@1234", (await db.AppUsers.FindAsync(user.Id))!.PasswordHash));
        c.ModelState.Clear(); Assert.IsType<RedirectToActionResult>(await c.ResetPassword(user.Id, new() { NewPassword = "Brand@123", ConfirmPassword = "Brand@123" }));
        var saved = (await db.AppUsers.FindAsync(user.Id))!; Assert.True(BCrypt.Net.BCrypt.Verify("Brand@123", saved.PasswordHash)); Assert.False(BCrypt.Net.BCrypt.Verify("Old@1234", saved.PasswordHash));
        var log = Assert.Single(await Log(db), l => l.Action == "Password reset"); Assert.Equal("m1", log.Target); Assert.DoesNotContain("Brand", log.Detail); Assert.DoesNotContain(saved.PasswordHash, log.Detail ?? "");
    }
    [Fact] public async Task EnableUser_LetsADisabledUserSignInAgain_AndIsLogged()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db); var user = await AddUser(db, "m1"); user.IsActive = false; await db.SaveChangesAsync();
        await c.EnableUser(user.Id); Assert.True((await db.AppUsers.FindAsync(user.Id))!.IsActive); Assert.Contains(await Log(db), l => l.Action == "User enabled" && l.Target == "m1");
    }
    [Fact] public async Task DisablingAUser_IsLogged_AndTheLastSuperAdminCannotBeDisabled()
    {
        using var db = await SeededDb(); var (c, boss) = await AsSuperAdmin(db); var user = await AddUser(db, "m1");
        Assert.IsType<JsonResult>(await c.DeleteUser(user.Id)); Assert.False((await db.AppUsers.FindAsync(user.Id))!.IsActive); Assert.Contains(await Log(db), l => l.Action == "User disabled" && l.Target == "m1");
        var second = await AddUser(db, "boss2", "SuperAdmin"); Assert.IsType<JsonResult>(await c.DeleteUser(second.Id));       // another SuperAdmin remains (boss)
        var lone = Admin(db, boss); Assert.IsType<BadRequestObjectResult>(await lone.DeleteUser(boss.Id));                   // cannot disable yourself
    }
    [Fact] public async Task EditUser_CannotRemoveYourOwnSuperAdminAccess_OrTheLastSuperAdmin_ButChangesAreLogged()
    {
        using var db = await SeededDb(); var (c, boss) = await AsSuperAdmin(db); var superId = (await db.Roles.SingleAsync(r => r.Name == "SuperAdmin")).Id; var managerRole = await AddRole(db, "Manager");
        Assert.IsType<BadRequestObjectResult>(await c.EditUser(boss.Id, new AppUser { FullName = "Boss", IsActive = true, BranchIds = "1" }, [managerRole.Id]));
        Assert.IsType<BadRequestObjectResult>(await c.EditUser(boss.Id, new AppUser { FullName = "Boss", IsActive = false, BranchIds = "1" }, [superId]));
        var plain = await AddUser(db, "m1"); Assert.IsType<RedirectToActionResult>(await c.EditUser(plain.Id, new AppUser { FullName = "M One", IsActive = true, BranchIds = "1" }, [managerRole.Id]));
        var log = Assert.Single(await Log(db), l => l.Action == "User changed"); Assert.Equal("m1", log.Target); Assert.Contains("none -> Manager", log.Detail);
        var other = await AddUser(db, "boss2", "SuperAdmin"); Assert.IsType<RedirectToActionResult>(await c.EditUser(other.Id, new AppUser { FullName = "Boss 2", IsActive = true, BranchIds = "1" }, [managerRole.Id]));   // boss still stands
    }

    // ---- the access log ----
    [Fact] public async Task TheAccessLog_PagesNewestFirst_AndFilters()
    {
        using var db = await SeededDb(); var (c, _) = await AsSuperAdmin(db);
        for (var i = 1; i <= 60; i++) db.AdminAuditLogs.Add(new() { ActorName = "boss", Action = i % 2 == 0 ? "Role created" : "Password reset", Target = "item " + i, Detail = i == 7 ? "needle in the detail" : null });
        await db.SaveChangesAsync();
        var first = Assert.IsType<AdminController.AccessLogPage>(Assert.IsType<ViewResult>(await c.AccessLog()).Model); Assert.Equal(50, first.Rows.Count); Assert.Equal(2, first.Pages); Assert.Equal("item 60", first.Rows[0].Target);
        var second = Assert.IsType<AdminController.AccessLogPage>(Assert.IsType<ViewResult>(await c.AccessLog(page: 2)).Model); Assert.Equal(10, second.Rows.Count);
        Assert.Equal(30, ((AdminController.AccessLogPage)((ViewResult)await c.AccessLog(kind: "Role created")).Model!).Total);
        Assert.Equal("item 7", ((AdminController.AccessLogPage)((ViewResult)await c.AccessLog(q: "needle")).Model!).Rows.Single().Target);
        Assert.Contains("Role created", first.Actions); Assert.Equal(2, ((AdminController.AccessLogPage)((ViewResult)await c.AccessLog(page: 99)).Model!).Page);
    }

    // ---- change my password ----
    private static AuthController Auth(ApplicationDbContext db, AppUser? user)
    {
        var http = new DefaultHttpContext(); if (user != null) http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "Test"));
        return new AuthController(db, new PermissionService(db)) { ControllerContext = new() { HttpContext = http } };
    }
    [Fact] public async Task ChangePassword_NeedsTheCurrentPassword_AMatchingNewOne_AndRecordsTheChange()
    {
        using var db = await SeededDb(); var user = await AddUser(db, "m1"); var c = Auth(db, user);
        Assert.IsType<ViewResult>(await c.ChangePassword(new() { CurrentPassword = "wrong", NewPassword = "New@1234", ConfirmPassword = "New@1234" })); Assert.True(c.ModelState["CurrentPassword"]!.Errors.Count > 0);
        c.ModelState.Clear(); await c.ChangePassword(new() { CurrentPassword = "Old@1234", NewPassword = "abc", ConfirmPassword = "abc" }); Assert.True(c.ModelState["NewPassword"]!.Errors.Count > 0);
        c.ModelState.Clear(); await c.ChangePassword(new() { CurrentPassword = "Old@1234", NewPassword = "New@1234", ConfirmPassword = "Other@1234" }); Assert.True(c.ModelState["ConfirmPassword"]!.Errors.Count > 0);
        c.ModelState.Clear(); await c.ChangePassword(new() { CurrentPassword = "Old@1234", NewPassword = "Old@1234", ConfirmPassword = "Old@1234" }); Assert.True(c.ModelState["NewPassword"]!.Errors.Count > 0);
        Assert.True(BCrypt.Net.BCrypt.Verify("Old@1234", (await db.AppUsers.FindAsync(user.Id))!.PasswordHash)); Assert.Empty(await Log(db));
        c.ModelState.Clear(); var ok = Assert.IsType<ViewResult>(await c.ChangePassword(new() { CurrentPassword = "Old@1234", NewPassword = "New@1234", ConfirmPassword = "New@1234" })); Assert.Equal(true, c.ViewBag.Done);
        Assert.True(BCrypt.Net.BCrypt.Verify("New@1234", (await db.AppUsers.FindAsync(user.Id))!.PasswordHash));
        var log = Assert.Single(await Log(db)); Assert.Equal("Password changed", log.Action); Assert.Equal("m1", log.ActorName); Assert.DoesNotContain("New@1234", log.Detail);
    }
    [Fact] public async Task ChangePassword_IsOnlyForSignedInActiveUsers()
    {
        using var db = await SeededDb(); Assert.IsType<ChallengeResult>(await Auth(db, null).ChangePassword(new() { CurrentPassword = "x", NewPassword = "yyyyyy", ConfirmPassword = "yyyyyy" }));
        var gone = await AddUser(db, "gone"); gone.IsActive = false; await db.SaveChangesAsync(); Assert.IsType<ChallengeResult>(await Auth(db, gone).ChangePassword(new() { CurrentPassword = "Old@1234", NewPassword = "New@1234", ConfirmPassword = "New@1234" }));
        Assert.NotNull(typeof(AuthController).GetMethod(nameof(AuthController.ChangePassword), [typeof(AuthController.ChangePasswordInput)])!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false).SingleOrDefault());
    }

    // An action parameter named "action" or "controller" is filled from the URL route, not from the query string (it once made the access log empty).
    [Fact] public void NoActionParameterUsesAReservedRouteName()
    {
        var bad = typeof(AdminController).Assembly.GetTypes().Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly), (t, m) => (t, m))
            .Where(x => x.m.ReturnType.Name.Contains("IActionResult") || x.m.ReturnType.Name.Contains("Task"))
            .SelectMany(x => x.m.GetParameters().Where(p => p.Name is "action" or "controller" or "area" or "handler" or "page" && p.Name != "page"), (x, p) => $"{x.t.Name}.{x.m.Name}({p.Name})").ToList();
        Assert.Empty(bad);
    }

    [Fact] public void TheAccessLogShowsPakistanTime_WhateverTheServerClockSays()
        => Assert.Equal(new DateTime(2026, 11, 27, 17, 30, 0), ShopTime.FromUtc(new DateTime(2026, 11, 27, 12, 30, 0)));

    // ---- the audit save never blocks a real change ----
    [Fact] public async Task SavingWithAnAuditLine_SavesBoth()
    {
        using var db = await SeededDb(); var role = new Role { Name = "R", Description = "", IsActive = true, CreatedAt = DateTime.UtcNow }; db.Roles.Add(role); db.AdminAuditLogs.Add(new() { ActorName = "a", Action = "Role created", Target = "R" });
        await db.SaveChangesWithAuditAsync(); Assert.True(await db.Roles.AnyAsync(r => r.Name == "R")); Assert.Equal(1, await db.AdminAuditLogs.CountAsync());
    }

    // ---- "Edit saved records" ----
    private static async Task<(bool Ran, IActionResult? Result)> Run(ApplicationDbContext db, AppUser user, string controller, string action, string method = "GET", string? query = null, int modelId = 7)
    {
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "QA")) }; http.Request.Method = method; if (query != null) http.Request.QueryString = new QueryString(query);
        var route = new RouteData(); route.Values["controller"] = controller; route.Values["action"] = action;
        var context = new ActionExecutingContext(new ActionContext(http, route, new ActionDescriptor(), new ModelStateDictionary()), [], new Dictionary<string, object> { ["model"] = new GatePass { Id = modelId, BranchId = 1, PartyName = "x" } }, new object());
        var ran = false;
        await new OperationFilter(db, new PermissionService(db), NullLogger<OperationFilter>.Instance, Microsoft.Extensions.Options.Options.Create(new BranchOptions())).OnActionExecutionAsync(context, () => { ran = true; return Task.FromResult(new ActionExecutedContext(context, [], context.Controller) { Result = new OkObjectResult(new { success = true }) }); });
        return (ran, context.Result);
    }
    [Fact] public async Task TheEditSavedPermission_ExistsForEachLockedModule_IsCreatedOnce_AndGivenToNobody()
    {
        using var db = await SeededDb(); await AccessPermissionSeeder.ApplyAsync(db); await AccessPermissionSeeder.ApplyAsync(db);
        Assert.Equal(EditSavedPermissions.Modules.Length, await db.Permissions.CountAsync(p => p.Action == EditSavedPermissions.Action));
        foreach (var module in EditSavedPermissions.Modules) Assert.Equal(1, await db.Permissions.CountAsync(p => p.Code == module + ".EditSaved"));
        Assert.False(await db.RolePermissions.AnyAsync(r => r.Permission!.Action == EditSavedPermissions.Action));
    }
    [Theory]
    [InlineData("Sale", "Update", "POST")] [InlineData("Purchase", "Update", "POST")] [InlineData("SaleReturn", "Update", "POST")] [InlineData("PurchaseReturn", "Update", "POST")] [InlineData("GatePass", "Save", "POST")]
    public async Task ASavedRecord_CanBeEditedOnlyByARoleThatHoldsEditSaved(string controller, string action, string method)
    {
        using var db = await SeededDb(); var module = WriteScreens.ModuleFor(controller);
        var plain = await AddUser(db, "plain", (await AddRole(db, "PlainEditor", module + ".View", module + ".Edit")).Name);
        var trusted = await AddUser(db, "trusted", (await AddRole(db, "TrustedEditor", module + ".View", module + ".Edit", module + ".EditSaved")).Name);
        var denied = await Run(db, plain, controller, action, method); Assert.False(denied.Ran); Assert.Equal(403, Assert.IsType<ObjectResult>(denied.Result).StatusCode);
        var allowed = await Run(db, trusted, controller, action, method); Assert.True(allowed.Ran); Assert.Null(allowed.Result);
    }
    [Fact] public async Task EditSaved_DoesNotReplaceTheEditPermission()
    {
        using var db = await SeededDb(); var user = await AddUser(db, "odd", (await AddRole(db, "OddRole", "Sale.View", "Sale.EditSaved")).Name);
        var result = await Run(db, user, "Sale", "Update", "POST"); Assert.False(result.Ran);   // it still needs Sale.Edit
    }
    [Fact] public async Task TheSavedRecordLock_StaysForEveryoneElse_AsBefore()
    {
        using var db = await SeededDb(); var full = await AddUser(db, "full", (await AddRole(db, "NoEditSaved", "Sale.View", "Sale.Create", "Sale.Edit", "Sale.Delete")).Name);
        Assert.False((await Run(db, full, "Sale", "Update", "POST")).Ran);                           // locked: no EditSaved
        Assert.True((await Run(db, full, "Sale", "Delete", "POST")).Ran);                            // delete and create stay open
        Assert.True((await Run(db, full, "Sale", "Save", "POST", modelId: 0)).Ran);                  // a new record (no id) is not "saved" yet
        var admin = await AddUser(db, "adm", "Admin"); var adminCodes = await Held(db, (await db.Roles.SingleAsync(r => r.Name == "Admin")).Id);
        Assert.DoesNotContain(adminCodes, c => c.EndsWith(".EditSaved"));                            // even Admin does not gain it silently
        Assert.False((await Run(db, admin, "Sale", "Update", "POST")).Ran);
    }
    [Fact] public async Task ProductionEditPage_FollowsTheSameRule()
    {
        using var db = await SeededDb(); var plain = await AddUser(db, "p1", (await AddRole(db, "ProdPlain", "Production.View", "Production.Edit")).Name); var trusted = await AddUser(db, "p2", (await AddRole(db, "ProdTrusted", "Production.View", "Production.Edit", "Production.EditSaved")).Name);
        Assert.False((await Run(db, plain, "Production", "Edit")).Ran); Assert.True((await Run(db, trusted, "Production", "Edit")).Ran);
    }
    [Fact] public async Task EntryFormWithAnId_OpensForARoleThatMayEditSaved_WithoutCreate()
    {
        using var db = await SeededDb(); var editor = await AddUser(db, "e1", (await AddRole(db, "SaleEditor", "Sale.View", "Sale.Edit", "Sale.EditSaved")).Name);
        Assert.True((await Run(db, editor, "Sale", "Index", query: "?id=5")).Ran);                  // editing a saved sale
        var noId = await Run(db, editor, "Sale", "Index"); Assert.False(noId.Ran); Assert.IsType<RedirectToActionResult>(noId.Result);   // a new sale needs Create
    }
    [Fact] public async Task TheWhatsAppPage_FollowsItsPermission_NotARoleName()
    {
        using var db = await SeededDb(); var holder = await AddUser(db, "w1", (await AddRole(db, "Sender", "WhatsAppGroup.Manage")).Name); var stranger = await AddUser(db, "w2", (await AddRole(db, "Nobody", "Sale.View")).Name);
        Assert.True((await Run(db, holder, "WhatsAppGroup", "Index")).Ran); Assert.False((await Run(db, stranger, "WhatsAppGroup", "Index")).Ran);
        Assert.Null(typeof(WhatsAppGroupController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().FirstOrDefault()?.Roles);
    }
    [Fact] public async Task TheOwnerRole_StaysViewOnly_EvenThoughModulesNowHaveAnEditSavedPermission()
    {
        using var db = await SeededDb(); var owner = await db.Roles.SingleAsync(r => r.Name == OwnerViewRoleSeeder.RoleName); var codes = await Held(db, owner.Id);
        Assert.All(codes, c => Assert.EndsWith(".View", c));
        var moduleCodes = await db.Permissions.Where(p => p.Code.StartsWith("Sale.")).Select(p => p.Code).ToListAsync();
        Assert.Equal(ViewOnlyMode.AllWriteVerbs, ViewOnlyMode.DenyVerbs("Sale", moduleCodes, codes));
    }
}
