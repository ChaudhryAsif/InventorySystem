using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventorySystem.Tests;

public class LedgerPostingServiceTests
{
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

    private static async Task<int> SeedPartyAsync(ApplicationDbContext db, string type)
    {
        db.Parties.Add(new Party { PartyName = "Test Party", PartyType = type, Status = "active" });
        await db.SaveChangesAsync();
        return db.Parties.Single().PartyId;
    }

    // ── SALE ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostSaleAsync_CreditSale_PostsBalancedEntry()
    {
        using var db = NewContext();
        var partyId = await SeedPartyAsync(db, "customer");
        var svc = new LedgerPostingService(db, new AccountService(db));

        var invoice = new SaleInvoice
        {
            CustomerID = partyId.ToString(),
            NetAmount = 1000m,
            PaymentMode = 0,   // credit
            SaleDate = DateTime.Today
        };

        var voucherId = await svc.PostSaleAsync(invoice);

        Assert.NotNull(voucherId);
        var gl = await db.GeneralLedger.Where(g => g.VoucherId == voucherId && !g.IsVoid).ToListAsync();
        Assert.Equal(2, gl.Count);
        Assert.Equal(gl.Sum(g => g.Debit), gl.Sum(g => g.Credit));

        var receivablesId = db.AccountHeads.Single(a => a.AccountCode == "1301").AccountHeadId;
        var salesId = db.AccountHeads.Single(a => a.AccountCode == "4101").AccountHeadId;
        Assert.Equal(1000m, gl.Single(g => g.AccountHeadId == receivablesId).Debit);
        Assert.Equal(1000m, gl.Single(g => g.AccountHeadId == salesId).Credit);
    }

    [Fact]
    public async Task PostSaleAsync_CashSale_PostsAgainstCash()
    {
        using var db = NewContext();
        var partyId = await SeedPartyAsync(db, "customer");
        var svc = new LedgerPostingService(db, new AccountService(db));

        var invoice = new SaleInvoice
        {
            CustomerID = partyId.ToString(),
            NetAmount = 500m,
            PaymentMode = 1,   // cash
            SaleDate = DateTime.Today
        };

        var voucherId = await svc.PostSaleAsync(invoice);

        Assert.NotNull(voucherId);
        var gl = await db.GeneralLedger.Where(g => g.VoucherId == voucherId && !g.IsVoid).ToListAsync();

        var cashId = db.AccountHeads.Single(a => a.AccountCode == "1101").AccountHeadId;
        var receivablesId = db.AccountHeads.Single(a => a.AccountCode == "1301").AccountHeadId;
        Assert.Equal(500m, gl.Single(g => g.AccountHeadId == cashId).Debit);
        Assert.DoesNotContain(gl, g => g.AccountHeadId == receivablesId);
    }

    // ── PURCHASE ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostPurchaseAsync_CreditPurchase_PostsBalancedEntry()
    {
        using var db = NewContext();
        var partyId = await SeedPartyAsync(db, "supplier");
        var svc = new LedgerPostingService(db, new AccountService(db));

        var invoice = new PurchaseInvoice
        {
            VendorID = partyId.ToString(),
            AmountPaid = 750m,
            PaymentMode = 0,   // credit
            PurchaseDate = DateTime.Today
        };

        var voucherId = await svc.PostPurchaseAsync(invoice);

        Assert.NotNull(voucherId);
        var gl = await db.GeneralLedger.Where(g => g.VoucherId == voucherId && !g.IsVoid).ToListAsync();
        Assert.Equal(2, gl.Count);
        Assert.Equal(gl.Sum(g => g.Debit), gl.Sum(g => g.Credit));

        var purchasesId = db.AccountHeads.Single(a => a.AccountCode == "5101").AccountHeadId;
        var payablesId = db.AccountHeads.Single(a => a.AccountCode == "2201").AccountHeadId;
        Assert.Equal(750m, gl.Single(g => g.AccountHeadId == purchasesId).Debit);
        Assert.Equal(750m, gl.Single(g => g.AccountHeadId == payablesId).Credit);
    }

    [Fact]
    public async Task PostPurchaseAsync_CashPurchase_PostsAgainstCash()
    {
        using var db = NewContext();
        var partyId = await SeedPartyAsync(db, "supplier");
        var svc = new LedgerPostingService(db, new AccountService(db));

        var invoice = new PurchaseInvoice
        {
            VendorID = partyId.ToString(),
            AmountPaid = 300m,
            PaymentMode = 1,   // cash
            PurchaseDate = DateTime.Today
        };

        var voucherId = await svc.PostPurchaseAsync(invoice);

        Assert.NotNull(voucherId);
        var gl = await db.GeneralLedger.Where(g => g.VoucherId == voucherId && !g.IsVoid).ToListAsync();

        var cashId = db.AccountHeads.Single(a => a.AccountCode == "1101").AccountHeadId;
        var payablesId = db.AccountHeads.Single(a => a.AccountCode == "2201").AccountHeadId;
        Assert.Equal(300m, gl.Single(g => g.AccountHeadId == cashId).Credit);
        Assert.DoesNotContain(gl, g => g.AccountHeadId == payablesId);
    }

    // ── VOID ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task VoidAsync_DelegatesToAccountService_VoidsGLEntries()
    {
        using var db = NewContext();
        var partyId = await SeedPartyAsync(db, "customer");
        var svc = new LedgerPostingService(db, new AccountService(db));

        var invoice = new SaleInvoice { CustomerID = partyId.ToString(), NetAmount = 1000m, PaymentMode = 0, SaleDate = DateTime.Today };
        var voucherId = await svc.PostSaleAsync(invoice);

        await svc.VoidAsync(voucherId);

        var voucher = await db.Vouchers.FindAsync(voucherId!.Value);
        Assert.True(voucher!.IsVoid);
        Assert.All(await db.GeneralLedger.Where(g => g.VoucherId == voucherId).ToListAsync(), g => Assert.True(g.IsVoid));
    }

    [Fact]
    public async Task VoidAsync_Null_IsSafeNoOp()
    {
        using var db = NewContext();
        var svc = new LedgerPostingService(db, new AccountService(db));

        var exception = await Record.ExceptionAsync(() => svc.VoidAsync(null));

        Assert.Null(exception);
    }

    // ── GUARD ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostSaleAsync_NonNumericCustomerId_ReturnsNull()
    {
        using var db = NewContext();
        var svc = new LedgerPostingService(db, new AccountService(db));

        var invoice = new SaleInvoice { CustomerID = "not-a-number", NetAmount = 1000m, PaymentMode = 0, SaleDate = DateTime.Today };

        var voucherId = await svc.PostSaleAsync(invoice);

        Assert.Null(voucherId);
    }

    [Fact]
    public async Task PostPurchaseAsync_NonNumericVendorId_ReturnsNull()
    {
        using var db = NewContext();
        var svc = new LedgerPostingService(db, new AccountService(db));

        var invoice = new PurchaseInvoice { VendorID = "not-a-number", AmountPaid = 1000m, PaymentMode = 0, PurchaseDate = DateTime.Today };

        var voucherId = await svc.PostPurchaseAsync(invoice);

        Assert.Null(voucherId);
    }
}
