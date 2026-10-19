using System.Security.Claims;
using InventorySystem.Authorization;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InventorySystem.Tests;

public class AdminPanelAccessTests
{
    [Fact]
    public void AdminRoutes_RequireSuperAdminAtAuthorizationBoundary()
    {
        var attributes = typeof(AdminController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>();
        Assert.Contains(attributes, a => a.Roles == "SuperAdmin");
    }

    [Theory]
    [InlineData("Admin", true, false)]
    [InlineData("User", true, false)]
    [InlineData("SuperAdmin", false, false)]
    [InlineData("SuperAdmin", true, true)]
    public async Task Filter_RequiresActiveSuperAdminAssignment_EvenWithSuperAdminCookieClaim(string databaseRole, bool active, bool allowed)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Roles.Add(new Role { Id = 900, Name = databaseRole, IsActive = active });
        db.AppUsers.Add(new AppUser { Id = 900, Username = "qa", FullName = "QA", IsActive = true });
        db.UserRoles.Add(new UserRole { UserId = 900, RoleId = 900 }); await db.SaveChangesAsync();
        var http = new DefaultHttpContext(); http.Request.Method = "GET";
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "900"), new Claim(ClaimTypes.Role, "SuperAdmin") }, "test"));
        var route = new RouteData(); route.Values["controller"] = "Admin"; route.Values["action"] = "MenuItems";
        var context = new ActionExecutingContext(new ActionContext(http, route, new ActionDescriptor(), new ModelStateDictionary()), [], new Dictionary<string, object>(), new object());
        var filter = new OperationFilter(db, new PermissionService(db), NullLogger<OperationFilter>.Instance, Options.Create(new BranchOptions()));
        var ran = false;
        await filter.OnActionExecutionAsync(context, () => { ran = true; return Task.FromResult(new ActionExecutedContext(context, [], context.Controller) { Result = new OkResult() }); });
        Assert.Equal(allowed, ran);
        if (!allowed) Assert.IsType<ForbidResult>(context.Result);
    }
}
