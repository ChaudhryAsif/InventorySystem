using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class ConsumeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ConsumeController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index() => View();

        [HttpPost]
        public IActionResult Save([FromBody] ConsumeInvoiceViewModel model)
        {
            if (model == null || model.Items == null || !model.Items.Any())
                return BadRequest("Invalid consumption data.");

            var branchId = model.BranchID ?? 1;
            var validItems = model.Items.Where(i => i.ItemId != null && i.Quantity > 0).ToList();

            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            // ── Pre-validate ALL stock before touching the database ────────────
            var groupedItems = validItems
                .GroupBy(i => i.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    TotalQty = g.Sum(x => x.Quantity),
                    Desc = g.First().Desc
                })
                .ToList();

            foreach (var group in groupedItems)
            {
                var stock = _context.Stock
                    .FirstOrDefault(s => s.ItemId == group.ItemId && s.BranchId == branchId);

                if (stock == null || stock.Quantity < group.TotalQty)
                    return Ok(new
                    {
                        success = false,
                        message = $"Insufficient stock for \"{group.Desc ?? group.ItemId.ToString()}\". " +
                                  $"Available: {stock?.Quantity ?? 0}, Requested: {group.TotalQty}"
                    });
            }

            // ── All checks passed — write inside a single transaction ──────────
            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var invoice = new ConsumeInvoice
                {
                    ConsumeDate = model.ConsumeDate,
                    BranchID = model.BranchID,
                    RefNo = model.RefNo,
                    Purpose = model.Purpose,
                    Remarks = model.Remarks
                };

                _context.ConsumeInvoice.Add(invoice);
                _context.SaveChanges(); // materialise ConsumeId

                // ── Line items + stock deduction ──────────────────────────────
                foreach (var item in validItems)
                {
                    _context.ConsumeInvoiceBody.Add(new ConsumeInvoiceBody
                    {
                        ConsumeId = invoice.ConsumeId,
                        ItemId = item.ItemId,
                        Descr = item.Desc,
                        Quantity = item.Quantity
                    });

                    var stock = _context.Stock
                        .First(s => s.ItemId == item.ItemId && s.BranchId == branchId);

                    stock.Quantity -= item.Quantity;
                    stock.LastUpdated = DateTime.Now;
                }

                _context.SaveChanges();
                transaction.Commit();

                return Ok(new { success = true, message = "Consumption voucher saved successfully.", id = invoice.ConsumeId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error saving consumption voucher: " + ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetNextVoucherNumber()
        {
            var nextId = (_context.ConsumeInvoice.Max(c => (int?)c.ConsumeId) ?? 0) + 1;
            return Json(new { voucherNo = nextId });
        }

        [HttpGet]
        public IActionResult GetStockByItem(long itemId, int branchId = 1)
        {
            var stock = _context.Stock
                .FirstOrDefault(s => s.ItemId == itemId && s.BranchId == branchId);
            return Json(new { quantity = stock?.Quantity ?? 0 });
        }
    }
}