using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventorySystem.Tests;

public class AccountServiceTests
{
    // Seeded account head IDs (from ApplicationDbContext.SeedChartOfAccounts)
    private const int Cash = 100;          // 1101 Cash in Hand
    private const int Bank = 101;          // 1102 Bank Account
    private const int Receivables = 103;   // 1301 Trade Receivables
    private const int Machinery = 104;     // 1201 Machinery & Equipment (non-system)
    private const int Payables = 202;      // 2201 Trade Payables
    private const int Sales = 400;         // 4101 Sales
    private const int Rent = 511;          // 5202 Rent Expense (non-system)

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();   // applies HasData seed (chart of accounts etc.)
        return ctx;
    }

    private static Voucher BalancedVoucher(string type, decimal amount, int debitAccount, int creditAccount,
        DateTime? date = null, int? partyId = null) => new()
    {
        VoucherType = type,
        VoucherDate = date ?? DateTime.Today,
        Details = new List<VoucherDetail>
        {
            new() { AccountHeadId = debitAccount,  Debit  = amount, PartyId = partyId },
            new() { AccountHeadId = creditAccount, Credit = amount, PartyId = partyId }
        }
    };

    // ── CHART OF ACCOUNTS ────────────────────────────────────────────────────

    [Fact]
    public async Task ChartOfAccounts_SeedsDefaultTree()
    {
        using var db = NewContext();
        var svc = new AccountService(db);

        var accounts = await svc.GetChartOfAccountsAsync();

        Assert.Equal(38, accounts.Count);
        Assert.Equal(5, accounts.Count(a => a.Level == 1));
        Assert.Contains(accounts, a => a.AccountCode == "1101" && a.AccountName == "Cash in Hand");
        // Every non-root account points to an existing parent
        var ids = accounts.Select(a => a.AccountHeadId).ToHashSet();
        Assert.All(accounts.Where(a => a.ParentId != null), a => Assert.Contains(a.ParentId!.Value, ids));
    }

    [Fact]
    public async Task SaveAccountHead_NewAccount_Succeeds()
    {
        using var db = NewContext();
        var svc = new AccountService(db);

        var (success, message) = await svc.SaveAccountHeadAsync(new AccountHead
        {
            AccountCode = "1105",
            AccountName = "Petty Cash",
            AccountType = "Assets",
            ParentId = 10,
            Level = 3,
            NormalBalance = "Debit"
        });

        Assert.True(success, message);
        Assert.NotNull(await db.AccountHeads.FirstOrDefaultAsync(a => a.AccountCode == "1105"));
    }

    [Fact]
    public async Task SaveAccountHead_DuplicateCode_Fails()
    {
        using var db = NewContext();
        var svc = new AccountService(db);

        var (success, message) = await svc.SaveAccountHeadAsync(new AccountHead
        {
            AccountCode = "1101",   // already seeded (Cash in Hand)
            AccountName = "Duplicate Cash",
            AccountType = "Assets"
        });

        Assert.False(success);
        Assert.Contains("already exists", message);
    }

    [Fact]
    public async Task SaveAccountHead_Update_ChangesValues()
    {
        using var db = NewContext();
        var svc = new AccountService(db);

        var (success, message) = await svc.SaveAccountHeadAsync(new AccountHead
        {
            AccountHeadId = Machinery,
            AccountCode = "1201",
            AccountName = "Plant & Machinery",
            AccountType = "Assets",
            ParentId = 11,
            Level = 3,
            NormalBalance = "Debit",
            IsActive = true
        });

        Assert.True(success, message);
        Assert.Equal("Plant & Machinery", (await db.AccountHeads.FindAsync(Machinery))!.AccountName);
    }

    [Fact]
    public async Task SaveAccountHead_SystemAccountCodeChange_Fails()
    {
        using var db = NewContext();
        var svc = new AccountService(db);

        var (success, message) = await svc.SaveAccountHeadAsync(new AccountHead
        {
            AccountHeadId = Cash,
            AccountCode = "9999",   // attempt to change a system account's code
            AccountName = "Cash in Hand",
            AccountType = "Assets"
        });

        Assert.False(success);
        Assert.Contains("system account", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAccountHead_SystemAccount_Fails()
    {
        using var db = NewContext();
        var svc = new AccountService(db);

        var (success, message) = await svc.DeleteAccountHeadAsync(Cash);

        Assert.False(success);
        Assert.Contains("System accounts", message);
    }

    [Fact]
    public async Task DeleteAccountHead_WithPostedEntries_Fails()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        await svc.SaveVoucherAsync(BalancedVoucher("PV", 100m, Rent, Cash));

        var (success, message) = await svc.DeleteAccountHeadAsync(Rent);

        Assert.False(success);
        Assert.Contains("posted ledger entries", message);
    }

    [Fact]
    public async Task DeleteAccountHead_WithChildren_Fails()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        await svc.SaveAccountHeadAsync(new AccountHead { AccountCode = "1400", AccountName = "Custom Group", AccountType = "Assets", Level = 2, ParentId = 1 });
        var parent = await db.AccountHeads.FirstAsync(a => a.AccountCode == "1400");
        await svc.SaveAccountHeadAsync(new AccountHead { AccountCode = "1401", AccountName = "Custom Child", AccountType = "Assets", Level = 3, ParentId = parent.AccountHeadId });

        var (success, message) = await svc.DeleteAccountHeadAsync(parent.AccountHeadId);

        Assert.False(success);
        Assert.Contains("sub-accounts", message);
    }

    [Fact]
    public async Task DeleteAccountHead_CleanNonSystemAccount_Succeeds()
    {
        using var db = NewContext();
        var svc = new AccountService(db);

        var (success, message) = await svc.DeleteAccountHeadAsync(Machinery);

        Assert.True(success, message);
        Assert.Null(await db.AccountHeads.FindAsync(Machinery));
    }

    // ── VOUCHERS ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveVoucher_Unbalanced_Fails()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        var voucher = new Voucher
        {
            VoucherType = "JV",
            VoucherDate = DateTime.Today,
            Details = new List<VoucherDetail>
            {
                new() { AccountHeadId = Rent, Debit = 500m },
                new() { AccountHeadId = Cash, Credit = 300m }
            }
        };

        var (success, message, _) = await svc.SaveVoucherAsync(voucher);

        Assert.False(success);
        Assert.Contains("not balanced", message);
        Assert.Empty(db.GeneralLedger);
    }

    [Fact]
    public async Task SaveVoucher_NoLines_Fails()
    {
        using var db = NewContext();
        var svc = new AccountService(db);

        var (success, message, _) = await svc.SaveVoucherAsync(new Voucher { VoucherType = "JV", VoucherDate = DateTime.Today });

        Assert.False(success);
        Assert.Contains("at least one line", message);
    }

    [Fact]
    public async Task SaveVoucher_Balanced_PostsToGeneralLedger()
    {
        using var db = NewContext();
        var svc = new AccountService(db);

        var (success, message, voucherId) = await svc.SaveVoucherAsync(BalancedVoucher("PV", 500m, Rent, Cash));

        Assert.True(success, message);
        Assert.NotNull(voucherId);

        var voucher = await db.Vouchers.FindAsync(voucherId!.Value);
        Assert.Equal($"PV-{DateTime.Now.Year}-0001", voucher!.VoucherNo);
        Assert.Equal(500m, voucher.TotalAmount);

        var gl = await db.GeneralLedger.Where(g => g.VoucherId == voucherId && !g.IsVoid).ToListAsync();
        Assert.Equal(2, gl.Count);
        Assert.Equal(gl.Sum(g => g.Debit), gl.Sum(g => g.Credit));
        Assert.Equal(500m, gl.Single(g => g.AccountHeadId == Rent).Debit);
        Assert.Equal(500m, gl.Single(g => g.AccountHeadId == Cash).Credit);
    }

    [Fact]
    public async Task GenerateVoucherNo_IncrementsPerType()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        await svc.SaveVoucherAsync(BalancedVoucher("PV", 100m, Rent, Cash));
        await svc.SaveVoucherAsync(BalancedVoucher("PV", 200m, Rent, Cash));

        var nextPv = await svc.GenerateVoucherNoAsync("PV");
        var nextRv = await svc.GenerateVoucherNoAsync("RV");

        Assert.Equal($"PV-{DateTime.Now.Year}-0003", nextPv);
        Assert.Equal($"RV-{DateTime.Now.Year}-0001", nextRv);
    }

    [Fact]
    public async Task GenerateVoucherNo_DebitAndCreditNotes_GetDistinctPrefixes()
    {
        using var db = NewContext();
        var svc = new AccountService(db);

        var dn = await svc.SaveVoucherAsync(BalancedVoucher("DN", 100m, Payables, Receivables));
        var cn = await svc.SaveVoucherAsync(BalancedVoucher("CN", 200m, Receivables, Payables));

        Assert.True(dn.success, dn.message);
        Assert.True(cn.success, cn.message);
        Assert.Equal($"DN-{DateTime.Now.Year}-0001", (await db.Vouchers.FindAsync(dn.voucherId!.Value))!.VoucherNo);
        Assert.Equal($"CN-{DateTime.Now.Year}-0001", (await db.Vouchers.FindAsync(cn.voucherId!.Value))!.VoucherNo);
    }

    [Fact]
    public async Task GenerateVoucherNo_UsesVoucherDateYear_NotCurrentYear()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        var lastYear = new DateTime(DateTime.Today.Year - 1, 6, 15);

        var (_, _, backdatedId) = await svc.SaveVoucherAsync(BalancedVoucher("PV", 100m, Rent, Cash, lastYear));
        var (_, _, currentId) = await svc.SaveVoucherAsync(BalancedVoucher("PV", 200m, Rent, Cash));
        var (_, _, backdated2Id) = await svc.SaveVoucherAsync(BalancedVoucher("PV", 300m, Rent, Cash, lastYear));

        Assert.Equal($"PV-{lastYear.Year}-0001", (await db.Vouchers.FindAsync(backdatedId!.Value))!.VoucherNo);
        Assert.Equal($"PV-{DateTime.Now.Year}-0001", (await db.Vouchers.FindAsync(currentId!.Value))!.VoucherNo);
        Assert.Equal($"PV-{lastYear.Year}-0002", (await db.Vouchers.FindAsync(backdated2Id!.Value))!.VoucherNo);
    }

    [Fact]
    public async Task GenerateVoucherNo_UsesMaxSequence_NotCount()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        var (_, _, firstId) = await svc.SaveVoucherAsync(BalancedVoucher("PV", 100m, Rent, Cash));
        await svc.SaveVoucherAsync(BalancedVoucher("PV", 200m, Rent, Cash));

        // Hard-delete the first voucher: count-based numbering would now
        // produce 0002 again and collide with the surviving voucher.
        db.Vouchers.Remove((await db.Vouchers.FindAsync(firstId!.Value))!);
        await db.SaveChangesAsync();

        Assert.Equal($"PV-{DateTime.Now.Year}-0003", await svc.GenerateVoucherNoAsync("PV"));
    }

    [Fact]
    public async Task EditVoucher_ReplacesLines_KeepsNumber_NoOrphans()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        var (_, _, voucherId) = await svc.SaveVoucherAsync(BalancedVoucher("PV", 500m, Rent, Cash));
        var originalNo = (await db.Vouchers.FindAsync(voucherId!.Value))!.VoucherNo;

        // Client resubmits with three different lines and no VoucherNo
        var (success, message, _) = await svc.SaveVoucherAsync(new Voucher
        {
            VoucherId = voucherId.Value,
            VoucherType = "PV",
            VoucherDate = DateTime.Today,
            Details = new List<VoucherDetail>
            {
                new() { AccountHeadId = Rent, Debit = 300m },
                new() { AccountHeadId = Machinery, Debit = 400m },
                new() { AccountHeadId = Cash, Credit = 700m }
            }
        });

        Assert.True(success, message);
        var voucher = await db.Vouchers.Include(v => v.Details).SingleAsync(v => v.VoucherId == voucherId.Value);
        Assert.Equal(originalNo, voucher.VoucherNo);   // number preserved server-side
        Assert.Equal(700m, voucher.TotalAmount);
        // Old lines must be gone, not orphaned
        Assert.Equal(3, await db.VoucherDetails.CountAsync(d => d.VoucherId == voucherId.Value));
        // Old GL voided, new GL posted under the original number
        var gl = await db.GeneralLedger.Where(g => g.VoucherId == voucherId.Value).ToListAsync();
        Assert.Equal(2, gl.Count(g => g.IsVoid));
        Assert.Equal(3, gl.Count(g => !g.IsVoid));
        Assert.All(gl, g => Assert.Equal(originalNo, g.VoucherNo));
    }

    [Fact]
    public async Task EditVoucher_VoidVoucher_Fails()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        var (_, _, voucherId) = await svc.SaveVoucherAsync(BalancedVoucher("PV", 500m, Rent, Cash));
        await svc.VoidVoucherAsync(voucherId!.Value);

        var edit = BalancedVoucher("PV", 600m, Rent, Cash);
        edit.VoucherId = voucherId.Value;
        var (success, message, _) = await svc.SaveVoucherAsync(edit);

        Assert.False(success);
        Assert.Contains("void", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VoidVoucher_VoidsVoucherAndLedgerEntries()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        var (_, _, voucherId) = await svc.SaveVoucherAsync(BalancedVoucher("PV", 500m, Rent, Cash));

        var (success, message) = await svc.VoidVoucherAsync(voucherId!.Value);

        Assert.True(success, message);
        var voucher = await db.Vouchers.FindAsync(voucherId.Value);
        Assert.True(voucher!.IsVoid);
        Assert.Equal("Void", voucher.Status);
        Assert.All(await db.GeneralLedger.Where(g => g.VoucherId == voucherId).ToListAsync(), g => Assert.True(g.IsVoid));

        // Voiding twice must fail
        var second = await svc.VoidVoucherAsync(voucherId.Value);
        Assert.False(second.success);
    }

    [Fact]
    public async Task GetVouchers_ExcludesVoidAndFiltersByType()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        var (_, _, pvId) = await svc.SaveVoucherAsync(BalancedVoucher("PV", 100m, Rent, Cash));
        await svc.SaveVoucherAsync(BalancedVoucher("RV", 200m, Cash, Sales));
        await svc.VoidVoucherAsync(pvId!.Value);

        var all = await svc.GetVouchersAsync(null, null, null);
        var pvOnly = await svc.GetVouchersAsync("PV", null, null);

        Assert.Single(all);                 // void PV excluded
        Assert.Empty(pvOnly);
        Assert.Equal("RV", all[0].VoucherType);
    }

    // ── LEDGER ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AccountLedger_ReturnsPostedEntriesInOrder()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        await svc.SaveVoucherAsync(BalancedVoucher("RV", 1000m, Cash, Sales, DateTime.Today.AddDays(-2)));
        await svc.SaveVoucherAsync(BalancedVoucher("PV", 400m, Rent, Cash, DateTime.Today));

        var entries = await svc.GetAccountLedgerAsync(Cash, null, null);

        Assert.Equal(2, entries.Count);
        Assert.Equal(1000m, entries[0].Debit);   // older entry first
        Assert.Equal(400m, entries[1].Credit);
        Assert.Equal(600m, entries.Sum(e => e.Debit) - entries.Sum(e => e.Credit));
    }

    // ── REPORTS ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TrialBalance_IsBalancedAfterPostings()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        await svc.SaveVoucherAsync(BalancedVoucher("RV", 5000m, Cash, Sales));
        await svc.SaveVoucherAsync(BalancedVoucher("PV", 2000m, Rent, Cash));
        await svc.SaveVoucherAsync(BalancedVoucher("CV", 1500m, Bank, Cash));

        var tb = await svc.GetTrialBalanceAsync(DateTime.Today);

        Assert.True(tb.IsBalanced, $"Trial balance out of balance: Dr {tb.TotalDebit} vs Cr {tb.TotalCredit}");
        Assert.Equal(1500m, tb.Rows.Single(r => r.AccountCode == "1101").ClosingDebit);  // 5000 - 2000 - 1500
        Assert.Equal(5000m, tb.Rows.Single(r => r.AccountCode == "4101").ClosingCredit);
    }

    [Fact]
    public async Task TrialBalance_IncludesOpeningBalances()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        await svc.SaveOpeningBalancesAsync(new List<AccountHead>
        {
            new() { AccountHeadId = Cash, OpeningBalance = 1000m, OpeningBalanceType = "Debit" }
        });

        var tb = await svc.GetTrialBalanceAsync(DateTime.Today);

        var cashRow = tb.Rows.Single(r => r.AccountCode == "1101");
        Assert.Equal(1000m, cashRow.OpeningDebit);
        Assert.Equal(1000m, cashRow.ClosingDebit);
    }

    [Fact]
    public async Task ProfitLoss_ComputesNetProfit()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        await svc.SaveVoucherAsync(BalancedVoucher("RV", 5000m, Cash, Sales));   // income
        await svc.SaveVoucherAsync(BalancedVoucher("PV", 2000m, Rent, Cash));    // expense

        var pl = await svc.GetProfitLossAsync(DateTime.Today.AddDays(-1), DateTime.Today);

        Assert.Equal(5000m, pl.TotalIncome);
        Assert.Equal(2000m, pl.TotalExpense);
        Assert.Equal(3000m, pl.NetProfit);
        Assert.True(pl.IsProfit);
    }

    [Fact]
    public async Task BalanceSheet_AssetsEqualLiabilitiesPlusEquity()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        await svc.SaveVoucherAsync(BalancedVoucher("RV", 5000m, Cash, Sales));
        await svc.SaveVoucherAsync(BalancedVoucher("PV", 2000m, Rent, Cash));

        var bs = await svc.GetBalanceSheetAsync(DateTime.Today);

        Assert.Equal(3000m, bs.TotalAssets);           // cash 5000 - 2000
        Assert.Equal(3000m, bs.TotalEquity);           // net profit injected
        Assert.True(bs.IsBalanced, $"Assets {bs.TotalAssets} ≠ Liabilities {bs.TotalLiabilities} + Equity {bs.TotalEquity}");
    }

    [Fact]
    public async Task BalanceSheet_BalancesAcrossYears()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        // Profit earned last year must roll into equity this year
        var lastYear = new DateTime(DateTime.Today.Year - 1, 6, 1);
        await svc.SaveVoucherAsync(BalancedVoucher("RV", 5000m, Cash, Sales, lastYear));

        var bs = await svc.GetBalanceSheetAsync(DateTime.Today);

        Assert.Equal(5000m, bs.TotalAssets);
        Assert.Equal(5000m, bs.TotalEquity);
        Assert.True(bs.IsBalanced, $"Assets {bs.TotalAssets} ≠ Liabilities {bs.TotalLiabilities} + Equity {bs.TotalEquity}");
    }

    [Fact]
    public async Task CashBook_TracksOpeningAndClosingBalance()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        await svc.SaveOpeningBalancesAsync(new List<AccountHead>
        {
            new() { AccountHeadId = Cash, OpeningBalance = 1000m, OpeningBalanceType = "Debit" }
        });
        await svc.SaveVoucherAsync(BalancedVoucher("RV", 500m, Cash, Sales));

        var cb = await svc.GetCashBookAsync(DateTime.Today, DateTime.Today);

        Assert.Equal(1000m, cb.OpeningBalance);
        Assert.Equal(500m, cb.TotalReceipts);
        Assert.Equal(0m, cb.TotalPayments);
        Assert.Equal(1500m, cb.ClosingBalance);
    }

    [Fact]
    public async Task PartyStatement_CustomerRunningBalance()
    {
        using var db = NewContext();
        db.Parties.Add(new Party { PartyName = "Test Customer", PartyType = "customer", Status = "active" });
        await db.SaveChangesAsync();
        var partyId = db.Parties.Single().PartyId;

        var svc = new AccountService(db);
        // Party is tagged only on the receivable leg (matches Voucher.cshtml behavior:
        // user lines carry the party, the auto cash/bank leg does not).
        // Credit sale: customer owes 1000
        await svc.SaveVoucherAsync(new Voucher
        {
            VoucherType = "JV",
            VoucherDate = DateTime.Today.AddDays(-5),
            Details = new List<VoucherDetail>
            {
                new() { AccountHeadId = Receivables, Debit = 1000m, PartyId = partyId },
                new() { AccountHeadId = Sales, Credit = 1000m }
            }
        });
        // Customer pays 400
        await svc.SaveVoucherAsync(new Voucher
        {
            VoucherType = "RV",
            VoucherDate = DateTime.Today,
            Details = new List<VoucherDetail>
            {
                new() { AccountHeadId = Cash, Debit = 400m },
                new() { AccountHeadId = Receivables, Credit = 400m, PartyId = partyId }
            }
        });

        var statement = await svc.GetPartyStatementAsync(partyId, null, null);

        Assert.Equal(2, statement.Count);
        Assert.Equal(600m, statement.Last().Balance);
    }

    [Fact]
    public async Task OutstandingReport_ShowsCustomerReceivable()
    {
        using var db = NewContext();
        db.Parties.Add(new Party { PartyName = "Test Customer", PartyType = "customer", Status = "active" });
        await db.SaveChangesAsync();
        var partyId = db.Parties.Single().PartyId;

        var svc = new AccountService(db);
        var voucher = new Voucher
        {
            VoucherType = "JV",
            VoucherDate = DateTime.Today,
            Details = new List<VoucherDetail>
            {
                new() { AccountHeadId = Receivables, Debit = 1000m, PartyId = partyId },
                new() { AccountHeadId = Sales, Credit = 1000m }   // party only on receivable leg
            }
        };
        await svc.SaveVoucherAsync(voucher);

        var rows = await svc.GetOutstandingReportAsync("customer");

        var row = Assert.Single(rows);
        Assert.Equal(1000m, row.Balance);
        Assert.Equal("Receivable", row.BalanceType);
    }

    [Fact]
    public async Task AgingReport_BucketsByAge()
    {
        using var db = NewContext();
        db.Parties.Add(new Party { PartyName = "Aging Customer", PartyType = "customer", Status = "active" });
        await db.SaveChangesAsync();
        var partyId = db.Parties.Single().PartyId;

        var svc = new AccountService(db);
        async Task PostReceivable(decimal amount, int daysAgo) =>
            await svc.SaveVoucherAsync(new Voucher
            {
                VoucherType = "JV",
                VoucherDate = DateTime.Today.AddDays(-daysAgo),
                Details = new List<VoucherDetail>
                {
                    new() { AccountHeadId = Receivables, Debit = amount, PartyId = partyId },
                    new() { AccountHeadId = Sales, Credit = amount }
                }
            });

        await PostReceivable(100m, 10);    // current
        await PostReceivable(200m, 45);    // 31-60
        await PostReceivable(300m, 75);    // 61-90
        await PostReceivable(400m, 120);   // 90+

        var report = await svc.GetAgingReportAsync("customer", DateTime.Today);

        var row = Assert.Single(report.Rows);
        Assert.Equal(100m, row.Current);
        Assert.Equal(200m, row.Days31_60);
        Assert.Equal(300m, row.Days61_90);
        Assert.Equal(400m, row.Over90);
        Assert.Equal(1000m, row.Total);
    }

    [Fact]
    public async Task CashFlow_ClassifiesByVoucherType()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        await svc.SaveVoucherAsync(BalancedVoucher("RV", 5000m, Cash, Sales));   // operating inflow
        await svc.SaveVoucherAsync(BalancedVoucher("PV", 2000m, Rent, Cash));    // operating outflow

        var cf = await svc.GetCashFlowAsync(DateTime.Today.AddDays(-1), DateTime.Today);

        Assert.Equal(5000m, cf.TotalInflows);
        Assert.Equal(2000m, cf.TotalOutflows);
        Assert.Equal(3000m, cf.ClosingCash);
        Assert.Equal(3000m, cf.NetOperating);
    }

    [Fact]
    public async Task DayBook_TotalsDebitAndCredit()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        await svc.SaveVoucherAsync(BalancedVoucher("RV", 1000m, Cash, Sales));
        await svc.SaveVoucherAsync(BalancedVoucher("PV", 300m, Rent, Cash));

        var dayBook = await svc.GetDayBookAsync(DateTime.Today, DateTime.Today);

        Assert.Equal(4, dayBook.Rows.Count);
        Assert.Equal(1300m, dayBook.TotalDebit);
        Assert.Equal(1300m, dayBook.TotalCredit);
    }

    [Fact]
    public async Task BankRecon_MarkClearedUpdatesBalances()
    {
        using var db = NewContext();
        var svc = new AccountService(db);
        await svc.SaveVoucherAsync(BalancedVoucher("CV", 800m, Bank, Cash));

        var before = await svc.GetBankReconAsync(Bank, null, null);
        Assert.Equal(800m, before.BookBalance);
        Assert.Equal(0m, before.ClearedBalance);
        Assert.Equal(800m, before.UnclearedBalance);

        var glId = before.Rows.Single().GLId;
        var (success, _) = await svc.MarkClearedAsync(glId, true);
        Assert.True(success);

        var after = await svc.GetBankReconAsync(Bank, null, null);
        Assert.Equal(800m, after.ClearedBalance);
        Assert.Equal(0m, after.UnclearedBalance);
    }

    [Fact]
    public async Task SaveOpeningBalances_PersistsValues()
    {
        using var db = NewContext();
        var svc = new AccountService(db);

        var (success, message) = await svc.SaveOpeningBalancesAsync(new List<AccountHead>
        {
            new() { AccountHeadId = Cash, OpeningBalance = 2500m, OpeningBalanceType = "Debit" },
            new() { AccountHeadId = Payables, OpeningBalance = 1200m, OpeningBalanceType = "Credit" }
        });

        Assert.True(success, message);
        Assert.Equal(2500m, (await db.AccountHeads.FindAsync(Cash))!.OpeningBalance);
        Assert.Equal(1200m, (await db.AccountHeads.FindAsync(Payables))!.OpeningBalance);
        Assert.Equal("Credit", (await db.AccountHeads.FindAsync(Payables))!.OpeningBalanceType);
    }
}
