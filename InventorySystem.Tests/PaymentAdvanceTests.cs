using System.Security.Claims;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Tests;

public class PaymentAdvanceTests
{
    private static ApplicationDbContext Db()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.Database.EnsureCreated();
        db.Parties.AddRange(
            new Party { PartyId = 900, PartyName = "Supplier owed", PartyType = "supplier", OpeningBalance = 100000, Status = "active" },
            new Party { PartyId = 901, PartyName = "Customer owing", PartyType = "customer", OpeningBalance = 50000, Status = "active" },
            new Party { PartyId = 902, PartyName = "Cleared supplier", PartyType = "supplier", OpeningBalance = 0, Status = "active" },
            new Party { PartyId = 903, PartyName = "Cleared customer", PartyType = "customer", OpeningBalance = 0, Status = "active" });
        db.SaveChanges();
        return db;
    }
    private static AccountsController Controller(ApplicationDbContext db) => new(db, new AccountService(db))
    { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "QA") }, "Test")) } } };
    private static PartyPaymentRequest Req(int party, decimal amount, string direction) => new() { PartyId = party, Amount = amount, Date = DateTime.Today, Direction = direction };
    private static async Task<decimal> Balance(ApplicationDbContext db, int party) => (await new AccountService(db).GetPartyStatementAsync(party, null, null)).Last().Balance;

    [Fact] public async Task PayingMoreThanPayable_IsAllowedAndKeptAsAdvance()
    {
        using var db = Db(); var c = Controller(db);
        Assert.IsType<OkObjectResult>(await c.SavePayment(Req(900, 150000, "Payment")));
        Assert.Equal(-50000m, await Balance(db, 900));
        var voucher = await db.Vouchers.Include(v => v.Details).ThenInclude(d => d.AccountHead).SingleAsync();
        Assert.Contains("advance of 50,000.00", voucher.Narration);
        Assert.Equal(150000m, voucher.Details.Single(d => d.AccountHead!.AccountCode == "2201").Debit);
        Assert.Equal(150000m, voucher.Details.Single(d => d.AccountHead!.AccountCode == "1101").Credit);
    }
    [Fact] public async Task ReceivingMoreThanDue_IsAllowedAndKeptAsAdvance()
    {
        using var db = Db(); var c = Controller(db);
        Assert.IsType<OkObjectResult>(await c.SavePayment(Req(901, 80000, "Receipt")));
        Assert.Equal(-30000m, await Balance(db, 901));
        Assert.Contains("advance of 30,000.00", (await db.Vouchers.SingleAsync()).Narration);
    }
    [Fact] public async Task ExactPayment_HasNoAdvanceNote()
    {
        using var db = Db(); var c = Controller(db);
        Assert.IsType<OkObjectResult>(await c.SavePayment(Req(900, 100000, "Payment")));
        Assert.Equal(0m, await Balance(db, 900));
        Assert.DoesNotContain("advance", (await db.Vouchers.SingleAsync()).Narration);
    }
    [Fact] public async Task ClearedParties_CanTakeAnAdvanceEitherWay()
    {
        using var db = Db(); var c = Controller(db);
        Assert.IsType<OkObjectResult>(await c.SavePayment(Req(902, 20000, "Payment")));
        Assert.IsType<OkObjectResult>(await c.SavePayment(Req(903, 7000, "Receipt")));
        Assert.Equal(-20000m, await Balance(db, 902));
        Assert.Equal(-7000m, await Balance(db, 903));
    }
    [Fact] public async Task WrongDirection_IsStillRefused()
    {
        using var db = Db(); var c = Controller(db);
        Assert.IsType<BadRequestObjectResult>(await c.SavePayment(Req(901, 1000, "Payment")));
        Assert.IsType<BadRequestObjectResult>(await c.SavePayment(Req(900, 1000, "Receipt")));
        Assert.Empty(await db.Vouchers.ToListAsync());
    }
}
