using System.Security.Claims;
using InventorySystem.Authorization;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
namespace InventorySystem.Tests;

// The "Owner (View only)" role: it sees everything and can change nothing.
public class ViewOnlyUserTests
{
    private static async Task<ApplicationDbContext> SeededDb()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
        await db.Database.EnsureCreatedAsync();
        await DefaultMenuSeeder.ApplyAsync(db); await ClientWorkflowMenuSeeder.ApplyAsync(db); await AccountsFolderSeeder.ApplyAsync(db); await OwnerViewRoleSeeder.ApplyAsync(db);
        return db;
    }
    private static async Task<Role> Owner(ApplicationDbContext db) => await db.Roles.SingleAsync(r => r.Name == OwnerViewRoleSeeder.RoleName);

    // A user holding the given role, plus a "can do everything" role for comparison.
    private static async Task<int> UserIn(ApplicationDbContext db, int roleId, int id)
    {
        db.AppUsers.Add(new() { Id = id, Username = "u" + id, FullName = "User " + id, IsActive = true, BranchIds = "1" });
        db.UserRoles.Add(new() { UserId = id, RoleId = roleId }); await db.SaveChangesAsync(); return id;
    }
    private static async Task<Role> EveryPermissionRole(ApplicationDbContext db)
    {
        var role = new Role { Name = "AllPermissions", Description = "test", IsActive = true, CreatedAt = DateTime.UtcNow }; db.Roles.Add(role); await db.SaveChangesAsync();
        foreach (var p in await db.Permissions.ToListAsync()) db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = p.Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); return role;
    }

    private static async Task<(bool Ran, IActionResult? Result)> Get(ApplicationDbContext db, int userId, string controller, string action, string method = "GET", string? query = null)
    {
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "QA")) };
        http.Request.Method = method; if (query != null) http.Request.QueryString = new QueryString(query);
        var route = new RouteData(); route.Values["controller"] = controller; route.Values["action"] = action;
        var context = new ActionExecutingContext(new ActionContext(http, route, new ActionDescriptor(), new ModelStateDictionary()), [], new Dictionary<string, object>(), new object());
        var filter = new OperationFilter(db, new PermissionService(db), NullLogger<OperationFilter>.Instance, Microsoft.Extensions.Options.Options.Create(new BranchOptions())); var ran = false;
        await filter.OnActionExecutionAsync(context, () => { ran = true; return Task.FromResult(new ActionExecutedContext(context, [], context.Controller) { Result = new OkObjectResult(new { success = true }) }); });
        return (ran, context.Result);
    }

    private static async Task<int> UserWith(ApplicationDbContext db, int id, params string[] codes)
    {
        var role = new Role { Name = "Custom" + id, Description = "test", IsActive = true, CreatedAt = DateTime.UtcNow }; db.Roles.Add(role); await db.SaveChangesAsync();
        foreach (var code in codes) db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = (await db.Permissions.SingleAsync(p => p.Code == code)).Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); return await UserIn(db, role.Id, id);
    }

    // ---- nobody else is affected ----
    [Theory]
    [InlineData("Sale", new[] { "Sale.View" }, "Sale.View", "")]                                    // plain view-only user of the module
    [InlineData("Sale", new[] { "Sale.View", "Sale.Create" }, "", "")]                              // can create: left alone
    [InlineData("Sale", new[] { "Sale.View", "Sale.Edit", "Sale.Delete" }, "", "")]
    [InlineData("CostSheet", new[] { "CostSheet.View", "CostSheet.Settings" }, "", "")]             // settings only still counts as a write right
    [InlineData("Accounts", new[] { "Accounts.View", "Accounts.Post" }, "", "")]
    [InlineData("Report", new[] { "Report.View" }, "", "")]                                         // nothing to grant in this module: nothing to hide
    [InlineData("Dashboard", new[] { "Dashboard.View" }, "", "")]
    public void TheBrowserHidesButtonsOnlyForAUserWithNoWriteRight(string module, string[] held, string viewOnlyMarker, string unused)
    {
        var all = new[] { "Sale.View", "Sale.Create", "Sale.Edit", "Sale.Delete", "CostSheet.View", "CostSheet.Create", "CostSheet.Settings", "Accounts.View", "Accounts.Create", "Accounts.Post", "Report.View", "Dashboard.View" };
        var result = ViewOnlyMode.DenyVerbs(module, all, held);
        if (viewOnlyMarker == "Sale.View") Assert.Equal(ViewOnlyMode.AllWriteVerbs, result); else Assert.Equal("", result);
    }
    [Fact] public void AUserWithNoPermissionsAtAll_IsTreatedAsViewOnly_ButStillGetsNothingFromTheServer() => Assert.Equal(ViewOnlyMode.AllWriteVerbs, ViewOnlyMode.DenyVerbs("Sale", ["Sale.View", "Sale.Create"], []));

    [Fact] public async Task PartialRights_KeepWorkingExactlyAsBefore()
    {
        using var db = await SeededDb();
        // Can create but not edit or delete: the entry form, its list and saving a new record all work.
        var creator = await UserWith(db, 910, "Sale.View", "Sale.Create", "Accounts.View", "Accounts.Create", "StockAdjustment.View", "StockAdjustment.Create");
        Assert.True((await Get(db, creator, "Sale", "Index")).Ran); Assert.True((await Get(db, creator, "Sale", "List")).Ran);
        Assert.True((await Get(db, creator, "Sale", "Save", "POST")).Ran); Assert.False((await Get(db, creator, "Sale", "Update", "POST")).Ran); Assert.False((await Get(db, creator, "Sale", "Delete", "POST")).Ran);
        Assert.True((await Get(db, creator, "Accounts", "PaymentVoucher")).Ran); Assert.True((await Get(db, creator, "Accounts", "SavePayment", "POST")).Ran);
        Assert.True((await Get(db, creator, "StockAdjustment", "Create")).Ran);
        // Can edit but not create: the edit screens open; the new-record screens stay closed (they could never be saved).
        var editor = await UserWith(db, 911, "CostSheet.View", "CostSheet.Edit", "Production.View", "Production.Edit");
        // (Production's own Edit page stays SuperAdmin-only because of the existing saved-record lock, as before.)
        Assert.True((await Get(db, editor, "CostSheet", "Edit")).Ran); Assert.True((await Get(db, editor, "Production", "Consume")).Ran);
        Assert.False((await Get(db, editor, "CostSheet", "Create")).Ran);
    }
    [Fact] public async Task EveryScreenOpensForAUserWhoHasThePermissionItsSaveNeeds()
    {
        using var db = await SeededDb();
        var screens = new (string Controller, string Action, string Module, string Verb)[] {
            ("CostSheet", "Create", "CostSheet", "Create"), ("CostSheet", "Edit", "CostSheet", "Edit"), ("CostSheet", "Settings", "CostSheet", "Settings"),
            ("Production", "Create", "Production", "Create"), ("Production", "Consume", "Production", "Edit"),
            ("StockAdjustment", "Create", "StockAdjustment", "Create"), ("GatePass", "Create", "GatePass", "Create"),
            ("Accounts", "PaymentVoucher", "Accounts", "Create"), ("Accounts", "ReceiptVoucher", "Accounts", "Create"), ("Accounts", "JournalVoucher", "Accounts", "Create"),
            ("Accounts", "ContraVoucher", "Accounts", "Create"), ("Accounts", "DebitNote", "Accounts", "Create"), ("Accounts", "CreditNote", "Accounts", "Create"), ("Accounts", "OpeningBalance", "Accounts", "Create") };
        var id = 920;
        foreach (var s in screens)
        {
            var withRight = await UserWith(db, id++, s.Module + ".View", s.Module + "." + s.Verb);
            var result = await Get(db, withRight, s.Controller, s.Action); Assert.True(result.Ran, $"{s.Controller}/{s.Action} should open for a user holding {s.Module}.{s.Verb}"); Assert.Null(result.Result);
        }
    }
    [Fact] public async Task TheSeededRoles_CostSheetUser_AndFullAdmin_StillWork()
    {
        using var db = await SeededDb();
        var costSheetRole = await db.Roles.SingleAsync(r => r.Name == "CostSheetUser"); var held = await db.RolePermissions.Where(r => r.RoleId == costSheetRole.Id).Select(r => r.Permission!.Code).ToListAsync();
        var user = await UserIn(db, costSheetRole.Id, 950);
        foreach (var code in held.Where(c => c.StartsWith("CostSheet.")))
        {
            var verb = code.Split('.')[1];
            foreach (var (controller, action) in new[] { ("CostSheet", "Index"), ("CostSheet", "Create"), ("CostSheet", "Edit"), ("CostSheet", "Settings") })
            {
                var needs = WriteScreens.VerbFor(controller, action) ?? "View";
                var ran = (await Get(db, user, controller, action)).Ran;
                Assert.Equal(held.Contains("CostSheet." + needs), ran);
            }
        }
        var admin = await db.Roles.SingleAsync(r => r.Name == "Admin"); var adminUser = await UserIn(db, admin.Id, 951);
        var adminCodes = await db.RolePermissions.Where(r => r.RoleId == admin.Id).Select(r => r.Permission!.Code).ToListAsync();
        foreach (var (controller, action, module, verb) in new[] { ("Sale", "Index", "Sale", "Create"), ("Accounts", "PaymentVoucher", "Accounts", "Create"), ("CostSheet", "Create", "CostSheet", "Create"), ("GatePass", "Create", "GatePass", "Create") })
            Assert.Equal(adminCodes.Contains(module + "." + verb), (await Get(db, adminUser, controller, action)).Ran);
    }
    [Fact] public async Task TheNewRole_DoesNotChangeAnyExistingRoleOrUser()
    {
        using var db = await SeededDb();
        var before = await db.RolePermissions.Where(r => r.Role!.Name != OwnerViewRoleSeeder.RoleName).CountAsync();
        await OwnerViewRoleSeeder.ApplyAsync(db); await ClientWorkflowMenuSeeder.ApplyAsync(db);
        Assert.Equal(before, await db.RolePermissions.Where(r => r.Role!.Name != OwnerViewRoleSeeder.RoleName).CountAsync());
        Assert.Equal(1, await db.Roles.CountAsync(r => r.Name == "Admin")); Assert.Equal(1, await db.Roles.CountAsync(r => r.Name == "CostSheetUser")); Assert.Equal(1, await db.Roles.CountAsync(r => r.Name == "StandardUser"));
    }

    [Fact] public async Task TheRole_HoldsEveryViewPermission_AndNothingElse()
    {
        using var db = await SeededDb(); var role = await Owner(db);
        var codes = await db.RolePermissions.Where(r => r.RoleId == role.Id).Select(r => r.Permission!.Code).ToListAsync();
        Assert.NotEmpty(codes); Assert.All(codes, c => Assert.EndsWith(".View", c));
        Assert.Equal(await db.Permissions.CountAsync(p => p.IsActive && p.Action == "View"), codes.Count);
        foreach (var must in new[] { "GatePass.View", "Accounts.View", "Report.View", "Dashboard.View", "Sale.View", "Purchase.View", "Item.View", "Parties.View" }) Assert.Contains(must, codes);
        Assert.DoesNotContain("WhatsAppGroup.Manage", codes); Assert.DoesNotContain(codes, c => c.EndsWith(".Create") || c.EndsWith(".Edit") || c.EndsWith(".Delete") || c.EndsWith(".Post"));
    }
    [Fact] public async Task TheRole_IsCreatedOnce_AndWhatTheSuperAdminUntickedStaysUnticked()
    {
        using var db = await SeededDb(); var role = await Owner(db);
        var salary = await db.RolePermissions.Where(r => r.RoleId == role.Id && r.Permission!.Code == "Accounts.View").ToListAsync();
        db.RolePermissions.RemoveRange(salary); await db.SaveChangesAsync();
        await OwnerViewRoleSeeder.ApplyAsync(db); await OwnerViewRoleSeeder.ApplyAsync(db);
        Assert.Equal(1, await db.Roles.CountAsync(r => r.Name == OwnerViewRoleSeeder.RoleName));
        Assert.False(await db.RolePermissions.AnyAsync(r => r.RoleId == role.Id && r.Permission!.Code == "Accounts.View"));
    }

    [Fact] public void TheScreenRules_NameTheWritePagesOnly()
    {
        Assert.Equal("Create", WriteScreens.VerbFor("Accounts", "PaymentVoucher")); Assert.Equal("Create", WriteScreens.VerbFor("CostSheet", "Create"));
        Assert.Equal("Settings", WriteScreens.VerbFor("CostSheet", "Settings")); Assert.Equal("Edit", WriteScreens.VerbFor("Production", "Consume"));
        Assert.Equal("Create", WriteScreens.VerbFor("GatePass", "Create")); Assert.Equal("Edit", WriteScreens.VerbFor("GatePass", "Create", editingExisting: true));
        foreach (var read in new[] { ("Accounts", "TrialBalance"), ("Sale", "List"), ("GatePass", "Outward"), ("Report", "SaleReport"), ("Accounts", "VoucherList") }) Assert.Null(WriteScreens.VerbFor(read.Item1, read.Item2));
        Assert.True(WriteScreens.IsEntryIndex("Sale", "Index")); Assert.True(WriteScreens.IsEntryIndex("PurchaseReturn", "index")); Assert.False(WriteScreens.IsEntryIndex("Item", "Index")); Assert.False(WriteScreens.IsEntryIndex("Sale", "List"));
        Assert.Equal("Dashboard", WriteScreens.ModuleFor("Home")); Assert.Equal("ItemCategory", WriteScreens.ModuleFor("Category")); Assert.Equal("Sale", WriteScreens.ModuleFor("Sale"));
    }

    [Fact] public async Task TheMenu_ShowsListsAndReportsToTheOwner_ButNoEntryScreens()
    {
        using var db = await SeededDb(); var owner = await Owner(db); var everything = await EveryPermissionRole(db); var menu = new MenuService(db, new PermissionService(db));
        static IEnumerable<MenuItem> Flatten(IEnumerable<MenuItem> items) => items.SelectMany(i => new[] { i }.Concat(Flatten(i.Children ?? [])));
        static List<string> Pages(IEnumerable<MenuItem> items) => Flatten(items).Where(i => i.Controller != null).Select(i => i.Controller + "/" + i.Action).ToList();
        var ownerPages = Pages(await menu.GetMenuForRoleAsync(owner.Id)); var fullPages = Pages(await menu.GetMenuForRoleAsync(everything.Id));
        foreach (var keep in new[] { "Accounts/TrialBalance", "Accounts/VoucherList", "GatePass/Outward", "GatePass/Inward", "Report/SaleReport", "Item/Index", "Parties/Index", "CostSheet/Index", "Production/Index" })
        { Assert.Contains(keep, ownerPages); Assert.Contains(keep, fullPages); }
        foreach (var gone in new[] { "Accounts/PaymentVoucher", "Accounts/ReceiptVoucher", "Accounts/JournalVoucher", "Accounts/ContraVoucher", "Accounts/DebitNote", "Accounts/CreditNote", "Accounts/OpeningBalance", "CostSheet/Create", "CostSheet/Settings", "Production/Create", "StockAdjustment/Create" })
        { Assert.DoesNotContain(gone, ownerPages); if (fullPages.Contains(gone)) continue; }
        Assert.Contains("Accounts/PaymentVoucher", fullPages); Assert.Contains("CostSheet/Create", fullPages);
    }

    [Theory]
    [InlineData("Accounts", "PaymentVoucher", null)] [InlineData("Accounts", "ReceiptVoucher", null)] [InlineData("Accounts", "JournalVoucher", null)] [InlineData("Accounts", "OpeningBalance", null)]
    [InlineData("CostSheet", "Create", null)] [InlineData("CostSheet", "Settings", null)] [InlineData("Production", "Create", null)] [InlineData("StockAdjustment", "Create", null)]
    [InlineData("GatePass", "Create", "?direction=Outward")] [InlineData("GatePass", "Create", "?direction=Outward&id=5")]
    public async Task TheOwner_CannotOpenAWriteScreen_ButAFullUserCan(string controller, string action, string? query)
    {
        using var db = await SeededDb(); var owner = await UserIn(db, (await Owner(db)).Id, 901); var full = await UserIn(db, (await EveryPermissionRole(db)).Id, 902);
        var denied = await Get(db, owner, controller, action, query: query); Assert.False(denied.Ran); Assert.IsType<ForbidResult>(denied.Result);
        var allowed = await Get(db, full, controller, action, query: query); Assert.True(allowed.Ran); Assert.Null(allowed.Result);
    }
    [Theory]
    [InlineData("Sale")] [InlineData("Purchase")] [InlineData("SaleReturn")] [InlineData("PurchaseReturn")]
    public async Task TheOwner_AskingForAnEntryForm_IsSentToTheList(string controller)
    {
        using var db = await SeededDb(); var owner = await UserIn(db, (await Owner(db)).Id, 903); var full = await UserIn(db, (await EveryPermissionRole(db)).Id, 904);
        var sent = await Get(db, owner, controller, "Index"); Assert.False(sent.Ran); var redirect = Assert.IsType<RedirectToActionResult>(sent.Result); Assert.Equal("List", redirect.ActionName); Assert.Equal(controller, redirect.ControllerName);
        Assert.True((await Get(db, owner, controller, "List")).Ran);
        var form = await Get(db, full, controller, "Index"); Assert.True(form.Ran); Assert.Null(form.Result);
    }
    [Theory]
    [InlineData("Sale", "List")] [InlineData("Accounts", "TrialBalance")] [InlineData("Accounts", "VoucherList")] [InlineData("Accounts", "Expenses")] [InlineData("GatePass", "Outward")] [InlineData("GatePass", "Inward")]
    [InlineData("Report", "SaleReport")] [InlineData("Item", "Index")] [InlineData("Parties", "Index")] [InlineData("CostSheet", "Index")] [InlineData("Production", "Index")] [InlineData("Home", "Index")]
    public async Task TheOwner_CanOpenEveryReadScreen(string controller, string action)
    {
        using var db = await SeededDb(); var owner = await UserIn(db, (await Owner(db)).Id, 905);
        var result = await Get(db, owner, controller, action); Assert.True(result.Ran); Assert.Null(result.Result);
    }
    [Theory]
    [InlineData("Sale", "Save")] [InlineData("Sale", "Delete")] [InlineData("Item", "Save")] [InlineData("Parties", "save")] [InlineData("GatePass", "Save")] [InlineData("GatePass", "SendToGroup")]
    [InlineData("Accounts", "SaveVoucher")] [InlineData("Accounts", "SavePayment")] [InlineData("Accounts", "PostVoucher")] [InlineData("Production", "Save")] [InlineData("StockAdjustment", "Save")] [InlineData("WhatsAppGroup", "Connect")]
    public async Task TheOwner_CannotSaveOrChangeAnything(string controller, string action)
    {
        using var db = await SeededDb(); var owner = await UserIn(db, (await Owner(db)).Id, 906);
        var result = await Get(db, owner, controller, action, method: "POST"); Assert.False(result.Ran); Assert.IsType<ForbidResult>(result.Result);
    }
}
