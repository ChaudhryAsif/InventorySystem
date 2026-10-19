using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Tests;

public class PartyValidationTests
{
    [Fact]
    public async Task AllVisiblePartyFields_RoundTripThroughCreateAndUpdate()
    {
        using var db = Database();
        var controller = new PartiesController(db);
        var model = new PartyViewModel {
            PartyName = "QA Full Fields", PartyType = "both", Status = "active",
            ContactPerson = "Contact", Designation = "Manager", Phone = "03000000000",
            Mobile = "03000000001", Email = "qa@example.com", Website = "https://example.com",
            Address = "Test address", City = "Multan", State = "Punjab", ZipCode = "60000",
            Country = "Pakistan", OpeningBalance = 0, CreditLimit = 12500, PaymentTerms = 45,
            TaxId = "QA-NTN", Notes = "Test notes"
        };
        var created = Assert.IsType<OkObjectResult>(await controller.save(model));
        model.PartyId = Assert.IsType<Party>(created.Value).PartyId;
        foreach (var update in new[] { false, true }) {
            if (update) { model.Address = "Updated address"; model.City = "Lahore"; model.Notes = "Updated notes"; Assert.IsType<OkObjectResult>(await controller.save(model)); }
            var read = Assert.IsType<OkObjectResult>((await controller.GetById(model.PartyId)).Result);
            var actual = Assert.IsType<PartyViewModel>(read.Value);
            foreach (var property in typeof(PartyViewModel).GetProperties().Where(p => p.Name != nameof(PartyViewModel.PartyCode)))
                Assert.Equal(property.GetValue(model), property.GetValue(actual));
        }
    }
    private static ApplicationDbContext Database()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Parties.Add(new Party { PartyId = 900, PartyName = "Original", PartyType = "customer", Status = "active" });
        db.ItemCategory.Add(new ItemCategory { CategoryId = 1, CategoryName = "Test category", IsActive = true });
        db.Items.Add(new Items { ItemID = 900, CategoryID = 1, ItemName = "Test", IsActive = true, StockUnit = "pcs" });
        db.SaveChanges(); return db;
    }
    [Theory]
    [InlineData("", "customer", "active")]
    [InlineData("  ", "customer", "active")]
    [InlineData("Valid", "invalid", "active")]
    [InlineData("Valid", "customer", "invalid")]
    public async Task InvalidPartyUpdate_LeavesExistingRecordUnchanged(string name, string type, string status)
    {
        using var db = Database();
        var response = await new PartiesController(db).save(new PartyViewModel { PartyId = 900, PartyName = name, PartyType = type, Status = status });
        Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal("Original", (await db.Parties.FindAsync(900))!.PartyName);
    }
    [Fact]
    public async Task DuplicateRename_IsRejectedAfterTrimming()
    {
        using var db = Database();
        db.Parties.Add(new Party { PartyId = 901, PartyName = "Other", PartyType = "customer", Status = "active" }); await db.SaveChangesAsync();
        var response = await new PartiesController(db).save(new PartyViewModel { PartyId = 900, PartyName = " Other ", PartyType = "customer", Status = "active" });
        Assert.IsType<ConflictObjectResult>(response);
    }
    [Theory]
    [InlineData("customer", "active", true, false)]
    [InlineData("supplier", "active", false, true)]
    [InlineData("both", "active", true, true)]
    [InlineData("both", "inactive", false, false)]
    [InlineData("customer", "inactive", false, false)]
    [InlineData("supplier", "inactive", false, false)]
    public async Task Invoices_RequireActiveEligibleParty(string type, string status, bool saleAllowed, bool purchaseAllowed)
    {
        using var db = Database(); var party = (await db.Parties.FindAsync(900))!; party.PartyType = type; party.Status = status; await db.SaveChangesAsync();
        var sale = new SaleInvoiceViewModel { CustomerID = "900", BranchID = 1, PaymentMode = 0, SaleDate = DateTime.Today, Items = [new() { ItemId = 900, Quantity = 1, SalePrice = 10 }] };
        var purchase = new PurchaseInvoiceViewModel { VendorID = "900", BranchID = 1, PaymentMode = 0, PurchaseDate = DateTime.Today, Items = [new() { Itemid = 900, Quantity = 1, PurPrice = 10 }] };
        if (saleAllowed) await InvoiceValidation.Validate(db, sale);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceValidation.Validate(db, sale));
        if (purchaseAllowed) await InvoiceValidation.Validate(db, purchase);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceValidation.Validate(db, purchase));
    }
    [Fact]
    public async Task InactiveParty_CanStillReturnHistoricalInvoice()
    {
        using var db = Database(); (await db.Parties.FindAsync(900))!.Status = "inactive";
        db.SaleInvoice.Add(new SaleInvoice { SaleId = 900, CustomerID = "900", BranchID = 1, TotalAmount = 10, NetAmount = 10 });
        db.SaleInvoiceBody.Add(new SaleInvoiceBody { SaleId = 900, ItemId = 900, Quantity = 1, SalePrice = 10, Total = 10 }); await db.SaveChangesAsync();
        var model = new SaleReturnViewModel { OriginalSaleId = 900, CustomerID = "900", BranchID = 1, PaymentMode = 0, ReturnDate = DateTime.Today, Items = [new() { ItemId = 900, Quantity = 1, SalePrice = 10 }] };
        await InvoiceValidation.Validate(db, model); Assert.Equal(10m, model.NetAmount);
    }
}
