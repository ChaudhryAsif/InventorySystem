using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Models;

public static class InvoiceValidation
{
    private static void TextLimits(string? reference, string? remarks)
    {
        if (reference?.Length > 20) throw new InvalidOperationException("Reference or bill number must be 20 characters or fewer.");
        if (remarks?.Length > 500) throw new InvalidOperationException("Remarks must be 500 characters or fewer.");
    }
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static (decimal discount, decimal total) Line(decimal quantity, decimal price, decimal percent)
    {
        if (quantity <= 0 || quantity > 1_000_000 || price < 0 || price > 100_000_000 || percent < 0 || percent > 100)
            throw new InvalidOperationException("Invalid quantity, price, or discount percentage.");
        var gross = Money(quantity * price);
        var discount = Money(gross * percent / 100);
        return (discount, Money(gross - discount));
    }
    private static async Task Header(ApplicationDbContext db, string? party, int? branch, int? mode, DateTime? date, IEnumerable<long?> items, bool allowInactive = false, string? requiredPartyType = null)
    {
        if (!int.TryParse(party, out var id) || !await db.Parties.AnyAsync(p => p.PartyId == id)) throw new InvalidOperationException("Select a valid customer or supplier.");
        if (requiredPartyType != null && !await db.Parties.AnyAsync(p => p.PartyId == id && p.Status == "active" && (p.PartyType == requiredPartyType || p.PartyType == "both")))
            throw new InvalidOperationException($"Select an active {requiredPartyType} or a party marked as both.");
        if (!date.HasValue || date.Value.Year < 2000 || date.Value.Date > DateTime.Today.AddDays(1)) throw new InvalidOperationException("Invalid invoice date.");
        if (branch is null or <= 0 || db.RestrictBranches && !db.AllowedBranchIds.Contains(branch.Value)) throw new InvalidOperationException("Select an authorized branch.");
        if (mode is null or < 0 or > 3) throw new InvalidOperationException("Invalid payment mode.");
        var ids = items.ToArray();
        if (ids.Length == 0 || ids.Length > 1000 || ids.Any(i => i is null or <= 0)) throw new InvalidOperationException("Select valid items.");
        var distinct = ids.Select(i => i!.Value).Distinct().ToArray();
        if (await db.Items.CountAsync(i => distinct.Contains(i.ItemID) && (allowInactive || i.IsActive == true)) != distinct.Length) throw new InvalidOperationException("An item is invalid or inactive.");
        if (!allowInactive && await db.Items.AnyAsync(i => distinct.Contains(i.ItemID) &&
            !db.ItemCategory.Any(c => c.CategoryId == i.CategoryID && c.IsActive == true)))
            throw new InvalidOperationException("An item's category is inactive or missing. Activate the category and item before using it in a purchase or sale.");
    }
    private static (decimal tax, decimal net) Totals(decimal total, decimal taxPercent, decimal freight, decimal other, decimal discount)
    {
        if (taxPercent < 0 || taxPercent > 100 || freight < 0 || other < 0 || discount < 0 || discount > total)
            throw new InvalidOperationException("Invalid tax, expenses, or invoice discount.");
        var tax = Money(total * taxPercent / 100);
        return (tax, Money(total + tax + freight + other - discount));
    }
    private static async Task Quantity(ApplicationDbContext db, long? itemId, decimal quantity)
    {
        var unit = await db.Items.Where(i => i.ItemID == itemId).Select(i => i.StockUnit).SingleAsync();
        if (unit == "pcs" && quantity != decimal.Truncate(quantity) || quantity != decimal.Round(quantity, 3))
            throw new InvalidOperationException("Enter whole pieces, or kilograms to at most 3 decimal places.");
    }
    public static async Task Validate(ApplicationDbContext db, SaleInvoiceViewModel m)
    {
        TextLimits(m.RefNo, m.Remarks);
        await OrderPricing.ValidateSaleAsync(db, m);
        await Header(db, m.CustomerID, m.BranchID, m.PaymentMode, m.SaleDate, m.Items.Select(i => i.ItemId), requiredPartyType: "customer");
        foreach (var i in m.Items) { await Quantity(db, i.ItemId, i.Quantity); (i.DiscAmt, i.Total) = Line(i.Quantity, i.SalePrice, i.DiscPer); }
        m.TotalAmount = m.Items.Sum(i => i.Total);
        (m.GSTAmount, m.NetAmount) = Totals(m.TotalAmount, m.GSTPer, m.FreightExp, m.OtherExp, m.Discount);
    }
    public static async Task Validate(ApplicationDbContext db, PurchaseInvoiceViewModel m)
    {
        TextLimits(m.BillNo, m.Remarks);
        await Header(db, m.VendorID, m.BranchID, m.PaymentMode, m.PurchaseDate, m.Items.Select(i => i.Itemid), requiredPartyType: "supplier");
        foreach (var i in m.Items)
        {
            if (i.PackageCount is <= 0 || i.PackageCount.HasValue && i.PackageType is not ("Reel" or "Drum"))
                throw new InvalidOperationException("Enter a positive reel/drum count and its total net weight in kg.");
            if (i.PackageCount.HasValue && !await db.Items.AnyAsync(item => item.ItemID == i.Itemid && item.StockUnit == "kg"))
                throw new InvalidOperationException("Reel/drum purchases require an item stocked in kg.");
        }
        foreach (var i in m.Items) { await Quantity(db, i.Itemid, i.Quantity ?? 0); var t = Line(i.Quantity ?? 0, i.PurPrice ?? 0, i.DiscPer ?? 0); i.DiscAmt = t.discount; i.Total = t.total; }
        m.TotalAmount = m.Items.Sum(i => i.Total ?? 0);
        var totals = Totals(m.TotalAmount ?? 0, m.GSTPer ?? 0, m.FreightExp ?? 0, m.OtherExp ?? 0, m.Discount ?? 0);
        m.GSTAmount = totals.tax; m.NetAmount = totals.net;
    }
    public static async Task Validate(ApplicationDbContext db, SaleReturnViewModel m)
    {
        TextLimits(m.RefNo, m.Remarks);
        await Header(db, m.CustomerID, m.BranchID, m.PaymentMode, m.ReturnDate, m.Items.Select(i => i.ItemId), allowInactive: true);
        var invoice = await db.SaleInvoice.FirstOrDefaultAsync(s => s.SaleId == m.OriginalSaleId && s.CustomerID == m.CustomerID && (s.BranchID ?? 1) == m.BranchID);
        if (invoice == null) throw new InvalidOperationException("Select the original sale for this customer and branch.");
        foreach (var lines in m.Items.GroupBy(i => i.ItemId))
        {
            var sold = await db.SaleInvoiceBody.Where(b => b.SaleId == invoice.SaleId && b.ItemId == lines.Key).SumAsync(b => b.Quantity ?? 0);
            var returned = await (from b in db.SaleReturnBody join r in db.SaleReturn on b.SaleReturnId equals r.SaleReturnId where r.OriginalSaleId == invoice.SaleId && r.SaleReturnId != m.SaleReturnId && b.ItemId == lines.Key select b.Quantity ?? 0).SumAsync();
            if (lines.Sum(i => i.Quantity) > sold - returned) throw new InvalidOperationException("Return quantity exceeds the unreturned sale quantity.");
            var originalTotal = await db.SaleInvoiceBody.Where(b => b.SaleId == invoice.SaleId && b.ItemId == lines.Key).SumAsync(b => b.Total ?? 0);
            var requestedTotal = lines.Sum(i => Line(i.Quantity, i.SalePrice, i.DiscPer).total);
            if (sold <= 0 || requestedTotal > Money(originalTotal * lines.Sum(i => i.Quantity) / sold))
                throw new InvalidOperationException("Refund exceeds the original discounted line value.");
        }
        foreach (var i in m.Items) { await Quantity(db, i.ItemId, i.Quantity); (i.DiscAmt, i.Total) = Line(i.Quantity, i.SalePrice, i.DiscPer); }
        m.TotalAmount = m.Items.Sum(i => i.Total); m.NetAmount = Money(m.TotalAmount * (invoice.NetAmount ?? 0) / Math.Max(invoice.TotalAmount ?? 0, 0.01m));
        var refunded = await db.SaleReturn.Where(r => r.OriginalSaleId == invoice.SaleId && r.SaleReturnId != m.SaleReturnId).SumAsync(r => r.NetAmount ?? 0);
        m.NetAmount = Math.Min(m.NetAmount, Math.Max(0, (invoice.NetAmount ?? 0) - refunded));
    }
    public static async Task Validate(ApplicationDbContext db, PurchaseReturnViewModel m)
    {
        TextLimits(m.RefNo, m.Remarks);
        await Header(db, m.VendorID, m.BranchID, m.PaymentMode, m.ReturnDate, m.Items.Select(i => i.ItemId), allowInactive: true);
        var invoice = await db.PurchaseInvoice.FirstOrDefaultAsync(s => s.PurchaseId == m.OriginalPurchaseId && s.VendorID == m.VendorID && (s.BranchID ?? 1) == m.BranchID);
        if (invoice == null) throw new InvalidOperationException("Select the original purchase for this supplier and branch.");
        foreach (var lines in m.Items.GroupBy(i => i.ItemId))
        {
            var bought = await db.PurchaseInvoiceBody.Where(b => b.PurchaseId == invoice.PurchaseId && b.Itemid == lines.Key).SumAsync(b => b.Quantity ?? 0);
            var returned = await (from b in db.PurchaseReturnBody join r in db.PurchaseReturn on b.PurchaseReturnId equals r.PurchaseReturnId where r.OriginalPurchaseId == invoice.PurchaseId && r.PurchaseReturnId != m.PurchaseReturnId && b.ItemId == lines.Key select b.Quantity ?? 0).SumAsync();
            if (lines.Sum(i => i.Quantity) > bought - returned) throw new InvalidOperationException("Return quantity exceeds the unreturned purchase quantity.");
            var originalTotal = await db.PurchaseInvoiceBody.Where(b => b.PurchaseId == invoice.PurchaseId && b.Itemid == lines.Key).SumAsync(b => (b.Quantity ?? 0) * (b.PurPrice ?? 0) - (b.DiscAmt ?? 0));
            var requestedTotal = lines.Sum(i => Line(i.Quantity, i.PurPrice, i.DiscPer).total);
            if (bought <= 0 || requestedTotal > Money(originalTotal * lines.Sum(i => i.Quantity) / bought))
                throw new InvalidOperationException("Refund exceeds the original discounted line value.");
        }
        foreach (var i in m.Items) { await Quantity(db, i.ItemId, i.Quantity); (i.DiscAmt, i.Total) = Line(i.Quantity, i.PurPrice, i.DiscPer); }
        m.TotalAmount = m.Items.Sum(i => i.Total); m.NetAmount = Money(m.TotalAmount * (invoice.AmountPaid ?? 0) / Math.Max(invoice.TotalAmount ?? 0, 0.01m));
        var refunded = await db.PurchaseReturn.Where(r => r.OriginalPurchaseId == invoice.PurchaseId && r.PurchaseReturnId != m.PurchaseReturnId).SumAsync(r => r.NetAmount ?? 0);
        m.NetAmount = Math.Min(m.NetAmount, Math.Max(0, (invoice.AmountPaid ?? 0) - refunded));
    }
}
