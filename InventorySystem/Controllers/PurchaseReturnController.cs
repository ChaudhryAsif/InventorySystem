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
    public class PurchaseReturnController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILedgerPostingService _ledgerPosting;
        private readonly AccountingOptions _accountingOptions;
        private readonly IStockService _stock;

        public PurchaseReturnController(ApplicationDbContext context, ILedgerPostingService ledgerPosting, IOptions<AccountingOptions> accountingOptions, IStockService stock)
        {
            _context = context;
            _ledgerPosting = ledgerPosting;
            _accountingOptions = accountingOptions.Value;
            _stock = stock;
        }

        [HttpGet]
        public IActionResult Index() => View();

        // ── Saved-return list page ────────────────────────────────────────────
        [HttpGet]
        public IActionResult List() => View();

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] PurchaseReturnViewModel model)
        {
            if (model == null || model.Items == null || !model.Items.Any())
                return BadRequest("Invalid return data.");

            var validItems = model.Items.Where(i => i.ItemId != null && i.Quantity > 0).ToList();
            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            var branchId = model.BranchID ?? 1;

            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var purchaseReturn = new PurchaseReturn
                {
                    ReturnDate         = model.ReturnDate,
                    VendorID           = model.VendorID,
                    OriginalPurchaseId = model.OriginalPurchaseId,
                    BranchID           = model.BranchID,
                    PaymentMode        = model.PaymentMode,
                    RefNo              = model.RefNo,
                    TotalAmount        = model.TotalAmount,
                    NetAmount          = model.NetAmount,
                    Remarks            = model.Remarks
                };

                _context.PurchaseReturn.Add(purchaseReturn);
                _context.SaveChanges();

                await ApplyReturnLinesAndStockAsync(purchaseReturn, validItems, branchId);
                await PostLedgerForPurchaseReturnAsync(purchaseReturn);

                transaction.Commit();
                return Ok(new { success = true, message = "Purchase return saved successfully.", id = purchaseReturn.PurchaseReturnId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ── UPDATE an existing return ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] PurchaseReturnViewModel model)
        {
            if (model == null || model.PurchaseReturnId is null or <= 0)
                return BadRequest("Invalid return id.");
            if (model.Items == null || !model.Items.Any())
                return BadRequest("Invalid return data.");

            var validItems = model.Items.Where(i => i.ItemId != null && i.Quantity > 0).ToList();
            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            var branchId = model.BranchID ?? 1;

            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var purchaseReturn = _context.PurchaseReturn.FirstOrDefault(r => r.PurchaseReturnId == model.PurchaseReturnId);
                if (purchaseReturn == null)
                    return Ok(new { success = false, message = "Return not found." });

                var bodies = _context.PurchaseReturnBody.Where(b => b.PurchaseReturnId == purchaseReturn.PurchaseReturnId).ToList();

                await ReversePurchaseReturnEffectsAsync(purchaseReturn, bodies);
                _context.SaveChanges();

                purchaseReturn.ReturnDate         = model.ReturnDate;
                purchaseReturn.VendorID           = model.VendorID;
                purchaseReturn.OriginalPurchaseId = model.OriginalPurchaseId;
                purchaseReturn.BranchID           = model.BranchID;
                purchaseReturn.PaymentMode        = model.PaymentMode;
                purchaseReturn.RefNo              = model.RefNo;
                purchaseReturn.TotalAmount        = model.TotalAmount;
                purchaseReturn.NetAmount          = model.NetAmount;
                purchaseReturn.Remarks            = model.Remarks;

                await ApplyReturnLinesAndStockAsync(purchaseReturn, validItems, branchId);
                await PostLedgerForPurchaseReturnAsync(purchaseReturn);

                transaction.Commit();
                return Ok(new { success = true, message = "Purchase return updated successfully.", id = purchaseReturn.PurchaseReturnId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error updating purchase return: " + ex.Message });
            }
        }

        // ── DELETE a return (with stock + ledger reversal) ────────────────────
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var purchaseReturn = _context.PurchaseReturn.FirstOrDefault(r => r.PurchaseReturnId == id);
                if (purchaseReturn == null)
                    return Ok(new { success = false, message = "Return not found." });

                var bodies = _context.PurchaseReturnBody.Where(b => b.PurchaseReturnId == id).ToList();

                await ReversePurchaseReturnEffectsAsync(purchaseReturn, bodies);
                _context.PurchaseReturn.Remove(purchaseReturn);
                _context.SaveChanges();

                transaction.Commit();
                return Ok(new { success = true, message = "Purchase return deleted successfully." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error deleting purchase return: " + ex.Message });
            }
        }

        // ── LIST data (with optional search + date range) ─────────────────────
        [HttpGet]
        public IActionResult GetPurchaseReturns(string? search, DateTime? from, DateTime? to, int page = 1, int pageSize = 50)
        {
            var query = _context.PurchaseReturn.AsQueryable();

            if (from.HasValue) query = query.Where(r => r.ReturnDate >= from.Value.Date);
            if (to.HasValue) query = query.Where(r => r.ReturnDate < to.Value.Date.AddDays(1));

            var returns = query
                .OrderByDescending(r => r.PurchaseReturnId)
                .Select(r => new
                {
                    r.PurchaseReturnId,
                    ReturnDate = r.ReturnDate,
                    r.VendorID,
                    VendorName = _context.Parties
                        .Where(v => v.PartyId.ToString() == r.VendorID)
                        .Select(v => v.PartyName)
                        .FirstOrDefault(),
                    r.RefNo,
                    r.PaymentMode,
                    r.NetAmount,
                    ItemCount = _context.PurchaseReturnBody.Count(b => b.PurchaseReturnId == r.PurchaseReturnId)
                })
                .ToList();

            var result = returns.Select(r => new
            {
                r.PurchaseReturnId,
                ReturnDate = r.ReturnDate.HasValue ? r.ReturnDate.Value.ToString("yyyy-MM-dd") : "",
                VendorName = r.VendorName ?? $"#{r.VendorID}",
                r.RefNo,
                PaymentMode = GetPaymentModeLabel(r.PaymentMode),
                r.NetAmount,
                r.ItemCount
            });

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLower();
                result = result.Where(r =>
                    (r.VendorName ?? "").ToLower().Contains(q) ||
                    (r.RefNo ?? "").ToLower().Contains(q) ||
                    r.PurchaseReturnId.ToString().Contains(q));
            }

            var resultList = result.ToList();
            var totalCount = resultList.Count;
            var paged = resultList.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Json(new { data = paged, totalCount, page, pageSize });
        }

        // ── Single return (for view + edit) ────────────────────────────────────
        [HttpGet]
        public IActionResult GetPurchaseReturnById(int id)
        {
            var purchaseReturn = _context.PurchaseReturn.FirstOrDefault(r => r.PurchaseReturnId == id);
            if (purchaseReturn == null) return NotFound();

            var vendorName = _context.Parties
                .Where(v => v.PartyId.ToString() == purchaseReturn.VendorID)
                .Select(v => v.PartyName)
                .FirstOrDefault();

            var bodies = _context.PurchaseReturnBody
                .Where(b => b.PurchaseReturnId == id)
                .Select(b => new
                {
                    b.ItemId,
                    b.Descr,
                    b.Quantity,
                    b.PurPrice,
                    b.DiscPer,
                    b.DiscAmt,
                    b.Total
                })
                .ToList();

            return Json(new
            {
                purchaseReturn.PurchaseReturnId,
                ReturnDate = purchaseReturn.ReturnDate.HasValue ? purchaseReturn.ReturnDate.Value.ToString("yyyy-MM-dd") : "",
                purchaseReturn.VendorID,
                VendorName = vendorName ?? $"#{purchaseReturn.VendorID}",
                purchaseReturn.OriginalPurchaseId,
                purchaseReturn.BranchID,
                purchaseReturn.PaymentMode,
                purchaseReturn.RefNo,
                purchaseReturn.TotalAmount,
                purchaseReturn.NetAmount,
                purchaseReturn.Remarks,
                Items = bodies
            });
        }

        // ── Print view ────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Print(int id)
        {
            var purchaseReturn = _context.PurchaseReturn
                .Include(r => r.PurchaseReturnBodies)
                .FirstOrDefault(r => r.PurchaseReturnId == id);
            if (purchaseReturn == null) return NotFound();

            ViewBag.VendorName = _context.Parties
                .Where(v => v.PartyId.ToString() == purchaseReturn.VendorID)
                .Select(v => v.PartyName)
                .FirstOrDefault() ?? $"#{purchaseReturn.VendorID}";
            ViewBag.PaymentMode = GetPaymentModeLabel(purchaseReturn.PaymentMode);

            return View(purchaseReturn);
        }

        [HttpGet]
        public IActionResult GetNextReturnNumber()
        {
            var nextId = (_context.PurchaseReturn.Max(c => (int?)c.PurchaseReturnId) ?? 0) + 1;
            return Json(new { returnNo = nextId });
        }

        // ───────────────────────────────────────────────────────────────────────
        //  Private helpers — single source of truth for stock + ledger effects
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>Adds line items and deducts stock (goods returned to supplier).</summary>
        private async Task ApplyReturnLinesAndStockAsync(PurchaseReturn purchaseReturn, List<PurchaseReturnItemViewModel> validItems, int branchId)
        {
            foreach (var item in validItems)
            {
                _context.PurchaseReturnBody.Add(new PurchaseReturnBody
                {
                    PurchaseReturnId = purchaseReturn.PurchaseReturnId,
                    ItemId           = item.ItemId,
                    Descr            = item.Desc,
                    Quantity         = item.Quantity,
                    PurPrice         = item.PurPrice,
                    DiscPer          = item.DiscPer,
                    DiscAmt          = item.DiscAmt,
                    Total            = item.Total
                });

                await _stock.AdjustAsync(item.ItemId!.Value, branchId, -item.Quantity);
            }
            _context.SaveChanges();
        }

        /// <summary>Posts the DebitNote against the supplier + (for non-credit) the refund receipt and ledger.</summary>
        private async Task PostLedgerForPurchaseReturnAsync(PurchaseReturn purchaseReturn)
        {
            if (!int.TryParse(purchaseReturn.VendorID, out var vendorPartyId) || vendorPartyId <= 0)
                return;

            var modeLabel = GetPaymentModeLabel(purchaseReturn.PaymentMode);
            var entryDate = purchaseReturn.ReturnDate ?? DateTime.Now;

            _context.AccountLedger.Add(new AccountLedger
            {
                PartyId         = vendorPartyId,
                EntryDate       = entryDate,
                TransactionType = TransactionTypes.DebitNote,
                ReferenceId     = purchaseReturn.PurchaseReturnId,
                Description     = $"Purchase Return #{purchaseReturn.PurchaseReturnId}" +
                                  (purchaseReturn.OriginalPurchaseId > 0 ? $" | Ref. Invoice #{purchaseReturn.OriginalPurchaseId}" : ""),
                Debit           = purchaseReturn.NetAmount ?? 0,  // reduces what we owe supplier
                Credit          = 0,
                BranchId        = purchaseReturn.BranchID,
                CreatedDate     = DateTime.Now
            });

            if (purchaseReturn.PaymentMode != 0)
            {
                var voucher = new PaymentVoucher
                {
                    PartyId     = vendorPartyId,
                    VoucherType = "Receipt",
                    VoucherDate = entryDate,
                    Amount      = purchaseReturn.NetAmount ?? 0,
                    PaymentMode = modeLabel,
                    ReferenceNo = purchaseReturn.RefNo,
                    PurchaseReturnId = purchaseReturn.PurchaseReturnId,
                    Notes       = $"Auto-recorded — Purchase Return #{purchaseReturn.PurchaseReturnId}",
                    CreatedDate = DateTime.Now
                };
                _context.PaymentVoucher.Add(voucher);
                _context.SaveChanges();

                _context.AccountLedger.Add(new AccountLedger
                {
                    PartyId         = vendorPartyId,
                    EntryDate       = entryDate,
                    TransactionType = TransactionTypes.Receipt,
                    ReferenceId     = voucher.VoucherId,
                    Description     = $"{modeLabel} Refund | Purchase Return #{purchaseReturn.PurchaseReturnId}",
                    Debit           = 0,
                    Credit          = purchaseReturn.NetAmount ?? 0,
                    BranchId        = purchaseReturn.BranchID,
                    CreatedDate     = DateTime.Now
                });
            }

            _context.SaveChanges();

            if (_accountingOptions.PostToGeneralLedger)
            {
                purchaseReturn.GLVoucherId = await _ledgerPosting.PostPurchaseReturnAsync(purchaseReturn);
                _context.SaveChanges();
            }
        }

        /// <summary>Restores stock and removes all ledger/voucher/body rows tied to this return.</summary>
        private async Task ReversePurchaseReturnEffectsAsync(PurchaseReturn purchaseReturn, List<PurchaseReturnBody> bodies)
        {
            var branchId = purchaseReturn.BranchID ?? 1;

            // Restore stock: goods were subtracted on save, so we add back on reversal
            foreach (var body in bodies)
            {
                if (body.ItemId == null) continue;
                await _stock.AdjustAsync(body.ItemId.Value, branchId, body.Quantity ?? 0);
            }

            // Remove the debit-note ledger row(s)
            var returnLedgers = _context.AccountLedger
                .Where(l => l.TransactionType == TransactionTypes.DebitNote && l.ReferenceId == purchaseReturn.PurchaseReturnId)
                .ToList();
            _context.AccountLedger.RemoveRange(returnLedgers);

            // Remove auto-receipt voucher(s) + their ledger rows
            var vouchers = _context.PaymentVoucher.Where(v => v.PurchaseReturnId == purchaseReturn.PurchaseReturnId).ToList();
            foreach (var v in vouchers)
            {
                var receiptLedgers = _context.AccountLedger
                    .Where(l => l.TransactionType == TransactionTypes.Receipt && l.ReferenceId == v.VoucherId)
                    .ToList();
                _context.AccountLedger.RemoveRange(receiptLedgers);
            }
            _context.PaymentVoucher.RemoveRange(vouchers);

            // Void the matching double-entry voucher, if one was posted
            if (_accountingOptions.PostToGeneralLedger && purchaseReturn.GLVoucherId != null)
            {
                await _ledgerPosting.VoidAsync(purchaseReturn.GLVoucherId);
                purchaseReturn.GLVoucherId = null;
            }

            // Remove the old line items
            _context.PurchaseReturnBody.RemoveRange(bodies);
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
