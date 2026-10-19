using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventorySystem.Tests;

public class CategoryStatusTests
{
    private static ApplicationDbContext Database(bool categoryActive = true)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.ItemCategory.AddRange(new ItemCategory { CategoryId = 700, CategoryName = "Paper & Glue", IsActive = categoryActive },
            new ItemCategory { CategoryId = 701, CategoryName = "Other", IsActive = true });
        db.Items.AddRange(new Items { ItemID = 700, CategoryID = 700, ItemName = "Paper 'A' <test>", IsActive = true, StockUnit = "kg" },
            new Items { ItemID = 701, CategoryID = 700, ItemName = "Glue", IsActive = false, StockUnit = "kg" },
            new Items { ItemID = 702, CategoryID = 701, ItemName = "Other goods", IsActive = true, StockUnit = "pcs" });
        db.Parties.Add(new Party { PartyId = 700, PartyName = "Both", PartyType = "both", Status = "active" });
        db.Stock.Add(new Stock { ItemId = 700, BranchId = 1, Quantity = 12.5m });
        db.SaveChanges(); return db;
    }
    private static JsonElement Data(JsonResult response) => JsonSerializer.SerializeToElement(response.Value);
    private static ItemCategoryViewModel Change(bool active) => new() { CategoryId = 700, CategoryName = "Paper & Glue", IsActive = active };
    private static SaleInvoiceViewModel Sale() => new() { CustomerID = "700", BranchID = 1, PaymentMode = 0, SaleDate = DateTime.Today,
        Items = [new() { ItemId = 700, Quantity = 1, SalePrice = 10 }] };
    private static PurchaseInvoiceViewModel Purchase() => new() { VendorID = "700", BranchID = 1, PaymentMode = 0, PurchaseDate = DateTime.Today,
        Items = [new() { Itemid = 700, Quantity = 1, PurPrice = 10 }] };

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task StatusChange_RequiresPreview_AndChangesNothingUntilConfirmed(bool target)
    {
        using var db = Database(!target); var model = Change(target); model.CategoryName = "Renamed";
        var data = Data(await new CategoryController(db).Save(model));
        Assert.True(data.GetProperty("requiresConfirmation").GetBoolean());
        Assert.False(data.GetProperty("success").GetBoolean());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
        Assert.Equal("Paper 'A' <test>", data.GetProperty("items")[0].GetProperty("itemName").GetString());
        Assert.Equal("Paper & Glue", (await db.ItemCategory.FindAsync(700))!.CategoryName);
        Assert.Equal(!target, (await db.ItemCategory.FindAsync(700))!.IsActive);
        Assert.True((await db.Items.FindAsync(700L))!.IsActive);
        Assert.False((await db.Items.FindAsync(701L))!.IsActive);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task ConfirmedChange_UpdatesEveryLinkedItem_WithoutChangingOtherCategoryOrStock(bool target)
    {
        using var db = Database(!target); var controller = new CategoryController(db); var model = Change(target);
        var preview = Data(await controller.Save(model));
        model.StatusConfirmationToken = preview.GetProperty("confirmationToken").GetString();
        Assert.True(Data(await controller.Save(model)).GetProperty("success").GetBoolean());
        Assert.Equal(target, (await db.ItemCategory.FindAsync(700))!.IsActive);
        Assert.All(await db.Items.Where(i => i.CategoryID == 700).ToListAsync(), i => Assert.Equal(target, i.IsActive));
        Assert.True((await db.Items.FindAsync(702L))!.IsActive);
        Assert.Equal(12.5m, (await db.Stock.SingleAsync()).Quantity);
    }

    [Theory]
    [InlineData("add")] [InlineData("rename")] [InlineData("status")] [InlineData("move")]
    public async Task ChangedItems_RequireARefreshedConfirmation(string change)
    {
        using var db = Database(); var controller = new CategoryController(db); var model = Change(false);
        var preview = Data(await controller.Save(model)); model.StatusConfirmationToken = preview.GetProperty("confirmationToken").GetString();
        if (change == "add") db.Items.Add(new Items { ItemID = 703, CategoryID = 700, ItemName = "New", IsActive = true });
        if (change == "rename") (await db.Items.FindAsync(700L))!.ItemName = "Changed name";
        if (change == "status") (await db.Items.FindAsync(701L))!.IsActive = true;
        if (change == "move") (await db.Items.FindAsync(701L))!.CategoryID = 701;
        await db.SaveChangesAsync();
        var refreshed = Data(await controller.Save(model));
        Assert.True(refreshed.GetProperty("requiresConfirmation").GetBoolean());
        Assert.NotEqual(model.StatusConfirmationToken, refreshed.GetProperty("confirmationToken").GetString());
        Assert.True((await db.ItemCategory.FindAsync(700))!.IsActive);
        Assert.True((await db.Items.FindAsync(700L))!.IsActive);
    }

    [Fact]
    public async Task NameOnlyEdit_DoesNotReactivateIndividuallyDisabledItems()
    {
        using var db = Database(); var model = Change(true); model.CategoryName = "Renamed";
        Assert.True(Data(await new CategoryController(db).Save(model)).GetProperty("success").GetBoolean());
        Assert.False((await db.Items.FindAsync(701L))!.IsActive);
        Assert.Equal("Renamed", (await db.ItemCategory.FindAsync(700))!.CategoryName);
    }

    [Fact]
    public async Task EmptyCategory_StillRequiresStatusConfirmation()
    {
        using var db = Database(); var controller = new CategoryController(db);
        db.Items.RemoveRange(db.Items.Where(i => i.CategoryID == 700)); await db.SaveChangesAsync();
        var model = Change(false); var preview = Data(await controller.Save(model));
        Assert.Empty(preview.GetProperty("items").EnumerateArray());
        model.StatusConfirmationToken = preview.GetProperty("confirmationToken").GetString();
        Assert.True(Data(await controller.Save(model)).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task CategoryStatusChangedDuringReview_RejectsStaleConfirmation()
    {
        using var db = Database(); var controller = new CategoryController(db); var model = Change(false);
        model.StatusConfirmationToken = Data(await controller.Save(model)).GetProperty("confirmationToken").GetString();
        (await db.ItemCategory.FindAsync(700))!.IsActive = false; await db.SaveChangesAsync();
        var result = Data(await controller.Save(model)); Assert.False(result.GetProperty("success").GetBoolean());
        Assert.True((await db.Items.FindAsync(700L))!.IsActive);
    }

    [Fact]
    public async Task MissingCategoryId_IsNotSilentlyCreated()
    {
        using var db = Database(); var model = Change(false); model.CategoryId = 999;
        Assert.False(Data(await new CategoryController(db).Save(model)).GetProperty("success").GetBoolean());
        Assert.Equal(2, await db.ItemCategory.CountAsync());
    }

    [Theory]
    [InlineData(true, true, true)] [InlineData(false, true, false)]
    [InlineData(true, false, false)] [InlineData(false, false, false)]
    public async Task PurchaseAndSale_RequireBothCategoryAndItemActive(bool categoryActive, bool itemActive, bool allowed)
    {
        using var db = Database(categoryActive); (await db.Items.FindAsync(700L))!.IsActive = itemActive; await db.SaveChangesAsync();
        if (allowed) { await InvoiceValidation.Validate(db, Sale()); await InvoiceValidation.Validate(db, Purchase()); }
        else {
            await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceValidation.Validate(db, Sale()));
            await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceValidation.Validate(db, Purchase()));
        }
    }

    [Fact]
    public async Task MissingCategory_BlocksPurchasesAndSales()
    {
        using var db = Database(); db.ItemCategory.Remove((await db.ItemCategory.FindAsync(700))!); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceValidation.Validate(db, Sale()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceValidation.Validate(db, Purchase()));
    }

    [Fact]
    public async Task InactiveCategoryAndItem_AllowHistoricalSaleAndPurchaseReturns()
    {
        using var db = Database(false); (await db.Items.FindAsync(700L))!.IsActive = false;
        db.SaleInvoice.Add(new SaleInvoice { SaleId = 700, CustomerID = "700", BranchID = 1, TotalAmount = 10, NetAmount = 10 });
        db.SaleInvoiceBody.Add(new SaleInvoiceBody { SaleId = 700, ItemId = 700, Quantity = 1, SalePrice = 10, Total = 10 });
        db.PurchaseInvoice.Add(new PurchaseInvoice { PurchaseId = 700, VendorID = "700", BranchID = 1, TotalAmount = 10, AmountPaid = 10 });
        db.PurchaseInvoiceBody.Add(new PurchaseInvoiceBody { PurchaseId = 700, Itemid = 700, Quantity = 1, PurPrice = 10, DiscAmt = 0 });
        await db.SaveChangesAsync();
        var sale = new SaleReturnViewModel { OriginalSaleId = 700, CustomerID = "700", BranchID = 1, PaymentMode = 0, ReturnDate = DateTime.Today,
            Items = [new() { ItemId = 700, Quantity = 1, SalePrice = 10 }] };
        var purchase = new PurchaseReturnViewModel { OriginalPurchaseId = 700, VendorID = "700", BranchID = 1, PaymentMode = 0, ReturnDate = DateTime.Today,
            Items = [new() { ItemId = 700, Quantity = 1, PurPrice = 10 }] };
        await InvoiceValidation.Validate(db, sale); await InvoiceValidation.Validate(db, purchase);
        Assert.Equal(10, sale.NetAmount); Assert.Equal(10, purchase.NetAmount);
    }

    [Fact]
    public async Task Lookup_OmitsInactiveCategoriesAndItems_IncludingBarcodeSearch()
    {
        using var db = Database(false); var item = (await db.Items.FindAsync(700L))!; item.Barcode = "QA-CODE"; await db.SaveChangesAsync();
        var controller = new ItemController(db);
        var all = Assert.IsType<JsonResult>(await controller.GetItems());
        var data = JsonSerializer.SerializeToElement(all.Value);
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        var scanned = Assert.IsType<JsonResult>(await controller.GetItems(search: "QA-CODE"));
        Assert.Equal(0, JsonSerializer.SerializeToElement(scanned.Value).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task DeleteWithLinkedItems_IsRejected()
    {
        using var db = Database();
        var controller = new CategoryController(db) { ControllerContext = new ControllerContext {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role,"SuperAdmin")],"Test")) } } };
        var result = Data(await controller.Delete(700));
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("linked items",result.GetProperty("message").GetString());
        Assert.NotNull(await db.ItemCategory.FindAsync(700)); Assert.Equal(3, await db.Items.CountAsync());
    }
}
