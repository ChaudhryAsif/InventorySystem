using System.Security.Claims;
using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace InventorySystem.Tests;

public class ExtendedAccountingSafetyTests
{
    private static ApplicationDbContext Database()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.Database.EnsureCreated(); return db;
    }
    private static Voucher Voucher(string type = "JV", decimal amount = 1) => new() {
        VoucherType = type, VoucherDate = DateTime.Today, Status = "Draft",
        Details = [new() { AccountHeadId = 100, Debit = amount }, new() { AccountHeadId = 101, Credit = amount }] };
    [Theory]
    [InlineData("BAD", 1)] [InlineData("JV", 0)] [InlineData("JV", 0.001)]
    public async Task InvalidVoucher_IsRejectedWithoutSaving(string type, decimal amount)
    {
        using var db = Database(); var result = await new AccountService(db).SaveVoucherAsync(Voucher(type, amount));
        Assert.False(result.success); Assert.Empty(db.Vouchers);
    }
    [Fact] public async Task FutureVoucher_IsRejected()
    {
        using var db = Database(); var v = Voucher(); v.VoucherDate = DateTime.Today.AddYears(1);
        Assert.False((await new AccountService(db).SaveVoucherAsync(v)).success);
    }
    [Theory]
    [InlineData(-1, "Debit")] [InlineData(1, "invalid")] [InlineData(0.001, "Debit")]
    public async Task InvalidOpeningBalance_IsRejectedBeforeAnyMutation(decimal amount, string side)
    {
        using var db = Database(); var svc = new AccountService(db);
        var result = await svc.SaveOpeningBalancesAsync([new() { AccountHeadId = 100, OpeningBalance = 10 }, new() { AccountHeadId = 101, OpeningBalance = amount, OpeningBalanceType = side }]);
        Assert.False(result.success); Assert.Equal(0m, (await db.AccountHeads.FindAsync(100))!.OpeningBalance);
    }
    [Theory] [InlineData("Assets", 99)] [InlineData("invalid", 3)]
    public async Task InvalidAccountClassification_IsRejected(string type, int level)
    {
        using var db = Database();
        Assert.False((await new AccountService(db).SaveAccountHeadAsync(new() { AccountCode = "QA", AccountName = "QA", AccountType = type, Level = level })).success);
    }
    [Fact] public async Task VoucherDetailResponse_HasNoSerializationCycle()
    {
        using var db = Database(); var svc = new AccountService(db); var saved = await svc.SaveVoucherAsync(Voucher()); Assert.True(saved.success);
        var result = Assert.IsType<JsonResult>(await new AccountsController(db, svc).GetVoucher(saved.voucherId!.Value));
        var text = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(text); Assert.Equal(2, doc.RootElement.GetProperty("data").GetProperty("details").GetArrayLength());
        Assert.False(doc.RootElement.GetProperty("data").GetProperty("details")[0].TryGetProperty("voucher", out _));
    }
    [Fact] public async Task OpeningBalanceEndpoint_AcceptsFieldsSentByItsForm()
    {
        using var db = Database(); var svc = new AccountService(db);
        var request = JsonSerializer.Deserialize<List<OpeningBalanceRequest>>("[{\"accountHeadId\":100,\"openingBalance\":12.34,\"openingBalanceType\":\"Debit\"}]", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await new AccountsController(db, svc).SaveOpeningBalances(request);
        Assert.Equal(12.34m, (await db.AccountHeads.FindAsync(100))!.OpeningBalance);
    }
    [Fact] public async Task RemovingRoles_AlsoClearsPrimaryAndLegacyRoles()
    {
        using var db = Database(); db.Roles.Add(new Role { Id = 900, Name = "Admin", IsActive = true });
        db.AppUsers.Add(new AppUser { Id = 900, Username = "qa", FullName = "QA", PrimaryRoleId = 900, Role = "Admin", IsActive = true });
        db.UserRoles.Add(new UserRole { UserId = 900, RoleId = 900 }); await db.SaveChangesAsync();
        var controller = new AdminController(db, new PermissionService(db), Options.Create(new BranchOptions())) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "SuperAdmin")], "test")) } } };
        Assert.IsType<RedirectToActionResult>(await controller.EditUser(900, new AppUser { Username = "qa", FullName = "QA", IsActive = true }, []));
        var user = (await db.AppUsers.FindAsync(900))!; Assert.Null(user.PrimaryRoleId); Assert.Equal("", user.Role); Assert.Empty(db.UserRoles.Where(x => x.UserId == 900));
    }
}
