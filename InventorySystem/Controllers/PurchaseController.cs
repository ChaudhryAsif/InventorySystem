using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class PurchaseController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILedgerPostingService _ledgerPosting;
        private readonly AccountingOptions _accountingOptions;
        private readonly IStockService _stock;

        public PurchaseController(ApplicationDbContext context, ILedgerPostingService ledgerPosting, IOptions<AccountingOptions> accountingOptions, IStockService stock)
        {
            _context = context;
            _ledgerPosting = ledgerPosting;
            _accountingOptions = accountingOptions.Value;
            _stock = stock;
        }

        [HttpGet]
        public IActionResult Index() => View();

        // ── Saved-invoice list page ───────────────────────────────────────────
        [HttpGet]
        public IActionResult List() => View();

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] PurchaseInvoiceViewModel model)
        {
            if (model == null || model.Items == null || !model.Items.Any())
                return BadRequest("Invalid invoice data.");

            var validItems = model.Items.Where(i => i.Itemid != null && i.Quantity > 0).ToList();
            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            var branchId = model.BranchID ?? 1;

            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var invoice = new PurchaseInvoice
                {
                    PurchaseDate = model.PurchaseDate,
                    VendorID = model.VendorID,
                    BillNo = model.BillNo,
                    BranchID = model.BranchID,
                    PaymentMode = model.PaymentMode,
                    Remarks = model.Remarks,
                    GSTPer = model.GSTPer,
                    GSTAmount = model.GSTAmount,
                    FreightExp = model.FreightExp,
                    OtherExp = model.OtherExp,
                    Discount = model.Discount,
                    TotalAmount = model.TotalAmount,
                    AmountPaid = model.NetAmount   // total amount the invoice is worth
                };

                _context.PurchaseInvoice.Add(invoice);
                _context.SaveChanges(); // materialise PurchaseId

                await ApplyPurchaseLinesAndStockAsync(invoice, validItems, branchId);
                await PostLedgerForPurchaseAsync(invoice);

                transaction.Commit();
                return Ok(new { success = true, message = "Invoice saved successfully", id = invoice.PurchaseId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error saving purchase invoice: " + ex.Message });
            }
        }

        // ── UPDATE an existing invoice ────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] PurchaseInvoiceViewModel model)
        {
            if (model == null || model.PurchaseId is null or <= 0)
                return BadRequest("Invalid invoice id.");
            if (model.Items == null || !model.Items.Any())
                return BadRequest("Invalid invoice data.");

            var validItems = model.Items.Where(i => i.Itemid != null && i.Quantity > 0).ToList();
            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            var branchId = model.BranchID ?? 1;

            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var invoice = _context.PurchaseInvoice.FirstOrDefault(p => p.PurchaseId == model.PurchaseId);
                if (invoice == null)
                    return Ok(new { success = false, message = "Invoice not found." });

                var bodies = _context.PurchaseInvoiceBody.Where(b => b.PurchaseId == invoice.PurchaseId).ToList();

                // 1) Reverse the old invoice's effects (restore stock, drop ledger + bodies)
                await ReversePurchaseEffectsAsync(invoice, bodies);
                _context.SaveChanges();

                // 2) Update header + re-apply lines/stock/ledger
                invoice.PurchaseDate = model.PurchaseDate;
                invoice.VendorID = model.VendorID;
                invoice.BillNo = model.BillNo;
                invoice.BranchID = model.BranchID;
                invoice.PaymentMode = model.PaymentMode;
                invoice.Remarks = model.Remarks;
                invoice.GSTPer = model.GSTPer;
                invoice.GSTAmount = model.GSTAmount;
                invoice.FreightExp = model.FreightExp;
                invoice.OtherExp = model.OtherExp;
                invoice.Discount = model.Discount;
                invoice.TotalAmount = model.TotalAmount;
                invoice.AmountPaid = model.NetAmount;

                await ApplyPurchaseLinesAndStockAsync(invoice, validItems, branchId);
                await PostLedgerForPurchaseAsync(invoice);

                transaction.Commit();
                return Ok(new { success = true, message = "Invoice updated successfully", id = invoice.PurchaseId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error updating purchase invoice: " + ex.Message });
            }
        }

        // ── DELETE an invoice (with stock + ledger reversal) ──────────────────
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var invoice = _context.PurchaseInvoice.FirstOrDefault(p => p.PurchaseId == id);
                if (invoice == null)
                    return Ok(new { success = false, message = "Invoice not found." });

                var bodies = _context.PurchaseInvoiceBody.Where(b => b.PurchaseId == id).ToList();

                await ReversePurchaseEffectsAsync(invoice, bodies);
                _context.PurchaseInvoice.Remove(invoice);
                _context.SaveChanges();

                transaction.Commit();
                return Ok(new { success = true, message = "Purchase invoice deleted successfully." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error deleting purchase invoice: " + ex.Message });
            }
        }

        // ── LIST data (with optional search + date range) ─────────────────────
        [HttpGet]
        public IActionResult GetPurchases(string? search, DateTime? from, DateTime? to, int page = 1, int pageSize = 50)
        {
            var query = _context.PurchaseInvoice.AsQueryable();

            if (from.HasValue) query = query.Where(p => p.PurchaseDate >= from.Value.Date);
            if (to.HasValue) query = query.Where(p => p.PurchaseDate < to.Value.Date.AddDays(1));

            var purchases = query
                .OrderByDescending(p => p.PurchaseId)
                .Select(p => new
                {
                    p.PurchaseId,
                    PurchaseDate = p.PurchaseDate,
                    p.VendorID,
                    VendorName = _context.Parties
                        .Where(v => v.PartyId.ToString() == p.VendorID)
                        .Select(v => v.PartyName)
                        .FirstOrDefault(),
                    p.BillNo,
                    p.PaymentMode,
                    NetAmount = p.AmountPaid,
                    ItemCount = _context.PurchaseInvoiceBody.Count(b => b.PurchaseId == p.PurchaseId)
                })
                .ToList();

            var result = purchases.Select(p => new
            {
                p.PurchaseId,
                PurchaseDate = p.PurchaseDate.HasValue ? p.PurchaseDate.Value.ToString("yyyy-MM-dd") : "",
                VendorName = p.VendorName ?? $"#{p.VendorID}",
                p.BillNo,
                PaymentMode = GetPaymentModeLabel(p.PaymentMode),
                p.NetAmount,
                p.ItemCount
            });

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLower();
                result = result.Where(p =>
                    (p.VendorName ?? "").ToLower().Contains(q) ||
                    (p.BillNo ?? "").ToLower().Contains(q) ||
                    p.PurchaseId.ToString().Contains(q));
            }

            var resultList = result.ToList();
            var totalCount = resultList.Count;
            var paged = resultList.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Json(new { data = paged, totalCount, page, pageSize });
        }

        // ── Single invoice (for view + edit) ──────────────────────────────────
        [HttpGet]
        public IActionResult GetPurchaseById(int id)
        {
            var invoice = _context.PurchaseInvoice.FirstOrDefault(p => p.PurchaseId == id);
            if (invoice == null) return NotFound();

            var vendorName = _context.Parties
                .Where(v => v.PartyId.ToString() == invoice.VendorID)
                .Select(v => v.PartyName)
                .FirstOrDefault();

            var branchId = invoice.BranchID ?? 1;

            var bodies = _context.PurchaseInvoiceBody
                .Where(b => b.PurchaseId == id)
                .Select(b => new
                {
                    b.Itemid,
                    b.Descr,
                    b.Quantity,
                    b.PurPrice,
                    b.SalePrice,
                    b.DiscPer,
                    b.DiscAmt,
                    // For edit mode: max sellable = current stock + qty already on this invoice
                    AvailStock = (_context.Stock
                        .Where(s => s.ItemId == b.Itemid && s.BranchId == branchId)
                        .Select(s => (decimal?)s.Quantity)
                        .FirstOrDefault() ?? 0) + (b.Quantity ?? 0)
                })
                .ToList();

            return Json(new
            {
                invoice.PurchaseId,
                PurchaseDate = invoice.PurchaseDate.HasValue ? invoice.PurchaseDate.Value.ToString("yyyy-MM-dd") : "",
                invoice.VendorID,
                VendorName = vendorName ?? $"#{invoice.VendorID}",
                invoice.BillNo,
                invoice.BranchID,
                invoice.PaymentMode,
                invoice.Remarks,
                invoice.GSTPer,
                invoice.GSTAmount,
                invoice.FreightExp,
                invoice.OtherExp,
                invoice.Discount,
                invoice.TotalAmount,
                NetAmount = invoice.AmountPaid,
                Items = bodies
            });
        }

        // ── Print view ────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Print(int id)
        {
            var invoice = _context.PurchaseInvoice
                .Include(p => p.PurchaseInvoiceBodies)
                .FirstOrDefault(p => p.PurchaseId == id);
            if (invoice == null) return NotFound();

            ViewBag.VendorName = _context.Parties
                .Where(v => v.PartyId.ToString() == invoice.VendorID)
                .Select(v => v.PartyName)
                .FirstOrDefault() ?? $"#{invoice.VendorID}";
            ViewBag.PaymentMode = GetPaymentModeLabel(invoice.PaymentMode);

            return View(invoice);
        }

        [HttpGet]
        public IActionResult GetNextInvoiceNumber()
        {
            var nextId = (_context.PurchaseInvoice.Max(c => (int?)c.PurchaseId) ?? 0) + 1;
            return Json(new { invoiceNo = nextId });
        }

        // ───────────────────────────────────────────────────────────────────────
        //  Private helpers — single source of truth for stock + ledger effects
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>Adds line items and increases stock (goods received).</summary>
        private async Task ApplyPurchaseLinesAndStockAsync(PurchaseInvoice invoice, List<PurchaseInvoiceBodyViewModel> validItems, int branchId)
        {
            foreach (var item in validItems)
            {
                _context.PurchaseInvoiceBody.Add(new PurchaseInvoiceBody
                {
                    PurchaseId = invoice.PurchaseId,
                    Itemid = item.Itemid,
                    Descr = item.Desc,
                    Quantity = item.Quantity,
                    PurPrice = item.PurPrice,
                    SalePrice = item.SalePrice,
                    DiscPer = item.DiscPer,
                    DiscAmt = item.DiscAmt
                });

                var itemId = long.Parse(item.Itemid.ToString()!);
                await _stock.AdjustAsync(itemId, branchId, item.Quantity ?? 0);
            }
            _context.SaveChanges();
        }

        /// <summary>Posts the supplier payable + (for non-credit) the payment voucher and ledger.</summary>
        private async Task PostLedgerForPurchaseAsync(PurchaseInvoice invoice)
        {
            if (!int.TryParse(invoice.VendorID, out var vendorPartyId) || vendorPartyId <= 0)
                return;

            var modeLabel = GetPaymentModeLabel(invoice.PaymentMode);
            var entryDate = invoice.PurchaseDate ?? DateTime.Now;

            _context.AccountLedger.Add(new AccountLedger
            {
                PartyId = vendorPartyId,
                EntryDate = entryDate,
                TransactionType = TransactionTypes.PurchaseInvoice,
                ReferenceId = invoice.PurchaseId,
                Description = $"Purchase Invoice #{invoice.PurchaseId}" +
                                  (invoice.BillNo != null ? $" | Bill: {invoice.BillNo}" : ""),
                Debit = 0,
                Credit = invoice.AmountPaid ?? 0,   // we owe this amount
                BranchId = invoice.BranchID,
                CreatedDate = DateTime.Now
            });

            if (invoice.PaymentMode != 0)
            {
                var voucher = new PaymentVoucher
                {
                    PartyId = vendorPartyId,
                    VoucherType = "Payment",
                    VoucherDate = entryDate,
                    Amount = invoice.AmountPaid ?? 0,
                    PaymentMode = modeLabel,
                    ReferenceNo = invoice.BillNo,
                    PurchaseId = invoice.PurchaseId,
                    Notes = $"Auto-recorded — Invoice #{invoice.PurchaseId}",
                    CreatedDate = DateTime.Now
                };
                _context.PaymentVoucher.Add(voucher);
                _context.SaveChanges(); // materialise VoucherId

                _context.AccountLedger.Add(new AccountLedger
                {
                    PartyId = vendorPartyId,
                    EntryDate = entryDate,
                    TransactionType = TransactionTypes.Payment,
                    ReferenceId = voucher.VoucherId,
                    Description = $"{modeLabel} Payment | Invoice #{invoice.PurchaseId}",
                    Debit = invoice.AmountPaid ?? 0,   // we paid — reduces what we owe
                    Credit = 0,
                    BranchId = invoice.BranchID,
                    CreatedDate = DateTime.Now
                });
            }

            _context.SaveChanges();

            if (_accountingOptions.PostToGeneralLedger)
            {
                invoice.GLVoucherId = await _ledgerPosting.PostPurchaseAsync(invoice);
                _context.SaveChanges();
            }
        }

        /// <summary>Restores stock and removes all ledger/voucher/body rows tied to this invoice.</summary>
        private async Task ReversePurchaseEffectsAsync(PurchaseInvoice invoice, List<PurchaseInvoiceBody> bodies)
        {
            var branchId = invoice.BranchID ?? 1;

            // Restore stock: goods were added on save, so we subtract on reversal
            foreach (var body in bodies)
            {
                if (body.Itemid == null) continue;
                await _stock.AdjustAsync(body.Itemid.Value, branchId, -(body.Quantity ?? 0));
            }

            // Remove the purchase-invoice payable ledger row(s)
            var purchaseLedgers = _context.AccountLedger
                .Where(l => l.TransactionType == TransactionTypes.PurchaseInvoice && l.ReferenceId == invoice.PurchaseId)
                .ToList();
            _context.AccountLedger.RemoveRange(purchaseLedgers);

            // Remove auto-payment voucher(s) + their ledger rows
            var vouchers = _context.PaymentVoucher.Where(v => v.PurchaseId == invoice.PurchaseId).ToList();
            foreach (var v in vouchers)
            {
                var paymentLedgers = _context.AccountLedger
                    .Where(l => l.TransactionType == TransactionTypes.Payment && l.ReferenceId == v.VoucherId)
                    .ToList();
                _context.AccountLedger.RemoveRange(paymentLedgers);
            }
            _context.PaymentVoucher.RemoveRange(vouchers);

            // Void the matching double-entry voucher, if one was posted
            if (_accountingOptions.PostToGeneralLedger && invoice.GLVoucherId != null)
            {
                await _ledgerPosting.VoidAsync(invoice.GLVoucherId);
                invoice.GLVoucherId = null;
            }

            // Remove the old line items
            _context.PurchaseInvoiceBody.RemoveRange(bodies);
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
