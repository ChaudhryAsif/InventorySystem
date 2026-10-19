using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Tests;

public class ReportFilterTests
{
    private static readonly DateTime From = new(2026, 10, 1), To = new(2026, 10, 31);

    private static ApplicationDbContext Db()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.ItemCategory.AddRange(new ItemCategory { CategoryId = 1, CategoryName = "Materials", IsActive = true },
            new ItemCategory { CategoryId = 2, CategoryName = "Products", IsActive = true });
        db.Items.AddRange(
            new Items { ItemID = 1, CategoryID = 1, ItemName = "Paper", IsActive = true, ItemType = "RawMaterial", StockUnit = "kg", ReorderLevel = 50 },
            new Items { ItemID = 2, CategoryID = 1, ItemName = "Glue", IsActive = true, ItemType = "RawMaterial", StockUnit = "kg", ReorderLevel = 50 },
            new Items { ItemID = 3, CategoryID = 2, ItemName = "Boxes", IsActive = true, ItemType = "FinishedGoods", StockUnit = "pcs", ReorderLevel = 50, Barcode = "BX-100" });
        db.Stock.AddRange(new Stock { ItemId = 1, BranchId = 1, Quantity = 500, LastUpdated = new DateTime(2026, 10, 1) },
            new Stock { ItemId = 2, BranchId = 1, Quantity = 5, LastUpdated = new DateTime(2026, 10, 10) });
        db.Parties.AddRange(new Party { PartyId = 900, PartyName = "Alpha Supplies", PartyType = "supplier", Status = "active" },
            new Party { PartyId = 901, PartyName = "Zeta Traders", PartyType = "both", Status = "active" });
        db.PurchaseInvoice.AddRange(
            new PurchaseInvoice { PurchaseId = 1, PurchaseDate = new DateTime(2026, 10, 10), VendorID = "900", PaymentMode = 1, TotalAmount = 1000, AmountPaid = 1000, BillNo = "B-1", BranchID = 1 },
            new PurchaseInvoice { PurchaseId = 2, PurchaseDate = new DateTime(2026, 10, 12), VendorID = "900", PaymentMode = 0, TotalAmount = 5000, AmountPaid = 5000, BillNo = "B-22", BranchID = 1 },
            new PurchaseInvoice { PurchaseId = 3, PurchaseDate = new DateTime(2026, 10, 14), VendorID = "901", PaymentMode = 2, TotalAmount = 300, AmountPaid = 330, BillNo = "C-3", BranchID = 1 });
        db.PurchaseInvoiceBody.AddRange(new PurchaseInvoiceBody { PurchaseId = 1, Itemid = 1, Quantity = 5, PurPrice = 200 },
            new PurchaseInvoiceBody { PurchaseId = 2, Itemid = 3, Quantity = 50, PurPrice = 100 },
            new PurchaseInvoiceBody { PurchaseId = 3, Itemid = 2, Quantity = 3, PurPrice = 100 });
        db.SaleInvoice.AddRange(
            new SaleInvoice { SaleId = 1, SaleDate = new DateTime(2026, 10, 10), CustomerID = "901", PaymentMode = 1, TotalAmount = 1500, NetAmount = 1500, RefNo = "R-1", BranchID = 1 },
            new SaleInvoice { SaleId = 2, SaleDate = new DateTime(2026, 10, 12), CustomerID = "900", PaymentMode = 0, TotalAmount = 9000, NetAmount = 9000, RefNo = "R-22", BranchID = 1 },
            new SaleInvoice { SaleId = 3, SaleDate = new DateTime(2026, 10, 14), CustomerID = "901", PaymentMode = 1, TotalAmount = 200, NetAmount = 200, RefNo = "S-3", BranchID = 1 });
        db.SaleInvoiceBody.AddRange(new SaleInvoiceBody { SaleId = 1, ItemId = 3, Quantity = 10, SalePrice = 150 },
            new SaleInvoiceBody { SaleId = 2, ItemId = 3, Quantity = 60, SalePrice = 150 },
            new SaleInvoiceBody { SaleId = 3, ItemId = 1, Quantity = 1, SalePrice = 200 });
        db.SaveChanges();
        return db;
    }

    private static JsonElement Body(IActionResult result)
    {
        var json = JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(result).Value);
        Assert.True(json.GetProperty("success").GetBoolean());
        return json;
    }

    private static string[] Names(JsonElement body) => body.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("itemName").GetString()!).ToArray();
    private static int[] Invoices(JsonElement body) => body.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("invoiceNo").GetInt32()).ToArray();

    // ── Stock ────────────────────────────────────────────────────────────────
    [Fact] public async Task Stock_SearchMatchesNameOrBarcode()
    {
        using var db = Db(); var c = new ReportController(db);
        Assert.Equal(["Glue"], Names(Body(await c.GetStockData(search: "Gl"))));
        Assert.Equal(["Boxes"], Names(Body(await c.GetStockData(search: "BX-10"))));
    }

    [Fact] public async Task Stock_QuantityRangeUpdatedDatesAndNeverStocked()
    {
        using var db = Db(); var c = new ReportController(db);
        Assert.Equal(["Paper"], Names(Body(await c.GetStockData(minQty: 10))));
        Assert.Equal(["Boxes", "Glue"], Names(Body(await c.GetStockData(maxQty: 10))));
        Assert.Equal(["Glue"], Names(Body(await c.GetStockData(updatedFrom: new DateTime(2026, 10, 5)))));
        Assert.Equal(["Paper"], Names(Body(await c.GetStockData(updatedTo: new DateTime(2026, 10, 5)))));
        Assert.Equal(["Boxes"], Names(Body(await c.GetStockData(status: "neverstocked"))));
    }

    [Fact] public async Task Stock_SortsByQuantityAndDirection()
    {
        using var db = Db(); var c = new ReportController(db);
        Assert.Equal(["Paper", "Glue", "Boxes"], Names(Body(await c.GetStockData(sortBy: "quantity", sortDir: "desc"))));
        Assert.Equal(["Boxes", "Glue", "Paper"], Names(Body(await c.GetStockData(sortBy: "quantity"))));
        Assert.Equal(["Paper", "Glue", "Boxes"], Names(Body(await c.GetStockData(sortBy: "category"))));
    }

    // ── Purchase ─────────────────────────────────────────────────────────────
    [Fact] public async Task Purchase_FiltersByPaymentBillAmountAndBalance()
    {
        using var db = Db(); var c = new ReportController(db);
        Assert.Equal([2], Invoices(Body(await c.GetPurchaseData(From, To, paymentMode: 0))));
        Assert.Equal([2], Invoices(Body(await c.GetPurchaseData(From, To, billNo: "B-2"))));
        Assert.Equal([2, 1], Invoices(Body(await c.GetPurchaseData(From, To, minAmount: 500))));
        Assert.Equal([3], Invoices(Body(await c.GetPurchaseData(From, To, maxAmount: 400))));
        var due = Body(await c.GetPurchaseData(From, To, balance: "due"));
        Assert.Equal([2], Invoices(due));
        Assert.Equal(5000, due.GetProperty("summary").GetProperty("balanceDue").GetDecimal());
        Assert.Equal([3, 1], Invoices(Body(await c.GetPurchaseData(From, To, balance: "paid"))));
    }

    [Fact] public async Task Purchase_UsesNetInvoiceValueAndPaymentModeForSettlementColumns()
    {
        using var db = Db();
        var credit = await db.PurchaseInvoice.FindAsync(2);
        var cash = await db.PurchaseInvoice.FindAsync(1);
        credit!.TotalAmount = 100; credit.AmountPaid = 120;
        cash!.TotalAmount = 200; cash.AmountPaid = 220;
        db.PurchaseInvoice.Remove((await db.PurchaseInvoice.FindAsync(3))!);
        await db.SaveChangesAsync();
        var c = new ReportController(db);
        var result = Body(await c.GetPurchaseData(From, To));
        var summary = result.GetProperty("summary");
        Assert.Equal(340, summary.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(220, summary.GetProperty("netAmount").GetDecimal());
        Assert.Equal(120, summary.GetProperty("balanceDue").GetDecimal());
        Assert.Equal([1], Invoices(Body(await c.GetPurchaseData(From, To, minAmount: 200))));
        Assert.Equal([1, 2], Invoices(Body(await c.GetPurchaseData(From, To, sortBy: "amount", sortDir: "desc"))));
    }

    [Fact] public async Task Purchase_FiltersByItemAndCategoryContained()
    {
        using var db = Db(); var c = new ReportController(db);
        Assert.Equal([2], Invoices(Body(await c.GetPurchaseData(From, To, itemId: 3))));
        Assert.Equal([3, 1], Invoices(Body(await c.GetPurchaseData(From, To, categoryId: 1))));
        Assert.Empty(Invoices(Body(await c.GetPurchaseData(From, To, itemId: 1, categoryId: 2))));
    }

    [Fact] public async Task Purchase_SortsAndPages()
    {
        using var db = Db(); var c = new ReportController(db);
        Assert.Equal([2, 1, 3], Invoices(Body(await c.GetPurchaseData(From, To, sortBy: "amount", sortDir: "desc"))));
        Assert.Equal([1, 2, 3], Invoices(Body(await c.GetPurchaseData(From, To, sortDir: "asc"))));
        Assert.Equal([3, 2, 1], Invoices(Body(await c.GetPurchaseData(From, To))));
        var page = Body(await c.GetPurchaseData(From, To, page: 2, pageSize: 2));
        Assert.Equal([1], Invoices(page)); Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
    }

    // ── Sale ─────────────────────────────────────────────────────────────────
    [Fact] public async Task Sale_FiltersByPaymentRefItemCategoryAndAmount()
    {
        using var db = Db(); var c = new ReportController(db);
        Assert.Equal([2], Invoices(Body(await c.GetSaleData(From, To, paymentMode: 0))));
        Assert.Equal([2], Invoices(Body(await c.GetSaleData(From, To, refNo: "R-2"))));
        Assert.Equal([2, 1], Invoices(Body(await c.GetSaleData(From, To, itemId: 3))));
        Assert.Equal([3], Invoices(Body(await c.GetSaleData(From, To, categoryId: 1))));
        Assert.Equal([2], Invoices(Body(await c.GetSaleData(From, To, minAmount: 5000))));
        Assert.Equal([3, 1], Invoices(Body(await c.GetSaleData(From, To, maxAmount: 2000))));
    }

    [Fact] public async Task Sale_SortsByCustomerAndAmount()
    {
        using var db = Db(); var c = new ReportController(db);
        Assert.Equal([2, 3, 1], Invoices(Body(await c.GetSaleData(From, To, sortBy: "customer")))); // ties keep date order
        Assert.Equal([2, 1, 3], Invoices(Body(await c.GetSaleData(From, To, sortBy: "amount", sortDir: "desc"))));
    }

    [Fact] public async Task ItemList_ReturnsActiveItemsByName()
    {
        using var db = Db(); var c = new ReportController(db);
        var list = JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(await c.GetItemList()).Value);
        Assert.Equal(["Boxes", "Glue", "Paper"], list.EnumerateArray().Select(x => x.GetProperty("text").GetString()!).ToArray());
    }
}
