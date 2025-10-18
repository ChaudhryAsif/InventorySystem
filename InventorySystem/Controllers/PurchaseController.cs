using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    public class PurchaseController : Controller
    {
        private readonly ApplicationDbContext _context;
        public PurchaseController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var purchases = await _context.Invoices
                .Include(i => i.Supplier)
                .Where(i => i.Type == "Purchase")
                .OrderByDescending(i => i.Date)
                .ToListAsync();
            return View(purchases);
        }

        public IActionResult Create()
        {
            ViewBag.Suppliers = _context.Suppliers.ToList();
            ViewBag.Products = _context.Products.ToList();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(int supplierId, List<int> productIds, List<int> qty, List<decimal> rate)
        {
            var invoice = new Invoice
            {
                Type = "Purchase",
                SupplierId = supplierId,
                Date = DateTime.Now,
                Items = new List<InvoiceItem>()
            };

            decimal total = 0;
            for (int i = 0; i < productIds.Count; i++)
            {
                var item = new InvoiceItem
                {
                    ProductId = productIds[i],
                    Quantity = qty[i],
                    Rate = rate[i]
                };
                invoice.Items.Add(item);
                total += item.Total;

                // Stock In
                var product = await _context.Products.FindAsync(productIds[i]);
                if (product != null)
                    product.CurrentStock += qty[i];
            }

            invoice.TotalAmount = total;
            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            // After await _context.SaveChangesAsync();
            var purchaseAccount = _context.Accounts.FirstOrDefault(a => a.Name == "Purchases")
                ?? new Account { Name = "Purchases", Type = "Expense" };
            var cashAccount = _context.Accounts.FirstOrDefault(a => a.Name == "Cash")
                ?? new Account { Name = "Cash", Type = "Asset" };

            _context.Accounts.AttachRange(purchaseAccount, cashAccount);

            _context.JournalEntries.Add(new JournalEntry
            {
                Date = DateTime.Now,
                DebitAccountId = purchaseAccount.Id,
                CreditAccountId = cashAccount.Id,
                Amount = invoice.TotalAmount,
                Description = $"Purchase #{invoice.Id}"
            });
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
