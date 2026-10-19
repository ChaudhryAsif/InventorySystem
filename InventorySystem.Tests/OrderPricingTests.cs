using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Services.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace InventorySystem.Tests;

public class OrderPricingTests
{
    private static ApplicationDbContext Database()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.Database.EnsureCreated();
        db.ItemCategory.Add(new ItemCategory { CategoryId = 700, CategoryName = "Test", IsActive = true });
        db.Items.AddRange(new Items { ItemID = 700, CategoryID = 700, ItemName = "Boxes", ItemType = "FinishedGoods", StockUnit = "pcs", IsActive = true },
            new Items { ItemID = 701, CategoryID = 700, ItemName = "Paper", ItemType = "RawMaterial", StockUnit = "kg", IsActive = true });
        db.Parties.Add(new Party { PartyId = 700, PartyName = "Customer", PartyType = "customer", Status = "active" });
        db.CostSheet.Add(new CostSheet { CostSheetId = 700, ItemName = "Boxes", FinishedItemId = 700, CustomerId = "700", Status = "Final", BranchID = 1, FinalRateWithGST = 25,
            Plies = [new CostSheetPly { ItemId = 701, KGs = .5m, Rate = 10 }] });
        db.ProductionOrder.Add(new ProductionOrder { ProductionOrderId = 700, CostSheetId = 700, CustomerId = "700", FinishedItemId = 700,
            ProductName = "Boxes", BranchId = 1, Status = "Completed", UsesEstimatedConsumption = true, ProducedQty = 100, OrderQty = 100,
            EstimatedRate = 25, AgreedUnitPrice = 20, AgreedGstPercent = 5, CustomerReference = "PO-123", PostedUtc = DateTime.UtcNow });
        db.Stock.Add(new Stock { ItemId = 700, BranchId = 1, Quantity = 100 });
        db.SaveChanges(); return db;
    }
    private static SaleInvoiceViewModel Sale(decimal quantity = 30) => new() {
        ProductionOrderId = 700, CustomerID = "700", BranchID = 1, PaymentMode = 0, SaleDate = DateTime.Today, GSTPer = 5,
        Items = [new() { ItemId = 700, Quantity = quantity, SalePrice = 20 }] };
    private static SaleController Sales(ApplicationDbContext db) => new(db, new LedgerPostingService(db, new AccountService(db)),
        Options.Create(new AccountingOptions()), new StockService(db));
    private static ProductionOrderViewModel Order() => new() { CostSheetId = 700, CustomerId = "700", OrderQty = 10, OrderDate = DateTime.Today,
        DeliveryDate = DateTime.Today, AgreedUnitPrice = 20, AgreedGstPercent = 5, CustomerReference = " PO-42 " };

    [Theory]
    [InlineData(null, 0)] [InlineData("-1", 0)] [InlineData("0", 0)] [InlineData("20.001", 0)]
    [InlineData("100000001", 0)] [InlineData("20", -1)] [InlineData("20", 101)] [InlineData("20", 5.001)]
    public void Agreement_RejectsMissingOrInvalidPricesAndTax(string? price, decimal gst)
    { var m = Order(); m.AgreedUnitPrice = price == null ? null : decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture); m.AgreedGstPercent = gst; Assert.NotNull(OrderPricing.Validate(m)); }

    [Fact]
    public async Task CreateAndPendingEdit_PreserveAgreedPriceSeparatelyFromEstimate()
    {
        using var db = Database(); var controller = new ProductionController(db, new StockService(db)); var model = Order();
        Assert.IsType<OkObjectResult>(await controller.Save(model));
        var order = await db.ProductionOrder.SingleAsync(o => o.ProductionOrderId != 700);
        Assert.Equal(20, order.AgreedUnitPrice); Assert.Equal(25, order.EstimatedRate); Assert.Equal("PO-42", order.CustomerReference);
        Assert.Equal(210, OrderPricing.Total(order.OrderQty, order.AgreedUnitPrice!.Value, order.AgreedGstPercent));
        model.ProductionOrderId = order.ProductionOrderId; model.AgreedUnitPrice = 19; model.OrderQty = 20;
        Assert.IsType<OkObjectResult>(await controller.Update(model)); Assert.Equal(19, order.AgreedUnitPrice);
        Assert.Equal(25, order.EstimatedRate); Assert.Equal(399, OrderPricing.Total(order.OrderQty, order.AgreedUnitPrice!.Value, order.AgreedGstPercent));
    }

    [Fact]
    public async Task MissingAgreement_IsRejectedBeforeOrderOrRecipeIsCreated()
    {
        using var db = Database(); var m = Order(); m.AgreedUnitPrice = null;
        Assert.IsType<BadRequestObjectResult>(await new ProductionController(db, new StockService(db)).Save(m));
        Assert.Single(db.ProductionOrder); Assert.Empty(db.ProductionMaterial);
    }

    [Theory]
    [InlineData("customer")] [InlineData("branch")] [InlineData("item")] [InlineData("price")] [InlineData("gst")]
    [InlineData("lineDiscount")] [InlineData("discount")] [InlineData("freight")] [InlineData("other")] [InlineData("extraLine")]
    [InlineData("quantity")] [InlineData("pending")] [InlineData("cancelled")] [InlineData("legacy")]
    public async Task LinkedSale_RejectsChangedAgreementOrUnavailableOrder(string change)
    {
        using var db = Database(); var m = Sale(); var order = (await db.ProductionOrder.FindAsync(700))!;
        switch (change) {
            case "customer": m.CustomerID = "999"; break; case "branch": m.BranchID = 2; break;
            case "item": m.Items[0].ItemId = 701; break; case "price": m.Items[0].SalePrice = 21; break;
            case "gst": m.GSTPer = 18; break; case "lineDiscount": m.Items[0].DiscPer = 1; break;
            case "discount": m.Discount = 1; break; case "freight": m.FreightExp = 1; break;
            case "other": m.OtherExp = 1; break; case "extraLine": m.Items.Add(new() { ItemId = 700, Quantity = 1, SalePrice = 20 }); break;
            case "quantity": m.Items[0].Quantity = 101; break; case "pending": order.Status = "Pending"; break;
            case "cancelled": order.Status = "Cancelled"; break; case "legacy": order.AgreedUnitPrice = null; break;
        }
        await db.SaveChangesAsync(); await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceValidation.Validate(db, m));
        Assert.Empty(db.SaleInvoice); Assert.Equal(100, (await db.Stock.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task PartialInvoices_EditAndCancellation_UseRemainingCompletedQuantityAndAgreedPrice()
    {
        using var db = Database(); var controller = Sales(db);
        Assert.IsType<OkObjectResult>(await controller.Save(Sale(30)));
        var first = await db.SaleInvoice.SingleAsync(); Assert.Equal(700, first.ProductionOrderId); Assert.Equal(630, first.NetAmount);
        Assert.Equal(30, await OrderPricing.InvoicedQuantity(db, 700));
        var edit = Sale(40); edit.SaleId = first.SaleId;
        Assert.IsType<OkObjectResult>(await controller.Update(edit)); Assert.Equal(40, await OrderPricing.InvoicedQuantity(db, 700));
        Assert.Equal(60, (await db.Stock.SingleAsync()).Quantity);
        var invalid = Assert.IsType<BadRequestObjectResult>(await controller.Save(Sale(61)));
        Assert.Contains("remaining", JsonSerializer.Serialize(invalid.Value));
        Assert.Single(db.SaleInvoice); Assert.Equal(60, (await db.Stock.SingleAsync()).Quantity);
        Assert.IsType<OkObjectResult>(await controller.Save(Sale(60)));
        Assert.Equal(100, await OrderPricing.InvoicedQuantity(db, 700));
        Assert.IsType<BadRequestObjectResult>(await controller.Save(Sale(1)));
        // Cancellation hides the invoice and releases its quantity, preserving history.
        controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
        controller.Request.Headers["X-Operation-Reason"] = "QA correction";
        Assert.IsType<OkObjectResult>(await controller.Delete(first.SaleId));
        Assert.Equal(60, await OrderPricing.InvoicedQuantity(db, 700));
        Assert.Equal(40, (await db.Stock.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task Lookup_ReturnsSavedAgreementAndExcludesExistingInvoiceDuringEdit()
    {
        using var db = Database(); var controller = Sales(db); Assert.IsType<OkObjectResult>(await controller.Save(Sale(30)));
        var result = Assert.IsType<JsonResult>(await controller.GetProductionOrders());
        var order = JsonSerializer.SerializeToElement(result.Value).GetProperty("orders")[0];
        Assert.Equal(20, order.GetProperty("AgreedUnitPrice").GetDecimal()); Assert.Equal(70, order.GetProperty("remainingQuantity").GetDecimal());
        var id = (await db.SaleInvoice.SingleAsync()).SaleId;
        result = Assert.IsType<JsonResult>(await controller.GetProductionOrders(id));
        order = JsonSerializer.SerializeToElement(result.Value).GetProperty("orders")[0];
        Assert.Equal(100, order.GetProperty("remainingQuantity").GetDecimal()); Assert.Equal(100, order.GetProperty("availableStock").GetDecimal());
    }

    [Fact]
    public async Task LinkedInvoice_CannotBeUnlinkedDuringEdit()
    {
        using var db = Database(); var controller = Sales(db); await controller.Save(Sale());
        var m = Sale(); m.SaleId = (await db.SaleInvoice.SingleAsync()).SaleId; m.ProductionOrderId = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceValidation.Validate(db, m));
    }

    [Fact]
    public async Task NewInvoice_CannotExcludeAnotherInvoicesQuantityUsingItsId()
    {
        using var db = Database(); var controller = Sales(db); await controller.Save(Sale(100));
        var m = Sale(1); m.SaleId = (await db.SaleInvoice.SingleAsync()).SaleId;
        Assert.IsType<BadRequestObjectResult>(await controller.Save(m)); Assert.Single(db.SaleInvoice);
    }

    [Fact]
    public async Task CostSheetChanges_DoNotChangeOrderAgreementOrLinkedInvoicePrice()
    {
        using var db = Database(); (await db.CostSheet.FindAsync(700))!.FinalRateWithGST = 99; await db.SaveChangesAsync();
        var m = Sale(); await InvoiceValidation.Validate(db, m); Assert.Equal(630, m.NetAmount);
        Assert.Equal(20, (await db.ProductionOrder.FindAsync(700))!.AgreedUnitPrice);
    }

    [Fact]
    public async Task Reversal_IsBlockedWhileOrderHasAnActiveLinkedSale()
    {
        using var db = Database(); await Sales(db).Save(Sale());
        // Even stock from another batch must not allow this order's linked sale to be orphaned.
        (await db.Stock.SingleAsync()).Quantity = 200; await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new EstimatedProductionService(db, new StockService(db)).ReverseAsync(700, "Test reversal"));
        Assert.Contains("linked", ex.Message); Assert.Equal("Completed", (await db.ProductionOrder.FindAsync(700))!.Status);
    }

    [Fact]
    public async Task UnlinkedNormalSale_StillAcceptsItsOwnPriceAndTax()
    {
        using var db = Database(); var m = Sale(); m.ProductionOrderId = null; m.Items[0].SalePrice = 22; m.GSTPer = 0;
        await InvoiceValidation.Validate(db, m); Assert.Equal(660, m.NetAmount);
    }

    [Fact]
    public void AgreementTotal_RoundsGstOnceAtInvoiceQuantity()
        => Assert.Equal(.05m, OrderPricing.Total(1, .03m, 50));
}
