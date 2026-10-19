using System.Security.Cryptography;
using System.Text;
using InventorySystem.Authorization;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Tests;

public class InvoiceSafetyTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task OversizedInvoiceText_IsRejectedBeforeDatabaseWrites(bool reference)
    {
        using var db = Database();
        var text = new string('x', reference ? 21 : 501);
        var refNo = reference ? text : "QA";
        var remarks = reference ? "QA" : text;
        Func<Task>[] validations = [
            () => InvoiceValidation.Validate(db, new SaleInvoiceViewModel { RefNo = refNo, Remarks = remarks }),
            () => InvoiceValidation.Validate(db, new PurchaseInvoiceViewModel { BillNo = refNo, Remarks = remarks }),
            () => InvoiceValidation.Validate(db, new SaleReturnViewModel { RefNo = refNo, Remarks = remarks }),
            () => InvoiceValidation.Validate(db, new PurchaseReturnViewModel { RefNo = refNo, Remarks = remarks })
        ];
        foreach (var validate in validations) {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(validate);
            Assert.Contains(reference ? "20 characters" : "500 characters", error.Message);
        }
        Assert.Empty(db.SaleInvoice); Assert.Empty(db.PurchaseInvoice);
    }
    private static ApplicationDbContext Database()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        db.ItemCategory.Add(new ItemCategory { CategoryId = 1, CategoryName = "Test category", IsActive = true });
        db.Parties.Add(new Party { PartyId = 800, PartyName = "Test customer", PartyType = "customer", Status = "active" });
        db.Items.Add(new Items { ItemID = 800, CategoryID = 1, ItemName = "Test product", IsActive = true, SalePrice = 180 });
        db.SaveChanges(); return db;
    }
    private static SaleInvoiceViewModel Sale() => new()
    {
        CustomerID = "800", BranchID = 1, PaymentMode = 1, SaleDate = DateTime.Today,
        GSTPer = 5, Discount = 10, FreightExp = 5,
        Items = [new() { ItemId = 800, Quantity = 2, SalePrice = 180, DiscPer = 10, Total = 1 }],
        TotalAmount = 1, NetAmount = 1, GSTAmount = 999
    };
    [Fact] public async Task SaleTotals_AreCalculatedFromLines_NotBrowserTotals()
    {
        using var db = Database(); var sale = Sale();
        await InvoiceValidation.Validate(db, sale);
        Assert.Equal(36m, sale.Items[0].DiscAmt); Assert.Equal(324m, sale.TotalAmount);
        Assert.Equal(16.2m, sale.GSTAmount); Assert.Equal(335.2m, sale.NetAmount);
    }
    [Theory]
    [InlineData(-1, 180, 0)] [InlineData(0, 180, 0)] [InlineData(1, -1, 0)] [InlineData(1, 180, 101)]
    public void InvalidLines_AreRejected(decimal quantity, decimal price, decimal discount)
        => Assert.Throws<InvalidOperationException>(() => InvoiceValidation.Line(quantity, price, discount));
    [Fact] public async Task UnauthorizedBranch_IsRejected()
    {
        using var db = Database(); db.RestrictBranches = true; db.AllowedBranchIds = [2];
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceValidation.Validate(db, Sale()));
    }
    [Fact] public async Task InactiveItem_IsRejectedForNewSales()
    {
        using var db = Database(); (await db.Items.FindAsync(800L))!.IsActive = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceValidation.Validate(db, Sale()));
    }
    [Fact] public async Task ReturnCannotExceedOriginalQuantityOrValue()
    {
        using var db = Database();
        db.SaleInvoice.Add(new SaleInvoice { SaleId = 800, CustomerID = "800", BranchID = 1, TotalAmount = 180, NetAmount = 180 });
        db.SaleInvoiceBody.Add(new SaleInvoiceBody { SaleId = 800, ItemId = 800, Quantity = 1, SalePrice = 180, Total = 180 });
        await db.SaveChangesAsync();
        var value = new SaleReturnViewModel { OriginalSaleId = 800, CustomerID = "800", BranchID = 1, PaymentMode = 1, ReturnDate = DateTime.Today,
            Items = [new() { ItemId = 800, Quantity = 2, SalePrice = 180 }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceValidation.Validate(db, value));
        value.Items[0].Quantity = 1; value.Items[0].SalePrice = 1000;
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceValidation.Validate(db, value));
        value.Items[0].SalePrice = 180; await InvoiceValidation.Validate(db, value);
        Assert.Equal(180, value.NetAmount);
    }
    [Fact] public async Task CancellationFilter_HidesCancelledInvoices_WithoutDeletingHistory()
    {
        using var db = Database();
        db.SaleInvoice.Add(new SaleInvoice { SaleId = 800, IsCancelled = true, BranchID = 1 }); await db.SaveChangesAsync();
        Assert.False(await db.SaleInvoice.AnyAsync(s => s.SaleId == 800));
        Assert.True(await db.SaleInvoice.IgnoreQueryFilters().AnyAsync(s => s.SaleId == 800));
    }
    [Fact] public void WebhookSignature_RejectsModifiedBodyAndMalformedSignature()
    {
        const string body = "{\"message\":\"test\"}", secret = "test-secret";
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        Assert.True(WebhookSignature.IsValid(body, signature, secret));
        Assert.False(WebhookSignature.IsValid(body + " ", signature, secret));
        Assert.False(WebhookSignature.IsValid(body, "sha256=garbage", secret));
        Assert.False(WebhookSignature.IsValid(body, signature, ""));
    }
}
