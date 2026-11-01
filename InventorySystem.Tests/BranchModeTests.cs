using System.Security.Claims;
using InventorySystem.Authorization;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InventorySystem.Tests;

public class BranchModeTests
{
    private static ApplicationDbContext Database(bool multiBranch = false, bool superAdmin = false)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        var db = new ApplicationDbContext(options, Options.Create(new BranchOptions { MultiBranchEnabled = multiBranch }));
        db.Database.EnsureCreated();
        db.ItemCategory.Add(new ItemCategory { CategoryId = 1, CategoryName = "Test category", GroupType="RawMaterial", IsActive = true });
        if (superAdmin) db.Roles.Add(new Role { Id = 900, Name = "SuperAdmin", IsActive = true });
        db.AppUsers.Add(new AppUser { Id = 900, Username = "branch-test", FullName = "Branch test", BranchIds = "2", PrimaryRoleId = superAdmin ? 900 : 1 });
        db.UserRoles.Add(new UserRole { UserId = 900, RoleId = superAdmin ? 900 : 1 });
        db.Items.Add(new Items { ItemID = 900, CategoryID = 1, ItemName = "Paper", IsActive = true });
        db.Parties.Add(new Party { PartyId = 900, PartyCode="RAW-GROUP:1", PartyName = "Supplier", PartyType = "both", Status = "active" });
        db.Stock.AddRange(new Stock { ItemId = 900, BranchId = 1, Quantity = 10 }, new Stock { ItemId = 900, BranchId = 2, Quantity = 200 });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static ActionExecutingContext Request(string controller, string action, string method, object? model = null, int? branch = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "900"), new Claim(ClaimTypes.Role, "Admin") }, "test"));
        var route = new RouteData(); route.Values["controller"] = controller; route.Values["action"] = action;
        var arguments = new Dictionary<string, object?>();
        if (model != null) arguments["model"] = model;
        if (branch.HasValue) arguments["branchId"] = branch.Value;
        return new ActionExecutingContext(new ActionContext(http, route, new ActionDescriptor(), new ModelStateDictionary()), new List<IFilterMetadata>(), arguments!, new object());
    }

    private static async Task<bool> Execute(ApplicationDbContext db, ActionExecutingContext context, bool multiBranch = false, Func<Task<IActionResult>>? action = null)
    {
        var ran = false;
        var filter = new OperationFilter(db, new PermissionService(db), NullLogger<OperationFilter>.Instance, Options.Create(new BranchOptions { MultiBranchEnabled = multiBranch }));
        await filter.OnActionExecutionAsync(context, async () =>
        {
            ran = true;
            return new ActionExecutedContext(context, new List<IFilterMetadata>(), context.Controller)
            { Result = action == null ? new OkObjectResult(new { success = true }) : await action() };
        });
        return ran;
    }

    [Fact]
    public async Task SingleBranch_IsAppliedBeforeAnyControllerAction()
    {
        using var db = Database();
        Assert.True(db.RestrictBranches);
        Assert.Equal(10, (await db.Stock.SingleAsync()).Quantity);
        Assert.Equal(2, await db.Stock.IgnoreQueryFilters().CountAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SingleBranch_AllUsersIncludingSuperAdmin_SeeOnlyDefaultStock(bool superAdmin)
    {
        using var db = Database(superAdmin: superAdmin);
        Assert.True(await Execute(db, Request("Home", "Index", "GET")));
        Assert.Equal(new[] { 1 }, db.AllowedBranchIds);
        Assert.Equal(10, (await db.Stock.SingleAsync()).Quantity);
        Assert.Equal("2", (await db.AppUsers.SingleAsync(u => u.Id == 900)).BranchIds);
    }

    [Fact]
    public async Task PurchaseWithoutBranch_UpdatesDefaultStockAndLedger()
    {
        using var db = Database();
        var model = new PurchaseInvoiceViewModel { VendorID = "900", PurchaseDate = DateTime.Today, PaymentMode = 0,
            Items = [new() { Itemid = 900, Quantity = 5, PurPrice = 20 }] };
        var controller = new PurchaseController(db, new LedgerPostingService(db, new AccountService(db)), Options.Create(new AccountingOptions()), new StockService(db));
        var context = Request("Purchase", "Save", "POST", model);
        Assert.True(await Execute(db, context, action: () => controller.Save(model)));
        Assert.Null(context.Result);
        Assert.Equal(1, model.BranchID);
        Assert.Equal(1, (await db.PurchaseInvoice.SingleAsync()).BranchID);
        Assert.Equal(15, (await db.Stock.SingleAsync()).Quantity);
        Assert.All(await db.AccountLedger.ToListAsync(), l => Assert.Equal(1, l.BranchId));
        Assert.Equal(200, (await db.Stock.IgnoreQueryFilters().SingleAsync(s => s.BranchId == 2)).Quantity);
    }

    [Theory]
    [InlineData("Sale")] [InlineData("Purchase")] [InlineData("CostSheet")] [InlineData("StockAdjustment")] [InlineData("Accounts")]
    public async Task OtherBranchWrites_AreRejectedBeforeAction(string module)
    {
        using var db = Database(superAdmin: true);
        object model = module switch
        {
            "Sale" => new SaleInvoiceViewModel { BranchID = 2 },
            "Purchase" => new PurchaseInvoiceViewModel { BranchID = 2 },
            "CostSheet" => new CostSheetViewModel { BranchID = 2 },
            "StockAdjustment" => new StockAdjustmentViewModel { BranchId = 2 },
            _ => new Voucher { BranchId = 2 }
        };
        var context = Request(module, "Save", "POST", model);
        Assert.False(await Execute(db, context));
        Assert.IsType<ForbidResult>(context.Result);
        Assert.Equal(10, (await db.Stock.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task ReportAllBranchesArgument_UsesDefaultOnly()
    {
        using var db = Database();
        var request = Request("Report", "GetStockData", "GET", branch: 0);
        Assert.True(await Execute(db, request));
        Assert.Equal(1, request.ActionArguments["branchId"]);
        request = Request("Report", "GetStockData", "GET", branch: 2);
        Assert.False(await Execute(db, request));
        Assert.IsType<ForbidResult>(request.Result);
    }

    [Fact]
    public async Task MultiBranch_CanBeRestored_WithExistingAssignments()
    {
        using var db = Database(multiBranch: true);
        var request = Request("Item", "GetItems", "GET", branch: 2);
        Assert.True(await Execute(db, request, multiBranch: true));
        Assert.Equal(new[] { 2 }, db.AllowedBranchIds);
        Assert.Equal(200, (await db.Stock.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task MultiBranch_NullCostSheetBranch_CannotBypassAssignments()
    {
        using var db = Database(multiBranch: true);
        var request = Request("CostSheet", "Save", "POST", new CostSheetViewModel());
        Assert.False(await Execute(db, request, multiBranch: true));
        Assert.IsType<ForbidResult>(request.Result);
    }

    [Fact]
    public async Task EditingUserInSingleBranchMode_PreservesFutureAssignments()
    {
        using var db = Database();
        var http = Request("Admin", "EditUser", "POST").HttpContext;
        ((ClaimsIdentity)http.User.Identity!).AddClaim(new Claim(ClaimTypes.Role, "SuperAdmin"));
        var controller = new AdminController(db, new PermissionService(db), Options.Create(new BranchOptions()))
        { ControllerContext = new ControllerContext { HttpContext = http } };
        var result = await controller.EditUser(900, new AppUser { FullName = "Updated", BranchIds = "999", IsActive = true }, new List<int> { 1 });
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("2", (await db.AppUsers.SingleAsync(u => u.Id == 900)).BranchIds);
    }
}

