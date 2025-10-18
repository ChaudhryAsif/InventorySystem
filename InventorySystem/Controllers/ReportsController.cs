using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;
        public ReportsController(ApplicationDbContext context) => _context = context;

        // STOCK SUMMARY (unchanged, uses Products)
        public async Task<IActionResult> StockSummary()
        {
            var data = await _context.Products
                .Include(p => p.Category)
                .Select(p => new
                {
                    p.SKU,
                    p.Name,
                    Category = p.Category!.Name,
                    p.CurrentStock,
                    p.ReorderLevel,
                    p.CostPrice,
                    p.SalePrice,
                    StockValue = p.CurrentStock * p.CostPrice
                })
                .OrderBy(p => p.Name)
                .ToListAsync();

            return View(data);
        }

        // SUPPLIER LEDGER - uses Invoices where Type == "Purchase"
        public async Task<IActionResult> SupplierLedger(int? supplierId)
        {
            ViewBag.Suppliers = await _context.Suppliers.OrderBy(s => s.Name).ToListAsync();

            if (supplierId == null) return View(Enumerable.Empty<Invoice>());

            var supplier = await _context.Suppliers.FindAsync(supplierId);
            if (supplier == null) return NotFound();

            var purchases = await _context.Invoices
                .Include(i => i.Supplier)
                .Include(i => i.Items)
                .ThenInclude(it => it.Product)
                .Where(i => i.Type == "Purchase" && i.SupplierId == supplierId)
                .OrderBy(i => i.Date)
                .ToListAsync();

            ViewBag.SupplierName = supplier.Name;
            return View(purchases); // view model: IEnumerable<Invoice>
        }

        // CUSTOMER LEDGER - uses Invoices where Type == "Sale"
        public async Task<IActionResult> CustomerLedger(int? customerId)
        {
            ViewBag.Customers = await _context.Customers.OrderBy(c => c.Name).ToListAsync();

            if (customerId == null) return View(Enumerable.Empty<Invoice>());

            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null) return NotFound();

            var sales = await _context.Invoices
                .Include(i => i.Customer)
                .Include(i => i.Items)
                .ThenInclude(it => it.Product)
                .Where(i => i.Type == "Sale" && i.CustomerId == customerId)
                .OrderBy(i => i.Date)
                .ToListAsync();

            ViewBag.CustomerName = customer.Name;
            return View(sales); // view model: IEnumerable<Invoice>
        }

        // PROFIT SUMMARY - compute using Invoices + InvoiceItems and Product.CostPrice
        public async Task<IActionResult> ProfitSummary(DateTime? from, DateTime? to)
        {
            from ??= DateTime.Today.AddMonths(-1);
            to ??= DateTime.Today;

            // Get sales invoices in range with items and product cost
            var sales = await _context.Invoices
                .Include(i => i.Items)
                .ThenInclude(it => it.Product)
                .Where(i => i.Type == "Sale" && i.Date >= from && i.Date <= to)
                .ToListAsync();

            // Purchases total (if you want separate)
            var purchases = await _context.Invoices
                .Include(i => i.Items)
                .ThenInclude(it => it.Product)
                .Where(i => i.Type == "Purchase" && i.Date >= from && i.Date <= to)
                .ToListAsync();

            decimal totalSales = sales.Sum(s => s.TotalAmount);
            decimal totalPurchases = purchases.Sum(p => p.TotalAmount);

            // Cost of goods sold: for each sale, sum quantity * product.CostPrice
            decimal totalCOGS = sales.Sum(s => s.Items?.Sum(it => (it.Product?.CostPrice ?? 0m) * it.Quantity) ?? 0m);

            decimal grossProfit = totalSales - totalCOGS;

            ViewBag.From = from!.Value.ToString("yyyy-MM-dd");
            ViewBag.To = to!.Value.ToString("yyyy-MM-dd");

            var model = new
            {
                totalSales,
                totalPurchases,
                totalCOGS,
                grossProfit
            };

            return View(model);
        }
    }
}
