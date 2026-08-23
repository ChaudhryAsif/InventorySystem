using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class ConsumeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IStockService _stock;

        public ConsumeController(ApplicationDbContext context, IStockService stock)
        {
            _context = context;
            _stock = stock;
        }

        [HttpGet]
        public IActionResult Index() => View();

        // ── Saved-voucher list page ───────────────────────────────────────────
        [HttpGet]
        public IActionResult List() => View();

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] ConsumeInvoiceViewModel model)
        {
            if (model == null || model.Items == null || !model.Items.Any())
                return BadRequest("Invalid consumption data.");

            var branchId = model.BranchID ?? 1;
            var validItems = model.Items.Where(i => i.ItemId != null && i.Quantity > 0).ToList();

            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            // ── Pre-validate ALL stock before touching the database ────────────
            var stockError = ValidateStock(validItems, branchId);
            if (stockError != null)
                return Ok(new { success = false, message = stockError });

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

                await ApplyConsumeLinesAndStockAsync(invoice, validItems, branchId);

                transaction.Commit();
                return Ok(new { success = true, message = "Consumption voucher saved successfully.", id = invoice.ConsumeId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error saving consumption voucher: " + ex.Message });
            }
        }

        // ── UPDATE an existing voucher ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] ConsumeInvoiceViewModel model)
        {
            if (model == null || model.ConsumeId is null or <= 0)
                return BadRequest("Invalid voucher id.");
            if (model.Items == null || !model.Items.Any())
                return BadRequest("Invalid consumption data.");

            var branchId = model.BranchID ?? 1;
            var validItems = model.Items.Where(i => i.ItemId != null && i.Quantity > 0).ToList();
            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var invoice = _context.ConsumeInvoice.FirstOrDefault(c => c.ConsumeId == model.ConsumeId);
                if (invoice == null)
                    return Ok(new { success = false, message = "Voucher not found." });

                var bodies = _context.ConsumeInvoiceBody.Where(b => b.ConsumeId == invoice.ConsumeId).ToList();

                // 1) Reverse the old voucher's effects (restore stock, drop bodies)
                await ReverseConsumeEffectsAsync(bodies, invoice.BranchID ?? 1);
                _context.SaveChanges();

                // 2) Validate the new lines against the now-restored stock
                var stockError = ValidateStock(validItems, branchId);
                if (stockError != null)
                {
                    transaction.Rollback();
                    return Ok(new { success = false, message = stockError });
                }

                // 3) Update header + re-apply lines/stock
                invoice.ConsumeDate = model.ConsumeDate;
                invoice.BranchID = model.BranchID;
                invoice.RefNo = model.RefNo;
                invoice.Purpose = model.Purpose;
                invoice.Remarks = model.Remarks;

                await ApplyConsumeLinesAndStockAsync(invoice, validItems, branchId);

                transaction.Commit();
                return Ok(new { success = true, message = "Consumption voucher updated successfully.", id = invoice.ConsumeId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error updating consumption voucher: " + ex.Message });
            }
        }

        // ── DELETE a voucher (with stock reversal) ─────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var invoice = _context.ConsumeInvoice.FirstOrDefault(c => c.ConsumeId == id);
                if (invoice == null)
                    return Ok(new { success = false, message = "Voucher not found." });

                var bodies = _context.ConsumeInvoiceBody.Where(b => b.ConsumeId == id).ToList();

                await ReverseConsumeEffectsAsync(bodies, invoice.BranchID ?? 1);
                _context.ConsumeInvoice.Remove(invoice);
                _context.SaveChanges();

                transaction.Commit();
                return Ok(new { success = true, message = "Consumption voucher deleted successfully." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error deleting consumption voucher: " + ex.Message });
            }
        }

        // ── LIST data (with optional search + date range) ─────────────────────
        [HttpGet]
        public IActionResult GetConsumeVouchers(string? search, DateTime? from, DateTime? to, int page = 1, int pageSize = 50)
        {
            var query = _context.ConsumeInvoice.AsQueryable();

            if (from.HasValue) query = query.Where(c => c.ConsumeDate >= from.Value.Date);
            if (to.HasValue) query = query.Where(c => c.ConsumeDate < to.Value.Date.AddDays(1));

            var vouchers = query
                .OrderByDescending(c => c.ConsumeId)
                .Select(c => new
                {
                    c.ConsumeId,
                    ConsumeDate = c.ConsumeDate,
                    c.RefNo,
                    c.Purpose,
                    ItemCount = _context.ConsumeInvoiceBody.Count(b => b.ConsumeId == c.ConsumeId),
                    TotalQty = _context.ConsumeInvoiceBody.Where(b => b.ConsumeId == c.ConsumeId).Sum(b => (decimal?)b.Quantity) ?? 0
                })
                .ToList();

            var result = vouchers.Select(c => new
            {
                c.ConsumeId,
                ConsumeDate = c.ConsumeDate.HasValue ? c.ConsumeDate.Value.ToString("yyyy-MM-dd") : "",
                c.RefNo,
                c.Purpose,
                c.ItemCount,
                c.TotalQty
            });

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLower();
                result = result.Where(c =>
                    (c.Purpose ?? "").ToLower().Contains(q) ||
                    (c.RefNo ?? "").ToLower().Contains(q) ||
                    c.ConsumeId.ToString().Contains(q));
            }

            var resultList = result.ToList();
            var totalCount = resultList.Count;
            var paged = resultList.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Json(new { data = paged, totalCount, page, pageSize });
        }

        // ── Single voucher (for view + edit) ───────────────────────────────────
        [HttpGet]
        public IActionResult GetConsumeById(int id)
        {
            var invoice = _context.ConsumeInvoice.FirstOrDefault(c => c.ConsumeId == id);
            if (invoice == null) return NotFound();

            var branchId = invoice.BranchID ?? 1;

            var bodies = _context.ConsumeInvoiceBody
                .Where(b => b.ConsumeId == id)
                .Select(b => new
                {
                    b.ItemId,
                    b.Descr,
                    b.Quantity,
                    // For edit mode: max consumable = current stock + qty already on this voucher
                    AvailStock = (_context.Stock
                        .Where(s => s.ItemId == b.ItemId && s.BranchId == branchId)
                        .Select(s => (decimal?)s.Quantity)
                        .FirstOrDefault() ?? 0) + b.Quantity
                })
                .ToList();

            return Json(new
            {
                invoice.ConsumeId,
                ConsumeDate = invoice.ConsumeDate.HasValue ? invoice.ConsumeDate.Value.ToString("yyyy-MM-dd") : "",
                invoice.BranchID,
                invoice.RefNo,
                invoice.Purpose,
                invoice.Remarks,
                Items = bodies
            });
        }

        // ── Print view ────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Print(int id)
        {
            var invoice = _context.ConsumeInvoice
                .Include(c => c.ConsumeInvoiceBodies)
                .FirstOrDefault(c => c.ConsumeId == id);
            if (invoice == null) return NotFound();

            return View(invoice);
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

        // ───────────────────────────────────────────────────────────────────────
        //  Private helpers — single source of truth for stock effects
        //  (Consume never touches AccountLedger/PaymentVoucher — internal use only)
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>Returns an error message if any grouped line exceeds stock, else null.</summary>
        private string? ValidateStock(List<ConsumeItemViewModel> validItems, int branchId)
        {
            var groupedItems = validItems
                .GroupBy(i => i.ItemId)
                .Select(g => new { ItemId = g.Key, TotalQty = g.Sum(x => x.Quantity), Desc = g.First().Desc });

            foreach (var group in groupedItems)
            {
                var stock = _context.Stock
                    .FirstOrDefault(s => s.ItemId == group.ItemId && s.BranchId == branchId);

                if (stock == null || stock.Quantity < group.TotalQty)
                    return $"Insufficient stock for \"{group.Desc ?? group.ItemId.ToString()}\". " +
                           $"Available: {stock?.Quantity ?? 0}, Requested: {group.TotalQty}";
            }
            return null;
        }

        /// <summary>Adds line items and deducts stock. Caller must have validated stock first.</summary>
        private async Task ApplyConsumeLinesAndStockAsync(ConsumeInvoice invoice, List<ConsumeItemViewModel> validItems, int branchId)
        {
            foreach (var item in validItems)
            {
                _context.ConsumeInvoiceBody.Add(new ConsumeInvoiceBody
                {
                    ConsumeId = invoice.ConsumeId,
                    ItemId = item.ItemId,
                    Descr = item.Desc,
                    Quantity = item.Quantity
                });

                await _stock.AdjustAsync(item.ItemId!.Value, branchId, -item.Quantity);
            }

            _context.SaveChanges();
        }

        /// <summary>Restores stock and removes the line-item rows tied to this voucher.</summary>
        private async Task ReverseConsumeEffectsAsync(List<ConsumeInvoiceBody> bodies, int branchId)
        {
            foreach (var body in bodies)
            {
                if (body.ItemId == null) continue;
                await _stock.AdjustAsync(body.ItemId.Value, branchId, body.Quantity);
            }

            _context.ConsumeInvoiceBody.RemoveRange(bodies);
        }
    }
}
