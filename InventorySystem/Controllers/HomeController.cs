using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        public HomeController(ApplicationDbContext context) => _context = context;

        public async Task<IActionResult> Index()
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
            ViewData["Title"] = "Dashboard";

            if (role != "Admin")
                return View("GenericDashboard");

            var vm = await BuildDashboardAsync();
            return View(vm);
        }

        // ── API endpoint for Sales Chart (AJAX refresh) ───────────────────────
        [HttpGet]
        public async Task<IActionResult> GetChartData()
        {
            var today = DateTime.Today;
            var labels = new List<string>();
            var sales = new List<decimal>();
            var purchases = new List<decimal>();

            for (int i = 6; i >= 0; i--)
            {
                var day = today.AddDays(-i);
                labels.Add(day.ToString("dd MMM"));

                var daySales = await _context.SaleInvoice
                    .Where(s => s.SaleDate.HasValue && s.SaleDate.Value.Date == day)
                    .SumAsync(s => (decimal?)s.NetAmount ?? 0);

                var dayPurchases = await _context.PurchaseInvoice
                    .Where(p => p.PurchaseDate.HasValue && p.PurchaseDate.Value.Date == day)
                    .SumAsync(p => (decimal?)p.TotalAmount ?? 0);

                sales.Add(daySales);
                purchases.Add(dayPurchases);
            }

            return Json(new { labels, sales, purchases });
        }

        // ── Private builder ───────────────────────────────────────────────────
        private async Task<DashboardViewModel> BuildDashboardAsync()
        {
            var today = DateTime.Today;
            var yesterday = today.AddDays(-1);
            var weekAgo = today.AddDays(-7);
            var lowStockThreshold = 10m;

            // ── Stat Cards ────────────────────────────────────────────────────
            var todaySales = await _context.SaleInvoice
                .Where(s => s.SaleDate.HasValue && s.SaleDate.Value.Date == today)
                .SumAsync(s => (decimal?)s.NetAmount ?? 0);

            var yesterdaySales = await _context.SaleInvoice
                .Where(s => s.SaleDate.HasValue && s.SaleDate.Value.Date == yesterday)
                .SumAsync(s => (decimal?)s.NetAmount ?? 0);

            var todayPurchases = await _context.PurchaseInvoice
                .Where(p => p.PurchaseDate.HasValue && p.PurchaseDate.Value.Date == today)
                .SumAsync(p => (decimal?)p.TotalAmount ?? 0);

            var lastWeekPurchases = await _context.PurchaseInvoice
                .Where(p => p.PurchaseDate.HasValue && p.PurchaseDate.Value.Date >= weekAgo && p.PurchaseDate.Value.Date < today)
                .SumAsync(p => (decimal?)p.TotalAmount ?? 0);

            var totalItems = await _context.Items.CountAsync(i => i.IsActive == true);

            var lowStockItems = await _context.Stock
                .Where(s => s.Quantity <= lowStockThreshold)
                .CountAsync();

            var pendingOrders = await _context.ProductionOrder
                .CountAsync(p => p.Status == "Pending" || p.Status == "InProduction");

            var yesterdayPending = await _context.ProductionOrder
                .CountAsync(p => p.CreatedDate.Date == yesterday
                              && (p.Status == "Pending" || p.Status == "InProduction"));

            // ── Sales Chart last 7 days ───────────────────────────────────────
            var chartLabels = new List<string>();
            var chartSales = new List<decimal>();
            var chartPurchases = new List<decimal>();

            for (int i = 6; i >= 0; i--)
            {
                var day = today.AddDays(-i);
                chartLabels.Add(day.ToString("dd MMM"));

                var ds = await _context.SaleInvoice
                    .Where(s => s.SaleDate.HasValue && s.SaleDate.Value.Date == day)
                    .SumAsync(s => (decimal?)s.NetAmount ?? 0);

                var dp = await _context.PurchaseInvoice
                    .Where(p => p.PurchaseDate.HasValue && p.PurchaseDate.Value.Date == day)
                    .SumAsync(p => (decimal?)p.TotalAmount ?? 0);

                chartSales.Add(ds);
                chartPurchases.Add(dp);
            }

            // ── Recent Activity ───────────────────────────────────────────────
            var recentSales = await _context.SaleInvoice
                .OrderByDescending(s => s.SaleId)
                .Take(3)
                .Select(s => new RecentActivityItem
                {
                    Icon = "🛒",
                    Title = $"Sale Invoice #{s.SaleId} — {s.NetAmount:N0}",
                    Time = s.SaleDate.HasValue ? s.SaleDate.Value.ToString("dd MMM, hh:mm tt") : "",
                    Type = "sale"
                }).ToListAsync();

            var recentPurchases = await _context.PurchaseInvoice
                .OrderByDescending(p => p.PurchaseId)
                .Take(3)
                .Select(p => new RecentActivityItem
                {
                    Icon = "📦",
                    Title = $"Purchase Invoice #{p.PurchaseId} — {p.TotalAmount:N0}",
                    Time = p.PurchaseDate.HasValue ? p.PurchaseDate.Value.ToString("dd MMM, hh:mm tt") : "",
                    Type = "purchase"
                }).ToListAsync();

            var recentProduction = await _context.ProductionOrder
                .OrderByDescending(p => p.ProductionOrderId)
                .Take(2)
                .Select(p => new RecentActivityItem
                {
                    Icon = "🏭",
                    Title = $"Production Order #{p.ProductionOrderId} — {p.ProductName} ({p.Status})",
                    Time = p.CreatedDate.ToString("dd MMM, hh:mm tt"),
                    Type = "production"
                }).ToListAsync();

            var activities = recentSales
                .Concat(recentPurchases)
                .Concat(recentProduction)
                .OrderByDescending(a => a.Time)
                .Take(8)
                .ToList();

            // ── Top Selling Items (this month) ────────────────────────────────
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var topSelling = await _context.SaleInvoiceBody
                .Where(b => b.SaleInvoice != null
                         && b.SaleInvoice.SaleDate.HasValue
                         && b.SaleInvoice.SaleDate.Value >= monthStart)
                .GroupBy(b => b.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    QuantitySold = g.Sum(x => x.Quantity ?? 0),
                    Revenue = g.Sum(x => x.Total ?? 0)
                })
                .OrderByDescending(g => g.Revenue)
                .Take(5)
                .ToListAsync();

            var topSellingItems = new List<TopSellingItem>();
            foreach (var t in topSelling)
            {
                if (t.ItemId == null) continue;
                var item = await _context.Items
                    .Include(i => i.Category)
                    .FirstOrDefaultAsync(i => i.ItemID == t.ItemId);
                var stock = await _context.Stock
                    .Where(s => s.ItemId == t.ItemId)
                    .SumAsync(s => (decimal?)s.Quantity ?? 0);

                topSellingItems.Add(new TopSellingItem
                {
                    ItemId = t.ItemId.Value,
                    ItemName = item?.ItemName ?? "—",
                    Category = item?.Category?.CategoryName ?? "—",
                    QuantitySold = t.QuantitySold,
                    Revenue = t.Revenue,
                    CurrentStock = stock
                });
            }

            // ── Low Stock Alerts ──────────────────────────────────────────────
            var lowStock = await _context.Stock
                .Where(s => s.Quantity <= lowStockThreshold)
                .Include(s => s.Item)
                    .ThenInclude(i => i.Category)
                .OrderBy(s => s.Quantity)
                .Take(5)
                .Select(s => new LowStockAlert
                {
                    ItemId = s.ItemId,
                    ItemName = s.Item != null ? s.Item.ItemName ?? "—" : "—",
                    Category = s.Item != null && s.Item.Category != null ? s.Item.Category.CategoryName : "—",
                    CurrentStock = s.Quantity
                }).ToListAsync();

            // ── Top Customers (this month) ────────────────────────────────────
            var topCustomers = await _context.SaleInvoice
                .Where(s => s.SaleDate.HasValue && s.SaleDate.Value >= monthStart)
                .GroupBy(s => s.CustomerID)
                .Select(g => new
                {
                    CustomerId = g.Key,
                    Total = g.Sum(x => x.NetAmount ?? 0),
                    Count = g.Count()
                })
                .OrderByDescending(g => g.Total)
                .Take(5)
                .ToListAsync();

            var topCustomerList = new List<TopParty>();
            foreach (var c in topCustomers)
            {
                var party = await _context.Parties
                    .FirstOrDefaultAsync(p => p.PartyId.ToString() == c.CustomerId);
                topCustomerList.Add(new TopParty
                {
                    PartyName = party?.PartyName ?? c.CustomerId ?? "—",
                    TotalAmount = c.Total,
                    TotalInvoices = c.Count
                });
            }

            // ── Top Suppliers (this month) ────────────────────────────────────
            var topSuppliers = await _context.PurchaseInvoice
                .Where(p => p.PurchaseDate.HasValue && p.PurchaseDate.Value >= monthStart)
                .GroupBy(p => p.VendorID)
                .Select(g => new
                {
                    VendorId = g.Key,
                    Total = g.Sum(x => x.TotalAmount ?? 0),
                    Count = g.Count()
                })
                .OrderByDescending(g => g.Total)
                .Take(5)
                .ToListAsync();

            var topSupplierList = new List<TopParty>();
            foreach (var s in topSuppliers)
            {
                var party = await _context.Parties
                    .FirstOrDefaultAsync(p => p.PartyId.ToString() == s.VendorId);
                topSupplierList.Add(new TopParty
                {
                    PartyName = party?.PartyName ?? s.VendorId ?? "—",
                    TotalAmount = s.Total,
                    TotalInvoices = s.Count
                });
            }

            return new DashboardViewModel
            {
                TodaySales = todaySales,
                YesterdaySales = yesterdaySales,
                TodayPurchases = todayPurchases,
                LastWeekPurchases = lastWeekPurchases,
                TotalItems = totalItems,
                LowStockCount = lowStockItems,
                PendingOrders = pendingOrders,
                YesterdayPendingOrders = yesterdayPending,
                ChartLabels = chartLabels,
                ChartSalesData = chartSales,
                ChartPurchaseData = chartPurchases,
                RecentActivities = activities,
                TopSellingItems = topSellingItems,
                LowStockAlerts = lowStock,
                TopCustomers = topCustomerList,
                TopSuppliers = topSupplierList
            };
        }
    }
}
