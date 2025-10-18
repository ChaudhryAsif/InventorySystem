using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    public class SaleController : Controller
    {
        private readonly ApplicationDbContext _context;
        public SaleController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var sales = await _context.Invoices
                .Include(i => i.Customer)
                .Where(i => i.Type == "Sale")
                .OrderByDescending(i => i.Date)
                .ToListAsync();
            return View(sales);
        }


        public IActionResult Create()
        {
            ViewBag.Customers = _context.Customers.ToList();
            ViewBag.Products = _context.Products.ToList();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(int customerId, List<int> productIds, List<int> qty, List<decimal> rate)
        {
            var invoice = new Invoice
            {
                Type = "Sale",
                CustomerId = customerId,
                Date = DateTime.Now,
                Items = new List<InvoiceItem>()
            };

            decimal total = 0;
            for (int i = 0; i < productIds.Count; i++)
            {
                var product = await _context.Products.FindAsync(productIds[i]);
                if (product == null) continue;

                if (product.CurrentStock < qty[i])
                {
                    ModelState.AddModelError("", $"Not enough stock for {product.Name}");
                    return View();
                }

                var item = new InvoiceItem
                {
                    ProductId = productIds[i],
                    Quantity = qty[i],
                    Rate = rate[i]
                };
                invoice.Items.Add(item);
                total += item.Total;

                // Stock Out
                product.CurrentStock -= qty[i];
            }

            invoice.TotalAmount = total;
            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            var salesAccount = _context.Accounts.FirstOrDefault(a => a.Name == "Sales")
    ?? new Account { Name = "Sales", Type = "Income" };
            var cashAccount = _context.Accounts.FirstOrDefault(a => a.Name == "Cash")
                ?? new Account { Name = "Cash", Type = "Asset" };

            _context.Accounts.AttachRange(salesAccount, cashAccount);

            _context.JournalEntries.Add(new JournalEntry
            {
                Date = DateTime.Now,
                DebitAccountId = cashAccount.Id,
                CreditAccountId = salesAccount.Id,
                Amount = invoice.TotalAmount,
                Description = $"Sale #{invoice.Id}"
            });
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
