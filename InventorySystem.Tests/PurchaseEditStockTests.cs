using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Services.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InventorySystem.Tests;

public class PurchaseEditStockTests
{
    [Theory]
    [InlineData(100, 50)]
    [InlineData(110, 60)]
    [InlineData(60, 10)]
    [InlineData(49, -1)]
    public async Task ConsumedReceipt_EditUsesNetQuantity(decimal newQuantity, decimal expectedStock)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.Database.EnsureCreated();
        db.ItemCategory.Add(new ItemCategory { CategoryId = 1, CategoryName = "QA", GroupType="RawMaterial", IsActive = true });
        db.Items.Add(new Items { ItemID = 900, CategoryID = 1, ItemName = "Paper", IsActive = true, StockUnit = "kg" });
        db.Parties.Add(new Party { PartyId = 900, PartyCode="RAW-GROUP:1", PartyName = "QA Supplier", PartyType = "supplier", Status = "active" });
        db.PurchaseInvoice.Add(new PurchaseInvoice { PurchaseId = 900, VendorID = "900", BranchID = 1, PaymentMode = 0, PurchaseDate = DateTime.Today });
        db.PurchaseInvoiceBody.AddRange(
            new PurchaseInvoiceBody { PurchaseId = 900, Itemid = 900, Quantity = 50, PurPrice = 100 },
            new PurchaseInvoiceBody { PurchaseId = 900, Itemid = 900, Quantity = 50, PurPrice = 100 });
        db.Stock.Add(new Stock { ItemId = 900, BranchId = 1, Quantity = 50 });
        await db.SaveChangesAsync();
        var model = new PurchaseInvoiceViewModel { PurchaseId = 900, VendorID = "900", BranchID = 1,
            PurchaseDate = DateTime.Today, PaymentMode = 0, Remarks = "Updated remarks",
            Items = [new() { Itemid = 900, Quantity = newQuantity, PurPrice = 100 }] };
        var controller = new PurchaseController(db, new LedgerPostingService(db, new AccountService(db)),
            Options.Create(new AccountingOptions { PostToGeneralLedger = false }), new StockService(db));
        var result = await controller.Update(model);
        if (expectedStock < 0) {
            Assert.IsType<ObjectResult>(result);
            Assert.Equal(50, (await db.Stock.SingleAsync()).Quantity);
            Assert.Equal(2, await db.PurchaseInvoiceBody.CountAsync());
        } else {
            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(expectedStock, (await db.Stock.SingleAsync()).Quantity);
            Assert.Equal(newQuantity, (await db.PurchaseInvoiceBody.SingleAsync()).Quantity);
            Assert.Equal("Updated remarks", (await db.PurchaseInvoice.SingleAsync()).Remarks);
        }
    }
}
