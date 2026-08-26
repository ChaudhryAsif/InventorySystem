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

        [HttpGet]
        public async Task<IActionResult> GetStockData(int branchId = 0, int categoryId = 0, string? status = null)
        {
            try
            {
                var rawData = await _context.Stock
                    .Join(_context.Items, s => s.ItemId, i => i.ItemID, (s, i) => new { s, i })
                    .Join(_context.ItemCategory, si => si.i.CategoryID, c => c.CategoryId, (si, c) => new
                    {
                        itemId       = si.i.ItemID,
                        itemName     = si.i.ItemName ?? "",
                        categoryName = c.CategoryName,
                        categoryId   = si.i.CategoryID,
                        branchId     = si.s.BranchId,
                        quantity     = si.s.Quantity,
                        lastUpdated  = si.s.LastUpdated
                    })
                    .Where(x => branchId == 0 || x.branchId == branchId)
                    .Where(x => categoryId == 0 || x.categoryId == categoryId)
                    .ToListAsync();

                var filtered = status switch
                {
                    "instock"    => rawData.Where(x => x.quantity > 50).ToList(),
                    "lowstock"   => rawData.Where(x => x.quantity > 0 && x.quantity <= 50).ToList(),
                    "outofstock" => rawData.Where(x => x.quantity <= 0).ToList(),
                    _            => rawData
                };

                return Json(new
                {
                    success = true,
                    data = filtered.Select(x => new
                    {
                        x.itemId,
                        x.itemName,
                        x.categoryName,
                        branchName  = GetBranchName(x.branchId),
                        x.quantity,
                        stockStatus = GetStockStatus(x.quantity),
                        lastUpdated = x.lastUpdated.ToString("yyyy-MM-dd HH:mm")
                    }),
                    summary = new
                    {
                        total      = rawData.Count,
                        inStock    = rawData.Count(x => x.quantity > 50),
                        lowStock   = rawData.Count(x => x.quantity > 0 && x.quantity <= 50),
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
            int page = 1, int pageSize = 50)
        {
            try
            {
                var fromDate = from ?? DateTime.Now.AddMonths(-1);
                var toDate   = (to ?? DateTime.Now).Date.AddDays(1).AddTicks(-1);

                var query = _context.PurchaseInvoice
                    .Where(p => p.PurchaseDate >= fromDate && p.PurchaseDate <= toDate);

                if (!string.IsNullOrEmpty(vendorId))
                    query = query.Where(p => p.VendorID == vendorId);

                if (invoiceNo.HasValue && invoiceNo.Value > 0)
                    query = query.Where(p => p.PurchaseId == invoiceNo.Value);

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
                        p.TotalAmount,
                        NetAmount  = p.AmountPaid,
                        p.Remarks,
                        VendorName = _context.Parties
                            .Where(party => party.PartyId.ToString() == p.VendorID)
                            .Select(party => party.PartyName)
                            .FirstOrDefault() ?? p.VendorID
                    })
                    .ToListAsync();

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
                        totalAmount  = p.TotalAmount ?? 0,
                        netAmount    = p.NetAmount ?? 0,
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
                        totalAmount   = invoices.Sum(p => p.TotalAmount ?? 0),
                        netAmount     = invoices.Sum(p => p.NetAmount ?? 0)
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
            int page = 1, int pageSize = 50)
        {
            try
            {
                var fromDate = from ?? DateTime.Now.AddMonths(-1);
                var toDate   = (to ?? DateTime.Now).Date.AddDays(1).AddTicks(-1);

                var query = _context.SaleInvoice
                    .Where(s => s.SaleDate >= fromDate && s.SaleDate <= toDate);

                if (!string.IsNullOrEmpty(customerId))
                    query = query.Where(s => s.CustomerID == customerId);

                if (invoiceNo.HasValue && invoiceNo.Value > 0)
                    query = query.Where(s => s.SaleId == invoiceNo.Value);

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

        private static string GetStockStatus(decimal qty) => qty switch
        {
            > 50 => "In Stock",
            > 0  => "Low Stock",
            _    => "Out of Stock"
        };

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