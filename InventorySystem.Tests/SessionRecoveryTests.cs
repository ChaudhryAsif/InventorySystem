using System.Security.Claims;
using InventorySystem.Authorization;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InventorySystem.Tests;

public class SessionRecoveryTests
{
    private sealed class NoTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private static AuthController Auth(ApplicationDbContext db, HttpContext http) => new(db, new PermissionService(db)) {
        ControllerContext = new ControllerContext { HttpContext = http }, TempData = new TempDataDictionary(http, new NoTempData()),
        Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(new ActionContext(http, new RouteData(), new ActionDescriptor())) };
    private sealed class AuthenticationStub : IAuthenticationService
    {
        public int SignOuts { get; private set; }
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) { SignOuts++; return Task.CompletedTask; }
    }

    private static ApplicationDbContext Db(bool active = false, bool privileged = false)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.AppUsers.Add(new AppUser { Id = 900, Username = "qa", FullName = "QA", IsActive = active, BranchIds = "1" });
        if (privileged) { db.Roles.Add(new Role { Id = 900, Name = "SuperAdmin" }); db.UserRoles.Add(new UserRole { UserId = 900, RoleId = 900 }); }
        db.SaveChanges(); return db;
    }

    private static (DefaultHttpContext http, AuthenticationStub auth) Http(string? id, string method = "GET", string accept = "text/html")
    {
        var auth = new AuthenticationStub();
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(auth).BuildServiceProvider() };
        var claims = id == null ? Array.Empty<Claim>() : new[] { new Claim(ClaimTypes.NameIdentifier, id) };
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestCookie"));
        http.Request.Method = method; http.Request.Headers.Accept = accept;
        http.Request.Path = "/Home/Index"; http.Request.QueryString = new QueryString("?from=qa");
        return (http, auth);
    }

    private static async Task<(ActionExecutingContext context, bool ran)> Run(ApplicationDbContext db, HttpContext http)
    {
        var route = new RouteData(); route.Values["controller"] = "Home"; route.Values["action"] = "Index";
        var context = new ActionExecutingContext(new ActionContext(http, route, new ActionDescriptor(), new ModelStateDictionary()), [], new Dictionary<string, object>(), new object());
        var filter = new OperationFilter(db, new PermissionService(db), NullLogger<OperationFilter>.Instance, Options.Create(new BranchOptions()));
        var ran = false;
        await filter.OnActionExecutionAsync(context, () => { ran = true; return Task.FromResult(new ActionExecutedContext(context, [], context.Controller) { Result = new OkResult() }); });
        return (context, ran);
    }

    [Theory]
    [InlineData(null)] [InlineData("invalid")] [InlineData("999")] [InlineData("900")]
    public async Task InvalidSession_Browser_ClearsCookieAndRedirectsToLogin(string? id)
    {
        using var db = Db(); var (http, auth) = Http(id); var (context, ran) = await Run(db, http);
        var redirect = Assert.IsType<RedirectToActionResult>(context.Result);
        Assert.Equal("Login", redirect.ActionName); Assert.Equal("Auth", redirect.ControllerName);
        Assert.Equal("/Home/Index?from=qa", redirect.RouteValues!["returnUrl"]);
        Assert.False(ran); Assert.Equal(1, auth.SignOuts); Assert.False(http.User.Identity!.IsAuthenticated);
    }

    [Theory]
    [InlineData("GET", "application/json")] [InlineData("GET", "*/*")] [InlineData("POST", "application/json")]
    [InlineData("POST", "text/html")] [InlineData("GET", "application/json,text/html")]
    public async Task InvalidSession_ApiOrPost_Remains401AndDoesNotExecute(string method, string accept)
    {
        using var db = Db(); var (http, auth) = Http("999", method, accept); var (context, ran) = await Run(db, http);
        Assert.IsType<UnauthorizedResult>(context.Result); Assert.False(ran); Assert.Equal(1, auth.SignOuts);
    }

    [Fact]
    public async Task InvalidSession_AjaxHtml_Remains401()
    {
        using var db = Db(); var (http, auth) = Http("999"); http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var (context, ran) = await Run(db, http); Assert.IsType<UnauthorizedResult>(context.Result); Assert.False(ran);
    }

    [Theory]
    [InlineData(null)] [InlineData("invalid")] [InlineData("999")] [InlineData("900")]
    public async Task Login_StaleCookie_ShowsFormInsteadOfRedirectLoop(string? id)
    {
        using var db = Db(); var (http, auth) = Http(id);
        var controller = Auth(db, http);
        Assert.IsType<ViewResult>(await controller.Login("/Purchase/Index"));
        Assert.Equal("/Purchase/Index", controller.ViewData["ReturnUrl"]);
        Assert.False(http.User.Identity!.IsAuthenticated); Assert.Equal(1, auth.SignOuts); Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Login_ValidCookie_KeepsDashboardRedirect()
    {
        using var db = Db(active: true); var (http, auth) = Http("900");
        var controller = Auth(db, http);
        var redirect = Assert.IsType<RedirectToActionResult>(await controller.Login());
        Assert.Equal("Home", redirect.ControllerName); Assert.Equal("Index", redirect.ActionName); Assert.Equal(0, auth.SignOuts);
    }

    [Fact]
    public async Task Login_AnonymousUser_ShowsForm()
    {
        using var db = Db(); var (http, auth) = Http(null); http.User = new ClaimsPrincipal(new ClaimsIdentity());
        var controller = Auth(db, http);
        Assert.IsType<ViewResult>(await controller.Login()); Assert.Equal(0, auth.SignOuts);
    }

    [Fact]
    public async Task ValidActiveUser_StillReachesAuthorizedPage()
    {
        using var db = Db(active: true, privileged: true); var (http, auth) = Http("900");
        var (context, ran) = await Run(db, http); Assert.True(ran); Assert.Null(context.Result); Assert.Equal(0, auth.SignOuts);
    }

    [Fact]
    public async Task ValidUserWithoutPermission_RemainsForbidden_NotLoggedOut()
    {
        using var db = Db(active: true); var (http, auth) = Http("900");
        var (context, ran) = await Run(db, http); Assert.False(ran); Assert.IsType<ForbidResult>(context.Result); Assert.Equal(0, auth.SignOuts);
    }
}
