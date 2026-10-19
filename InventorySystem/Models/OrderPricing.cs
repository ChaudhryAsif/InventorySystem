using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Models;

public static class OrderPricing
{
    public static string? Validate(ProductionOrderViewModel model)
    {
        if (model.AgreedUnitPrice is null or <= 0 or > 100_000_000 ||
            model.AgreedUnitPrice != decimal.Round(model.AgreedUnitPrice.Value, 2) ||
            model.AgreedGstPercent < 0 || model.AgreedGstPercent > 100 ||
            model.AgreedGstPercent != decimal.Round(model.AgreedGstPercent, 2) || model.CustomerReference?.Trim().Length > 100)
            return "Enter the agreed selling price per box (before GST), with up to 2 decimals, GST from 0 to 100%, and a customer reference up to 100 characters.";
        return null;
    }

    public static decimal Total(int quantity, decimal price, decimal gst)
    {
        var net = InvoiceValidation.Money(quantity * price);
        return net + InvoiceValidation.Money(net * gst / 100);
    }

    public static async Task<decimal> InvoicedQuantity(ApplicationDbContext db, int orderId, int? excludingSale = null)
        => await (from body in db.SaleInvoiceBody join sale in db.SaleInvoice on body.SaleId equals sale.SaleId
            where sale.ProductionOrderId == orderId && (!excludingSale.HasValue || sale.SaleId != excludingSale.Value)
            select body.Quantity ?? 0).SumAsync();

    public static async Task ValidateSaleAsync(ApplicationDbContext db, SaleInvoiceViewModel model)
    {
        var originalOrder = model.SaleId > 0
            ? await db.SaleInvoice.Where(s => s.SaleId == model.SaleId).Select(s => s.ProductionOrderId).FirstOrDefaultAsync() : null;
        if (originalOrder.HasValue && originalOrder != model.ProductionOrderId)
            throw new InvalidOperationException("An order-linked invoice must keep its original production order. Cancel it and create a corrected invoice instead.");
        if (!model.ProductionOrderId.HasValue) return;
        var order = await db.ProductionOrder.SingleOrDefaultAsync(o => o.ProductionOrderId == model.ProductionOrderId);
        if (order == null || order.Status != "Completed" || !order.UsesEstimatedConsumption || order.ProducedQty <= 0 || order.AgreedUnitPrice is null or <= 0)
            throw new InvalidOperationException("Select a completed production order with a confirmed agreed price.");
        if (order.CustomerId != model.CustomerID || order.BranchId != model.BranchID)
            throw new InvalidOperationException("The invoice customer and branch must match the production order.");
        if (model.Items.Count != 1 || model.Items[0].ItemId != order.FinishedItemId || model.Items[0].SalePrice != order.AgreedUnitPrice ||
            model.Items[0].DiscPer != 0 || model.Discount != 0 || model.FreightExp != 0 || model.OtherExp != 0 || model.GSTPer != order.AgreedGstPercent)
            throw new InvalidOperationException("Keep the order's finished item, agreed price and GST. Discounts and extra charges are not allowed on an order-linked invoice.");
        var billed = await InvoicedQuantity(db, order.ProductionOrderId, model.SaleId);
        if (model.Items[0].Quantity > order.ProducedQty - billed)
            throw new InvalidOperationException("Invoice quantity exceeds the completed boxes remaining to invoice for this order. Refresh the order selection.");
    }
}
