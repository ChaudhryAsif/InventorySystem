using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Tests;

public class GatePassShareTests
{
    private static ApplicationDbContext Db()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.Database.EnsureCreated();
        return db;
    }
    private static HttpContext Http()
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "QA") }, "Test")) };
        http.Request.Scheme = "https"; http.Request.Host = new HostString("shop.test");
        return http;
    }
    private static GatePassController Staff(ApplicationDbContext db) => new(db) { ControllerContext = new() { HttpContext = Http() } };
    private static PublicReceiptController Customer(ApplicationDbContext db) => new(db) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
    private static async Task<GatePass> Pass(ApplicationDbContext db, string? phone = "0300-1234567", bool cancelled = false, int lines = 2, int branch = 1)
    {
        var pass = new GatePass { Direction = "Outward", PassDate = new DateTime(2026, 11, 21), BranchId = branch, PartyName = "Star Packaging", PartyPhone = phone, VehicleNumber = "KHI-1234", DriverName = "Imran", IsCancelled = cancelled, CreatedUtc = DateTime.UtcNow };
        for (var i = 1; i <= lines; i++) pass.Lines.Add(new GatePassLine { SerialNo = i, Description = "3-Ply Carton " + i, TotalQuantity = 100 * i, Unit = "pcs" });
        db.GatePasses.Add(pass); await db.SaveChangesAsync(); return pass;
    }
    private static JsonElement Data(IActionResult result) => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);

    [Fact] public async Task Share_MakesASecretLinkValidFor90Days_AndAWhatsAppLinkWithTheMessage()
    {
        using var db = Db(); var pass = await Pass(db);
        var data = Data(await Staff(db).CreateShareLink(new() { Id = pass.Id }));
        var saved = await db.GatePasses.SingleAsync();
        Assert.True(GatePassShare.LooksLikeToken(saved.ShareToken)); Assert.True(saved.ShareToken!.Length >= 40);
        Assert.InRange((saved.ShareExpiresUtc!.Value - DateTime.UtcNow).TotalDays, 89.9, 90.1);
        Assert.Equal($"https://shop.test/r/{saved.ShareToken}", data.GetProperty("url").GetString());
        var whatsapp = data.GetProperty("whatsappUrl").GetString()!;
        Assert.StartsWith("https://wa.me/923001234567?text=", whatsapp);
        var message = Uri.UnescapeDataString(whatsapp.Split("?text=")[1]);
        Assert.Contains(pass.PassNumber, message); Assert.Contains("Star Packaging", message); Assert.Contains("KHI-1234", message); Assert.Contains("3-Ply Carton 2: 200 PCS", message);
        Assert.Contains($"https://shop.test/r/{saved.ShareToken}", message); Assert.DoesNotContain("90", message); Assert.DoesNotContain("valid", message);
    }
    [Fact] public async Task SharingAgain_KeepsTheSameLink_AndRestartsThe90Days()
    {
        using var db = Db(); var pass = await Pass(db); var c = Staff(db);
        await c.CreateShareLink(new() { Id = pass.Id }); var first = (await db.GatePasses.SingleAsync()).ShareToken;
        (await db.GatePasses.SingleAsync()).ShareExpiresUtc = DateTime.UtcNow.AddDays(10); await db.SaveChangesAsync();
        await c.CreateShareLink(new() { Id = pass.Id }); var again = await db.GatePasses.SingleAsync();
        Assert.Equal(first, again.ShareToken); Assert.InRange((again.ShareExpiresUtc!.Value - DateTime.UtcNow).TotalDays, 89.9, 90.1);
    }
    [Fact] public async Task AnExpiredLink_IsReplacedByANewOne()
    {
        using var db = Db(); var pass = await Pass(db); var c = Staff(db);
        await c.CreateShareLink(new() { Id = pass.Id }); var old = await db.GatePasses.SingleAsync(); var oldToken = old.ShareToken;
        old.ShareExpiresUtc = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        await c.CreateShareLink(new() { Id = pass.Id });
        Assert.NotEqual(oldToken, (await db.GatePasses.SingleAsync()).ShareToken);
    }
    [Fact] public async Task CancelledOrUnknownPasses_CannotBeShared()
    {
        using var db = Db(); var cancelled = await Pass(db, cancelled: true); var c = Staff(db);
        Assert.IsType<BadRequestObjectResult>(await c.CreateShareLink(new() { Id = cancelled.Id }));
        Assert.IsType<NotFoundObjectResult>(await c.CreateShareLink(new() { Id = 999 }));
        Assert.IsType<BadRequestObjectResult>(await c.CreateShareLink(null)); Assert.IsType<BadRequestObjectResult>(await c.CreateShareLink(new() { Id = 0 }));
        Assert.Null((await db.GatePasses.SingleAsync()).ShareToken);
    }
    [Fact] public async Task WithoutAPartyPhone_WhatsAppAsksForTheContact()
    {
        using var db = Db(); var pass = await Pass(db, phone: null);
        var data = Data(await Staff(db).CreateShareLink(new() { Id = pass.Id }));
        Assert.StartsWith("https://wa.me/?text=", data.GetProperty("whatsappUrl").GetString());
    }
    [Fact] public async Task TheCustomerPage_ShowsThePass_ForAValidLink()
    {
        using var db = Db(); var pass = await Pass(db); await Staff(db).CreateShareLink(new() { Id = pass.Id });
        var token = (await db.GatePasses.SingleAsync()).ShareToken!;
        var view = Assert.IsType<ViewResult>(await Customer(db).Show(token));
        Assert.Null(view.ViewName); Assert.Equal(pass.Id, Assert.IsType<GatePass>(view.Model).Id); Assert.Equal(3, Assert.IsType<GatePass>(view.Model).Lines.Count + 1);
    }
    [Fact] public async Task TheCustomerPage_NeverShowsAnythingForABadOrExpiredLink()
    {
        using var db = Db(); var pass = await Pass(db); await Staff(db).CreateShareLink(new() { Id = pass.Id });
        var saved = await db.GatePasses.SingleAsync(); var token = saved.ShareToken!;
        foreach (var bad in new[] { "short", "not a token with spaces!!!!!!!!!!!!", new string('A', 43), "../../etc/passwd" })
        {
            var c = Customer(db); var view = Assert.IsType<ViewResult>(await c.Show(bad));
            Assert.Equal("Expired", view.ViewName); Assert.Null(view.Model); Assert.Equal(404, c.Response.StatusCode);
        }
        saved.ShareExpiresUtc = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        var expired = Customer(db); Assert.Equal("Expired", Assert.IsType<ViewResult>(await expired.Show(token)).ViewName); Assert.Equal(404, expired.Response.StatusCode);
    }
    [Fact] public async Task TheCustomerPage_IgnoresTheStaffBranchFilter_AndKeepsTheTokenPrivate()
    {
        using var db = Db(); var pass = await Pass(db, branch: 2); await Staff(db).CreateShareLink(new() { Id = pass.Id });
        db.RestrictBranches = true; db.AllowedBranchIds = [1];
        var c = Customer(db); var view = Assert.IsType<ViewResult>(await c.Show((await db.GatePasses.IgnoreQueryFilters().SingleAsync()).ShareToken!));
        Assert.Null(view.ViewName);
        Assert.Equal("no-referrer", c.Response.Headers["Referrer-Policy"]); Assert.Equal("no-store", c.Response.Headers["Cache-Control"]); Assert.Contains("noindex", c.Response.Headers["X-Robots-Tag"].ToString());
    }
    [Fact] public void OnlyTheCustomerPageIsOpenToEveryone()
    {
        Assert.NotNull(typeof(PublicReceiptController).GetCustomAttribute<AllowAnonymousAttribute>());
        var staff = typeof(GatePassController).GetCustomAttribute<AuthorizeAttribute>(); Assert.NotNull(staff); Assert.Contains("Admin", staff!.Roles);
        Assert.Null(typeof(GatePassController).GetMethod(nameof(GatePassController.CreateShareLink))!.GetCustomAttribute<AllowAnonymousAttribute>());
    }
    [Theory]
    [InlineData("0300-1234567", "923001234567")] [InlineData("+92 300 1234567", "923001234567")] [InlineData("0092 300 1234567", "923001234567")]
    [InlineData("3001234567", "923001234567")] [InlineData("(021) 3456 7890", "922134567890")] [InlineData("12345", null)] [InlineData("", null)] [InlineData(null, null)]
    public void PhoneNumbers_BecomeWhatsAppNumbers(string? raw, string? expected) => Assert.Equal(expected, GatePassShare.WhatsAppPhone(raw));
    [Fact] public async Task TheMessage_ListsEightItemsThenCountsTheRest()
    {
        using var db = Db(); var pass = await Pass(db, lines: 10);
        var message = GatePassShare.Message(pass, "https://shop.test/r/x");
        Assert.Contains("- 3-Ply Carton 8: 800 PCS", message); Assert.DoesNotContain("3-Ply Carton 9:", message); Assert.Contains("- and 2 more", message);
    }
    [Fact] public void Tokens_AreLongUniqueAndUrlSafe()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => GatePassShare.NewToken()).ToList();
        Assert.Equal(50, tokens.Distinct().Count()); Assert.All(tokens, t => { Assert.Equal(43, t.Length); Assert.True(GatePassShare.LooksLikeToken(t)); });
    }
}
