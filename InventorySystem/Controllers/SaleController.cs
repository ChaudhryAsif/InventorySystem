using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class SaleController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SaleController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index() => View();

        // ── Saved-invoice list page ───────────────────────────────────────────
        [HttpGet]
        public IActionResult List() => View();

        [HttpPost]
        public IActionResult Save([FromBody] SaleInvoiceViewModel model)
        {
            if (model == null || model.Items == null || !model.Items.Any())
                return BadRequest("Invalid sale data.");

            var branchId   = model.BranchID ?? 1;
            var validItems = model.Items.Where(i => i.ItemId != null && i.Quantity > 0).ToList();

            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            // ── Pre-validate ALL stock (grouped by item) before touching the database ──
            var stockError = ValidateStock(validItems, branchId);
            if (stockError != null)
                return Ok(new { success = false, message = stockError });

            // ── All checks passed — write inside a single transaction ─────────
            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var invoice = new SaleInvoice
                {
                    SaleDate    = model.SaleDate,
                    CustomerID  = model.CustomerID,
                    BranchID    = model.BranchID,
                    PaymentMode = model.PaymentMode,
                    RefNo       = model.RefNo,
                    GSTPer      = model.GSTPer,
                    GSTAmount   = model.GSTAmount,
                    Discount    = model.Discount,
                    FreightExp  = model.FreightExp,
                    OtherExp    = model.OtherExp,
                    TotalAmount = model.TotalAmount,
                    NetAmount   = model.NetAmount,
                    Remarks     = model.Remarks
                };

                _context.SaleInvoice.Add(invoice);
                _context.SaveChanges(); // materialise SaleId

                ApplyInvoiceLinesAndStock(invoice, validItems, branchId);
                PostLedgerForSale(invoice);

                transaction.Commit();
                return Ok(new { success = true, message = "Sale invoice saved successfully", id = invoice.SaleId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error saving sale invoice: " + ex.Message });
            }
        }

        // ── UPDATE an existing invoice ────────────────────────────────────────
        [HttpPost]
        public IActionResult Update([FromBody] SaleInvoiceViewModel model)
        {
            if (model == null || model.SaleId is null or <= 0)
                return BadRequest("Invalid invoice id.");
            if (model.Items == null || !model.Items.Any())
                return BadRequest("Invalid sale data.");

            var branchId   = model.BranchID ?? 1;
            var validItems = model.Items.Where(i => i.ItemId != null && i.Quantity > 0).ToList();
            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var invoice = _context.SaleInvoice.FirstOrDefault(s => s.SaleId == model.SaleId);
                if (invoice == null)
                    return Ok(new { success = false, message = "Invoice not found." });

                var bodies = _context.SaleInvoiceBody.Where(b => b.SaleId == invoice.SaleId).ToList();

                // 1) Reverse the old invoice's effects (restore stock, drop ledger + bodies)
                ReverseInvoiceEffects(invoice, bodies);
                _context.SaveChanges();

                // 2) Validate the new lines against the now-restored stock
                var stockError = ValidateStock(validItems, branchId);
                if (stockError != null)
                {
                    transaction.Rollback();
                    return Ok(new { success = false, message = stockError });
                }

                // 3) Update header + re-apply lines/stock/ledger
                invoice.SaleDate    = model.SaleDate;
                invoice.CustomerID  = model.CustomerID;
                invoice.BranchID    = model.BranchID;
                invoice.PaymentMode = model.PaymentMode;
                invoice.RefNo       = model.RefNo;
                invoice.GSTPer      = model.GSTPer;
                invoice.GSTAmount   = model.GSTAmount;
                invoice.Discount    = model.Discount;
                invoice.FreightExp  = model.FreightExp;
                invoice.OtherExp    = model.OtherExp;
                invoice.TotalAmount = model.TotalAmount;
                invoice.NetAmount   = model.NetAmount;
                invoice.Remarks     = model.Remarks;

                ApplyInvoiceLinesAndStock(invoice, validItems, branchId);
                PostLedgerForSale(invoice);

                transaction.Commit();
                return Ok(new { success = true, message = "Sale invoice updated successfully", id = invoice.SaleId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error updating sale invoice: " + ex.Message });
            }
        }

        // ── DELETE an invoice (with stock + ledger reversal) ──────────────────
        [HttpPost]
        public IActionResult Delete(int id)
        {
            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var invoice = _context.SaleInvoice.FirstOrDefault(s => s.SaleId == id);
                if (invoice == null)
                    return Ok(new { success = false, message = "Invoice not found." });

                var bodies = _context.SaleInvoiceBody.Where(b => b.SaleId == id).ToList();

                ReverseInvoiceEffects(invoice, bodies);
                _context.SaleInvoice.Remove(invoice);
                _context.SaveChanges();

                transaction.Commit();
                return Ok(new { success = true, message = "Sale invoice deleted successfully." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error deleting sale invoice: " + ex.Message });
            }
        }

        // ── LIST data (with optional search + date range) ─────────────────────
        [HttpGet]
        public IActionResult GetSales(string? search, DateTime? from, DateTime? to)
        {
            var query = _context.SaleInvoice.AsQueryable();

            if (from.HasValue) query = query.Where(s => s.SaleDate >= from.Value.Date);
            if (to.HasValue)   query = query.Where(s => s.SaleDate < to.Value.Date.AddDays(1));

            var sales = query
                .OrderByDescending(s => s.SaleId)
                .Select(s => new
                {
                    s.SaleId,
                    SaleDate = s.SaleDate,
                    s.CustomerID,
                    CustomerName = _context.Parties
                        .Where(p => p.PartyId.ToString() == s.CustomerID)
                        .Select(p => p.PartyName)
                        .FirstOrDefault(),
                    s.RefNo,
                    s.PaymentMode,
                    s.NetAmount,
                    ItemCount = _context.SaleInvoiceBody.Count(b => b.SaleId == s.SaleId)
                })
                .ToList();

            var result = sales.Select(s => new
            {
                s.SaleId,
                SaleDate = s.SaleDate.HasValue ? s.SaleDate.Value.ToString("yyyy-MM-dd") : "",
                CustomerName = s.CustomerName ?? $"#{s.CustomerID}",
                s.RefNo,
                PaymentMode = GetPaymentModeLabel(s.PaymentMode),
                s.NetAmount,
                s.ItemCount
            });

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLower();
                result = result.Where(s =>
                    (s.CustomerName ?? "").ToLower().Contains(q) ||
                    (s.RefNo ?? "").ToLower().Contains(q) ||
                    s.SaleId.ToString().Contains(q));
            }

            return Json(result);
        }

        // ── Single invoice (for view + edit) ──────────────────────────────────
        [HttpGet]
        public IActionResult GetSaleById(int id)
        {
            var invoice = _context.SaleInvoice.FirstOrDefault(s => s.SaleId == id);
            if (invoice == null) return NotFound();

            var customerName = _context.Parties
                .Where(p => p.PartyId.ToString() == invoice.CustomerID)
                .Select(p => p.PartyName)
                .FirstOrDefault();

            var branchId = invoice.BranchID ?? 1;

            var bodies = _context.SaleInvoiceBody
                .Where(b => b.SaleId == id)
                .Select(b => new
                {
                    b.ItemId,
                    b.Descr,
                    b.Quantity,
                    b.SalePrice,
                    b.DiscPer,
                    b.DiscAmt,
                    b.Total,
                    // For edit mode: max sellable = current stock + qty already on this invoice
                    AvailStock = (_context.Stock
                        .Where(s => s.ItemId == b.ItemId && s.BranchId == branchId)
                        .Select(s => (decimal?)s.Quantity)
                        .FirstOrDefault() ?? 0) + (b.Quantity ?? 0)
                })
                .ToList();

            return Json(new
            {
                invoice.SaleId,
                SaleDate = invoice.SaleDate.HasValue ? invoice.SaleDate.Value.ToString("yyyy-MM-dd") : "",
                invoice.CustomerID,
                CustomerName = customerName ?? $"#{invoice.CustomerID}",
                invoice.BranchID,
                invoice.PaymentMode,
                invoice.RefNo,
                invoice.GSTPer,
                invoice.GSTAmount,
                invoice.Discount,
                invoice.FreightExp,
                invoice.OtherExp,
                invoice.TotalAmount,
                invoice.NetAmount,
                invoice.Remarks,
                Items = bodies
            });
        }

        // ── Print view ────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Print(int id)
        {
            var invoice = _context.SaleInvoice
                .Include(s => s.SaleInvoiceBodies)
                .FirstOrDefault(s => s.SaleId == id);
            if (invoice == null) return NotFound();

            ViewBag.CustomerName = _context.Parties
                .Where(p => p.PartyId.ToString() == invoice.CustomerID)
                .Select(p => p.PartyName)
                .FirstOrDefault() ?? $"#{invoice.CustomerID}";
            ViewBag.PaymentMode = GetPaymentModeLabel(invoice.PaymentMode);

            return View(invoice);
        }

        [HttpGet]
        public IActionResult GetNextInvoiceNumber()
        {
            var nextId = (_context.SaleInvoice.Max(s => (int?)s.SaleId) ?? 0) + 1;
            return Json(new { invoiceNo = nextId });
        }

        [HttpGet]
        public IActionResult GetStockByItem(long itemId, int branchId = 1)
        {
            var stock = _context.Stock
                .FirstOrDefault(s => s.ItemId == itemId && s.BranchId == branchId);
            return Json(new { quantity = stock?.Quantity ?? 0 });
        }

        // ───────────────────────────────────────────────────────────────────────
        //  Private helpers — single source of truth for stock + ledger effects
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>Returns an error message if any grouped line exceeds stock, else null.</summary>
        private string? ValidateStock(List<SaleItemViewModel> validItems, int branchId)
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
        private void ApplyInvoiceLinesAndStock(SaleInvoice invoice, List<SaleItemViewModel> validItems, int branchId)
        {
            foreach (var item in validItems)
            {
                _context.SaleInvoiceBody.Add(new SaleInvoiceBody
                {
                    SaleId    = invoice.SaleId,
                    ItemId    = item.ItemId,
                    Descr     = item.Desc,
                    Quantity  = item.Quantity,
                    SalePrice = item.SalePrice,
                    DiscPer   = item.DiscPer,
                    DiscAmt   = item.DiscAmt,
                    Total     = item.Total
                });

                var stock = _context.Stock.First(s => s.ItemId == item.ItemId && s.BranchId == branchId);
                stock.Quantity   -= item.Quantity;
                stock.LastUpdated = DateTime.Now;
            }
            _context.SaveChanges();
        }

        /// <summary>Posts the customer receivable + (for non-credit) the receipt voucher and ledger.</summary>
        private void PostLedgerForSale(SaleInvoice invoice)
        {
            if (!int.TryParse(invoice.CustomerID, out var customerPartyId) || customerPartyId <= 0)
                return;

            var modeLabel = GetPaymentModeLabel(invoice.PaymentMode);
            var entryDate = invoice.SaleDate ?? DateTime.Now;

            _context.AccountLedger.Add(new AccountLedger
            {
                PartyId         = customerPartyId,
                EntryDate       = entryDate,
                TransactionType = TransactionTypes.SaleInvoice,
                ReferenceId     = invoice.SaleId,
                Description     = $"Sale Invoice #{invoice.SaleId}" +
                                  (invoice.RefNo != null ? $" | Ref: {invoice.RefNo}" : ""),
                Debit           = invoice.NetAmount ?? 0,
                Credit          = 0,
                BranchId        = invoice.BranchID,
                CreatedDate     = DateTime.Now
            });

            if (invoice.PaymentMode != 0)
            {
                var voucher = new PaymentVoucher
                {
                    PartyId     = customerPartyId,
                    VoucherType = "Receipt",
                    VoucherDate = entryDate,
                    Amount      = invoice.NetAmount ?? 0,
                    PaymentMode = modeLabel,
                    SaleId      = invoice.SaleId,
                    Notes       = $"Auto-recorded — Sale #{invoice.SaleId}",
                    CreatedDate = DateTime.Now
                };
                _context.PaymentVoucher.Add(voucher);
                _context.SaveChanges(); // materialise VoucherId

                _context.AccountLedger.Add(new AccountLedger
                {
                    PartyId         = customerPartyId,
                    EntryDate       = entryDate,
                    TransactionType = TransactionTypes.Receipt,
                    ReferenceId     = voucher.VoucherId,
                    Description     = $"{modeLabel} Receipt | Sale #{invoice.SaleId}",
                    Debit           = 0,
                    Credit          = invoice.NetAmount ?? 0,
                    BranchId        = invoice.BranchID,
                    CreatedDate     = DateTime.Now
                });
            }

            _context.SaveChanges();
        }

        /// <summary>Restores stock and removes all ledger/voucher/body rows tied to this invoice.</summary>
        private void ReverseInvoiceEffects(SaleInvoice invoice, List<SaleInvoiceBody> bodies)
        {
            var branchId = invoice.BranchID ?? 1;

            // Restore stock
            foreach (var body in bodies)
            {
                if (body.ItemId == null) continue;
                var stock = _context.Stock.FirstOrDefault(s => s.ItemId == body.ItemId && s.BranchId == branchId);
                if (stock == null)
                {
                    _context.Stock.Add(new Stock
                    {
                        ItemId      = body.ItemId.Value,
                        BranchId    = branchId,
                        Quantity    = body.Quantity ?? 0,
                        LastUpdated = DateTime.Now
                    });
                }
                else
                {
                    stock.Quantity   += body.Quantity ?? 0;
                    stock.LastUpdated = DateTime.Now;
                }
            }

            // Remove the sale-invoice receivable ledger row(s)
            var saleLedgers = _context.AccountLedger
                .Where(l => l.TransactionType == TransactionTypes.SaleInvoice && l.ReferenceId == invoice.SaleId)
                .ToList();
            _context.AccountLedger.RemoveRange(saleLedgers);

            // Remove auto-receipt voucher(s) + their ledger rows
            var vouchers = _context.PaymentVoucher.Where(v => v.SaleId == invoice.SaleId).ToList();
            foreach (var v in vouchers)
            {
                var receiptLedgers = _context.AccountLedger
                    .Where(l => l.TransactionType == TransactionTypes.Receipt && l.ReferenceId == v.VoucherId)
                    .ToList();
                _context.AccountLedger.RemoveRange(receiptLedgers);
            }
            _context.PaymentVoucher.RemoveRange(vouchers);

            // Remove the old line items
            _context.SaleInvoiceBody.RemoveRange(bodies);
        }

        private static string GetPaymentModeLabel(int? mode) => mode switch
        {
            1 => "Cash",
            2 => "Cheque",
            3 => "Bank Transfer",
            _ => "Credit"
        };
    }
}
