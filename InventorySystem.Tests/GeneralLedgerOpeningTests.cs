using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventorySystem.Tests;

public class GeneralLedgerOpeningTests
{
    private static ApplicationDbContext Db()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.Database.EnsureCreated(); return db;
    }
    private static GeneralLedger Entry(int account, DateTime date, decimal debit, decimal credit, bool isVoid = false) => new()
    { VoucherNo = "JV-1", VoucherType = "JV", VoucherDate = date, AccountHeadId = account, Debit = debit, Credit = credit, IsVoid = isVoid };
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public async Task RunningBalanceStartsFromOpeningBalancePlusEarlierEntries()
    {
        using var db = Db();
        var cash = await db.AccountHeads.FirstAsync(a => a.AccountCode == "1101");
        cash.OpeningBalance = 1000; cash.OpeningBalanceType = "Debit";
        var day = new DateTime(2026, 11, 10);
        db.GeneralLedger.AddRange(
            Entry(cash.AccountHeadId, day.AddDays(-20), 500, 0),
            Entry(cash.AccountHeadId, day.AddDays(-5), 0, 200),
            Entry(cash.AccountHeadId, day.AddDays(-4), 9999, 0, isVoid: true),
            Entry(cash.AccountHeadId, day, 300, 0));
        await db.SaveChangesAsync();

        var result = Json(Assert.IsType<JsonResult>(await new AccountsController(db, new AccountService(db)).GetGeneralLedger(cash.AccountHeadId, day, day)).Value);
        var summary = result.GetProperty("summary");
        Assert.Equal(1300m, summary.GetProperty("opening").GetDecimal());
        Assert.Equal(1600m, summary.GetProperty("closing").GetDecimal());
        Assert.Equal(1600m, result.GetProperty("data")[0].GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task CreditOpeningBalanceStartsNegative()
    {
        using var db = Db();
        var payable = await db.AccountHeads.FirstAsync(a => a.AccountCode == "2201");
        payable.OpeningBalance = 700; payable.OpeningBalanceType = "Credit";
        await db.SaveChangesAsync();
        var result = Json(Assert.IsType<JsonResult>(await new AccountsController(db, new AccountService(db)).GetGeneralLedger(payable.AccountHeadId, DateTime.Today, DateTime.Today)).Value);
        Assert.Equal(-700m, result.GetProperty("summary").GetProperty("opening").GetDecimal());
    }

    [Fact]
    public async Task OnlyAccountsWithEntriesOrOpeningBalanceAreListed()
    {
        using var db = Db();
        var cash = await db.AccountHeads.FirstAsync(a => a.AccountCode == "1101");
        var bank = await db.AccountHeads.FirstAsync(a => a.AccountCode == "1102");
        var sales = await db.AccountHeads.FirstAsync(a => a.AccountCode == "4101");
        var rent = await db.AccountHeads.FirstAsync(a => a.AccountCode == "5202");
        bank.OpeningBalance = 50; sales.IsActive = false;
        db.GeneralLedger.AddRange(Entry(cash.AccountHeadId, DateTime.Today, 10, 0), Entry(sales.AccountHeadId, DateTime.Today, 0, 10), Entry(rent.AccountHeadId, DateTime.Today, 5, 0, isVoid: true));
        await db.SaveChangesAsync();

        var result = Json(Assert.IsType<JsonResult>(await new AccountsController(db, new AccountService(db)).GetLedgerAccounts()).Value);
        var codes = result.GetProperty("data").EnumerateArray().Select(a => a.GetProperty("AccountCode").GetString()).ToList();
        Assert.Equal(["1101", "1102"], codes);
    }
}
