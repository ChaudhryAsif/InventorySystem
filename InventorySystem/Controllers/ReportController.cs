using InventorySystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class ReportController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ── Stock Report ────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult StockReport() => View();

        // Same report, pre-filtered by item type.
        [HttpGet]
        public IActionResult RawMaterialStock() { ViewBag.ItemType = "RawMaterial"; return View("StockReport"); }

        [HttpGet]
        public IActionResult FinishedGoodsStock() { ViewBag.ItemType = "FinishedGoods"; return View("StockReport"); }

        [HttpGet]
        public async Task<IActionResult> GetStockData(int branchId = 0, int categoryId = 0, string? status = null, string? itemType = null,
            string? search = null, decimal? minQty = null, decimal? maxQty = null, DateTime? updatedFrom = null, DateTime? updatedTo = null,
            string? sortBy = null, string? sortDir = null)
        {
            try
            {
                var branches = branchId > 0 ? new[] { branchId } : _context.RestrictBranches
                    ? _context.AllowedBranchIds : (await _context.Stock.Select(s => s.BranchId).Distinct().ToListAsync()).Append(1).Distinct().ToArray();
                var rawData = new List<StockReportRow>();
                foreach (var branch in branches)
                {
                    var rows = await (from item in _context.Items
                                      join category in _context.ItemCategory on item.CategoryID equals category.CategoryId
                                      join stock in _context.Stock.Where(s => s.BranchId == branch) on item.ItemID equals stock.ItemId into stocks
                                      from stock in stocks.DefaultIfEmpty()
                                      where item.IsActive == true && (categoryId == 0 || item.CategoryID == categoryId)
                                          && (string.IsNullOrEmpty(itemType) || item.ItemType == itemType)
                                          && (string.IsNullOrWhiteSpace(search) || (item.ItemName ?? "").Contains(search.Trim()) || (item.Barcode ?? "").Contains(search.Trim()))
                                      select new StockReportRow
                                      {
                                          itemId = item.ItemID, itemName = item.ItemName ?? "", categoryName = category.CategoryName,
                                          itemType = item.ItemType,
                                          branchId = branch, quantity = stock == null ? 0 : stock.Quantity,
                                          reorderLevel = item.ReorderLevel, lastUpdated = stock == null ? null : stock.LastUpdated
                                      }).ToListAsync();
                    rawData.AddRange(rows);
                }

                if (minQty.HasValue) rawData = rawData.Where(x => x.quantity >= minQty.Value).ToList();
                if (maxQty.HasValue) rawData = rawData.Where(x => x.quantity <= maxQty.Value).ToList();
                if (updatedFrom.HasValue) rawData = rawData.Where(x => x.lastUpdated >= updatedFrom.Value.Date).ToList();
                if (updatedTo.HasValue) rawData = rawData.Where(x => x.lastUpdated <= updatedTo.Value.Date.AddDays(1).AddTicks(-1)).ToList();

                var filtered = status switch
                {
                    "instock"      => rawData.Where(x => x.quantity > x.reorderLevel).ToList(),
                    "lowstock"     => rawData.Where(x => x.quantity > 0 && x.quantity <= x.reorderLevel).ToList(),
                    "outofstock"   => rawData.Where(x => x.quantity <= 0).ToList(),
                    "neverstocked" => rawData.Where(x => x.lastUpdated == null).ToList(),
                    _              => rawData
                };

                var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
                filtered = sortBy switch
                {
                    "quantity" => OrderBy(filtered, x => x.quantity, desc),
                    "updated"  => OrderBy(filtered, x => x.lastUpdated ?? DateTime.MinValue, desc),
                    "category" => OrderBy(filtered, x => x.categoryName, desc),
                    _          => OrderBy(filtered, x => x.itemName, desc)
                };

                return Json(new
                {
                    success = true,
                    data = filtered.Select(x => new
                    {
                        x.itemId,
                        x.itemName,
                        x.categoryName,
                        x.itemType,
                        branchName  = GetBranchName(x.branchId),
                        x.quantity,
                        stockStatus = GetStockStatus(x.quantity, x.reorderLevel),
                        lastUpdated = x.lastUpdated?.ToString("yyyy-MM-dd HH:mm") ?? "Never stocked"
                    }),
                    summary = new
                    {
                        shown      = filtered.Count,
                        total      = rawData.Count,
                        inStock    = rawData.Count(x => x.quantity > x.reorderLevel),
                        lowStock   = rawData.Count(x => x.quantity > 0 && x.quantity <= x.reorderLevel),
                        outOfStock = rawData.Count(x => x.quantity <= 0)
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ── Purchase Report ─────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult PurchaseReport() => View();

        [HttpGet]
        public async Task<IActionResult> GetPurchaseData(
            DateTime? from, DateTime? to,
            string? vendorId = null,
            int? invoiceNo = null,
            int page = 1, int pageSize = 50,
            int? branchId = null, int? paymentMode = null, string? billNo = null,
            long? itemId = null, int? categoryId = null,
            decimal? minAmount = null, decimal? maxAmount = null,
            string? balance = null, string? sortBy = null, string? sortDir = null)
        {
            try
            {
                var fromDate = from ?? DateTime.Now.AddMonths(-1);
                var toDate   = (to ?? DateTime.Now).Date.AddDays(1).AddTicks(-1);
                if (page < 1) page = 1;
                pageSize = Math.Clamp(pageSize, 1, 500);

                var query = _context.PurchaseInvoice
                    .Where(p => p.PurchaseDate >= fromDate && p.PurchaseDate <= toDate);

                if (!string.IsNullOrEmpty(vendorId))
                    query = query.Where(p => p.VendorID == vendorId);

                if (invoiceNo.HasValue && invoiceNo.Value > 0)
                    query = query.Where(p => p.PurchaseId == invoiceNo.Value);

                if (branchId > 0) query = query.Where(p => (p.BranchID ?? 1) == branchId);
                if (paymentMode.HasValue) query = query.Where(p => p.PaymentMode == paymentMode);
                if (!string.IsNullOrWhiteSpace(billNo)) query = query.Where(p => p.BillNo != null && p.BillNo.Contains(billNo.Trim()));
                if (minAmount.HasValue) query = query.Where(p => (p.AmountPaid ?? p.TotalAmount ?? 0) >= minAmount.Value);
                if (maxAmount.HasValue) query = query.Where(p => (p.AmountPaid ?? p.TotalAmount ?? 0) <= maxAmount.Value);
                if (balance == "due") query = query.Where(p => (p.PaymentMode ?? 0) == 0 && (p.AmountPaid ?? p.TotalAmount ?? 0) > 0);
                if (balance == "paid") query = query.Where(p => (p.PaymentMode ?? 0) > 0);
                if (itemId > 0 || categoryId > 0)
                    query = query.Where(p => _context.PurchaseInvoiceBody.Any(b => b.PurchaseId == p.PurchaseId
                        && (!(itemId > 0) || b.Itemid == itemId)
                        && (!(categoryId > 0) || _context.Items.Any(i => i.ItemID == b.Itemid && i.CategoryID == categoryId))));

                var invoices = await query
                    .OrderByDescending(p => p.PurchaseDate)
                    .Select(p => new
                    {
                        p.PurchaseId,
                        p.PurchaseDate,
                        p.VendorID,
                        p.BillNo,
                        p.BranchID,
                        p.PaymentMode,
                        // AmountPaid stores the invoice net value, not a settlement amount.
                        TotalAmount = p.AmountPaid ?? p.TotalAmount ?? 0,
                        NetAmount = (p.PaymentMode ?? 0) > 0 ? (p.AmountPaid ?? p.TotalAmount ?? 0) : 0,
                        p.Remarks,
                        VendorName = _context.Parties
                            .Where(party => party.PartyId.ToString() == p.VendorID)
                            .Select(party => party.PartyName)
                            .FirstOrDefault() ?? p.VendorID
                    })
                    .ToListAsync();

                var descP = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
                var ascP = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
                invoices = sortBy switch
                {
                    "amount"  => OrderBy(invoices, x => x.TotalAmount, descP),
                    "party"   => OrderBy(invoices, x => x.VendorName ?? "", descP),
                    "invoice" => OrderBy(invoices, x => x.PurchaseId, descP),
                    _         => ascP ? OrderBy(invoices, x => x.PurchaseDate ?? DateTime.MinValue, false) : invoices
                };

                var totalCount = invoices.Count;
                var pagedInvoices = invoices.Skip((page - 1) * pageSize).Take(pageSize).ToList();

                var invoiceIds = pagedInvoices.Select(i => i.PurchaseId).ToList();
                var lineItems  = await _context.PurchaseInvoiceBody
                    .Where(b => invoiceIds.Contains(b.PurchaseId))
                    .Select(b => new
                    {
                        b.PurchaseId,
                        itemId    = b.Itemid,
                        descr     = b.Descr ?? "",
                        qty       = b.Quantity ?? 0,
                        purPrice  = b.PurPrice ?? 0,
                        salePrice = b.SalePrice ?? 0,
                        discPer   = b.DiscPer ?? 0,
                        discAmt   = b.DiscAmt ?? 0,
                        total     = (b.Quantity ?? 0) * (b.PurPrice ?? 0) - (b.DiscAmt ?? 0),
                        itemName  = _context.Items
                            .Where(i => i.ItemID == b.Itemid)
                            .Select(i => i.ItemName)
                            .FirstOrDefault() ?? b.Descr
                    })
                    .ToListAsync();

                var itemsByInvoice = lineItems
                    .GroupBy(b => b.PurchaseId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                return Json(new
                {
                    success = true,
                    totalCount,
                    page,
                    pageSize,
                    data = pagedInvoices.Select(p => new
                    {
                        invoiceNo    = p.PurchaseId,
                        purchaseDate = p.PurchaseDate.HasValue
                            ? p.PurchaseDate.Value.ToString("yyyy-MM-dd") : "-",
                        vendorName   = p.VendorName,
                        billNo       = p.BillNo ?? "-",
                        branchName   = GetBranchName(p.BranchID ?? 1),
                        paymentMode  = GetPaymentModeLabel(p.PaymentMode),
                        totalAmount  = p.TotalAmount,
                        netAmount    = p.NetAmount,
                        balanceDue   = Math.Max(0, (p.TotalAmount) - (p.NetAmount)),
                        remarks      = p.Remarks ?? "",
                        items        = itemsByInvoice.TryGetValue(p.PurchaseId, out var its)
                            ? its.Select(i => new
                            {
                                i.itemId,
                                itemName  = i.itemName ?? i.descr,
                                i.qty,
                                i.purPrice,
                                i.salePrice,
                                i.discPer,
                                i.discAmt,
                                i.total
                            })
                            : []
                    }),
                    summary = new
                    {
                        totalInvoices = invoices.Count,
                        totalAmount   = invoices.Sum(p => p.TotalAmount),
                        netAmount     = invoices.Sum(p => p.NetAmount),
                        balanceDue    = invoices.Sum(p => Math.Max(0, (p.TotalAmount) - (p.NetAmount)))
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ── Sale Report ─────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult SaleReport() => View();

        [HttpGet]
        public async Task<IActionResult> GetSaleData(
            DateTime? from, DateTime? to,
            string? customerId = null,
            int? invoiceNo = null,
            int page = 1, int pageSize = 50,
            int? branchId = null, int? paymentMode = null, string? refNo = null,
            long? itemId = null, int? categoryId = null,
            decimal? minAmount = null, decimal? maxAmount = null,
            string? sortBy = null, string? sortDir = null)
        {
            try
            {
                var fromDate = from ?? DateTime.Now.AddMonths(-1);
                var toDate   = (to ?? DateTime.Now).Date.AddDays(1).AddTicks(-1);
                if (page < 1) page = 1;
                pageSize = Math.Clamp(pageSize, 1, 500);

                var query = _context.SaleInvoice
                    .Where(s => s.SaleDate >= fromDate && s.SaleDate <= toDate);

                if (!string.IsNullOrEmpty(customerId))
                    query = query.Where(s => s.CustomerID == customerId);

                if (invoiceNo.HasValue && invoiceNo.Value > 0)
                    query = query.Where(s => s.SaleId == invoiceNo.Value);

                if (branchId > 0) query = query.Where(s => (s.BranchID ?? 1) == branchId);
                if (paymentMode.HasValue) query = query.Where(s => s.PaymentMode == paymentMode);
                if (!string.IsNullOrWhiteSpace(refNo)) query = query.Where(s => s.RefNo != null && s.RefNo.Contains(refNo.Trim()));
                if (minAmount.HasValue) query = query.Where(s => (s.TotalAmount ?? 0) >= minAmount.Value);
                if (maxAmount.HasValue) query = query.Where(s => (s.TotalAmount ?? 0) <= maxAmount.Value);
                if (itemId > 0 || categoryId > 0)
                    query = query.Where(s => _context.SaleInvoiceBody.Any(b => b.SaleId == s.SaleId
                        && (!(itemId > 0) || b.ItemId == itemId)
                        && (!(categoryId > 0) || _context.Items.Any(i => i.ItemID == b.ItemId && i.CategoryID == categoryId))));

                var invoices = await query
                    .OrderByDescending(s => s.SaleDate)
                    .Select(s => new
                    {
                        s.SaleId,
                        s.SaleDate,
                        s.CustomerID,
                        s.RefNo,
                        s.BranchID,
                        s.PaymentMode,
                        s.TotalAmount,
                        s.NetAmount,
                        s.Remarks,
                        CustomerName = _context.Parties
                            .Where(p => p.PartyId.ToString() == s.CustomerID)
                            .Select(p => p.PartyName)
                            .FirstOrDefault() ?? s.CustomerID
                    })
                    .ToListAsync();

                var descS = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
                var ascS = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
                invoices = sortBy switch
                {
                    "amount"   => OrderBy(invoices, x => x.TotalAmount ?? 0, descS),
                    "customer" => OrderBy(invoices, x => x.CustomerName ?? "", descS),
                    "invoice"  => OrderBy(invoices, x => x.SaleId, descS),
                    _          => ascS ? OrderBy(invoices, x => x.SaleDate ?? DateTime.MinValue, false) : invoices
                };

                var totalCount = invoices.Count;
                var pagedInvoices = invoices.Skip((page - 1) * pageSize).Take(pageSize).ToList();

                var invoiceIds = pagedInvoices.Select(i => i.SaleId).ToList();
                var lineItems  = await _context.SaleInvoiceBody
                    .Where(b => invoiceIds.Contains(b.SaleId))
                    .Select(b => new
                    {
                        b.SaleId,
                        itemId    = b.ItemId,
                        descr     = b.Descr ?? "",
                        qty       = b.Quantity ?? 0,
                        salePrice = b.SalePrice ?? 0,
                        discPer   = b.DiscPer ?? 0,
                        discAmt   = b.DiscAmt ?? 0,
                        total     = b.Total ?? 0,
                        itemName  = _context.Items
                            .Where(i => i.ItemID == b.ItemId)
                            .Select(i => i.ItemName)
                            .FirstOrDefault() ?? b.Descr
                    })
                    .ToListAsync();

                var itemsByInvoice = lineItems
                    .GroupBy(b => b.SaleId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                return Json(new
                {
                    success = true,
                    totalCount,
                    page,
                    pageSize,
                    data = pagedInvoices.Select(s => new
                    {
                        invoiceNo    = s.SaleId,
                        saleDate     = s.SaleDate.HasValue
                            ? s.SaleDate.Value.ToString("yyyy-MM-dd") : "-",
                        customerName = s.CustomerName,
                        refNo        = s.RefNo ?? "-",
                        branchName   = GetBranchName(s.BranchID ?? 1),
                        paymentMode  = GetPaymentModeLabel(s.PaymentMode),
                        totalAmount  = s.TotalAmount ?? 0,
                        netAmount    = s.NetAmount ?? 0,
                        remarks      = s.Remarks ?? "",
                        items        = itemsByInvoice.TryGetValue(s.SaleId, out var its)
                            ? its.Select(i => new
                            {
                                i.itemId,
                                itemName  = i.itemName ?? i.descr,
                                i.qty,
                                i.salePrice,
                                i.discPer,
                                i.discAmt,
                                i.total
                            })
                            : []
                    }),
                    summary = new
                    {
                        totalInvoices = invoices.Count,
                        totalAmount   = invoices.Sum(s => s.TotalAmount ?? 0),
                        netAmount     = invoices.Sum(s => s.NetAmount ?? 0)
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ── Dropdown helpers ────────────────────────────────────────────────────
        // Active items for the report item filter.
        [HttpGet]
        public async Task<IActionResult> GetItemList()
        {
            var items = await _context.Items.Where(i => i.IsActive == true)
                .OrderBy(i => i.ItemName)
                .Select(i => new { value = i.ItemID.ToString(), text = i.ItemName ?? "" })
                .ToListAsync();
            return Json(items);
        }

        private static List<T> OrderBy<T, K>(IEnumerable<T> source, Func<T, K> key, bool desc) =>
            (desc ? source.OrderByDescending(key) : source.OrderBy(key)).ToList();

        [HttpGet]
        public async Task<IActionResult> GetSupplierList()
        {
            var suppliers = await _context.Parties
                .Where(p => p.PartyType == "supplier" || p.PartyType == "both")
                .Select(p => new { value = p.PartyId.ToString(), text = p.PartyName })
                .ToListAsync();
            return Json(suppliers);
        }

        [HttpGet]
        public async Task<IActionResult> GetCustomerList()
        {
            var customers = await _context.Parties
                .Where(p => p.PartyType == "customer" || p.PartyType == "both")
                .Select(p => new { value = p.PartyId.ToString(), text = p.PartyName })
                .ToListAsync();
            return Json(customers);
        }

        [HttpGet]
        public async Task<IActionResult> GetCategoryList()
        {
            var categories = await _context.ItemCategory
                .Select(c => new { value = c.CategoryId, text = c.CategoryName })
                .ToListAsync();
            return Json(categories);
        }

        // ── Static helpers ──────────────────────────────────────────────────────
        private static string GetBranchName(int id) => id switch
        {
            1 => "Main Branch",
            2 => "Branch 2",
            3 => "Branch 3",
            _ => $"Branch {id}"
        };

        private sealed class StockReportRow
        {
            public long itemId { get; set; }
            public string itemName { get; set; } = "";
            public string categoryName { get; set; } = "";
            public string itemType { get; set; } = "";
            public int branchId { get; set; }
            public decimal quantity { get; set; }
            public decimal reorderLevel { get; set; }
            public DateTime? lastUpdated { get; set; }
        }
        private static string GetStockStatus(decimal qty, decimal threshold) => qty <= 0 ? "Out of Stock" : qty <= threshold ? "Low Stock" : "In Stock";

        private static string GetPaymentModeLabel(int? mode) => mode switch
        {
            0 => "Credit",
            1 => "Cash",
            2 => "Cheque",
            3 => "Bank Transfer",
            _ => "N/A"
        };
    }
}