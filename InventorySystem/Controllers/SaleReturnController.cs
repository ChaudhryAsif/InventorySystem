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
    public class SaleReturnController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILedgerPostingService _ledgerPosting;
        private readonly AccountingOptions _accountingOptions;
        private readonly IStockService _stock;

        public SaleReturnController(ApplicationDbContext context, ILedgerPostingService ledgerPosting, IOptions<AccountingOptions> accountingOptions, IStockService stock)
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
        public async Task<IActionResult> Save([FromBody] SaleReturnViewModel model)
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
                var saleReturn = new SaleReturn
                {
                    ReturnDate     = model.ReturnDate,
                    CustomerID     = model.CustomerID,
                    OriginalSaleId = model.OriginalSaleId,
                    BranchID       = model.BranchID,
                    PaymentMode    = model.PaymentMode,
                    RefNo          = model.RefNo,
                    TotalAmount    = model.TotalAmount,
                    NetAmount      = model.NetAmount,
                    Remarks        = model.Remarks
                };

                _context.SaleReturn.Add(saleReturn);
                _context.SaveChanges();

                await ApplyReturnLinesAndStockAsync(saleReturn, validItems, branchId);
                await PostLedgerForSaleReturnAsync(saleReturn);

                transaction.Commit();
                return Ok(new { success = true, message = "Sale return saved successfully.", id = saleReturn.SaleReturnId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ── UPDATE an existing return ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] SaleReturnViewModel model)
        {
            if (model == null || model.SaleReturnId is null or <= 0)
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
                var saleReturn = _context.SaleReturn.FirstOrDefault(r => r.SaleReturnId == model.SaleReturnId);
                if (saleReturn == null)
                    return Ok(new { success = false, message = "Return not found." });

                var bodies = _context.SaleReturnBody.Where(b => b.SaleReturnId == saleReturn.SaleReturnId).ToList();

                await ReverseSaleReturnEffectsAsync(saleReturn, bodies);
                _context.SaveChanges();

                saleReturn.ReturnDate     = model.ReturnDate;
                saleReturn.CustomerID     = model.CustomerID;
                saleReturn.OriginalSaleId = model.OriginalSaleId;
                saleReturn.BranchID       = model.BranchID;
                saleReturn.PaymentMode    = model.PaymentMode;
                saleReturn.RefNo          = model.RefNo;
                saleReturn.TotalAmount    = model.TotalAmount;
                saleReturn.NetAmount      = model.NetAmount;
                saleReturn.Remarks        = model.Remarks;

                await ApplyReturnLinesAndStockAsync(saleReturn, validItems, branchId);
                await PostLedgerForSaleReturnAsync(saleReturn);

                transaction.Commit();
                return Ok(new { success = true, message = "Sale return updated successfully.", id = saleReturn.SaleReturnId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error updating sale return: " + ex.Message });
            }
        }

        // ── DELETE a return (with stock + ledger reversal) ────────────────────
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var saleReturn = _context.SaleReturn.FirstOrDefault(r => r.SaleReturnId == id);
                if (saleReturn == null)
                    return Ok(new { success = false, message = "Return not found." });

                var bodies = _context.SaleReturnBody.Where(b => b.SaleReturnId == id).ToList();

                await ReverseSaleReturnEffectsAsync(saleReturn, bodies);
                _context.SaleReturn.Remove(saleReturn);
                _context.SaveChanges();

                transaction.Commit();
                return Ok(new { success = true, message = "Sale return deleted successfully." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error deleting sale return: " + ex.Message });
            }
        }

        // ── LIST data (with optional search + date range) ─────────────────────
        [HttpGet]
        public IActionResult GetSaleReturns(string? search, DateTime? from, DateTime? to, int page = 1, int pageSize = 50)
        {
            var query = _context.SaleReturn.AsQueryable();

            if (from.HasValue) query = query.Where(r => r.ReturnDate >= from.Value.Date);
            if (to.HasValue) query = query.Where(r => r.ReturnDate < to.Value.Date.AddDays(1));

            var returns = query
                .OrderByDescending(r => r.SaleReturnId)
                .Select(r => new
                {
                    r.SaleReturnId,
                    ReturnDate = r.ReturnDate,
                    r.CustomerID,
                    CustomerName = _context.Parties
                        .Where(c => c.PartyId.ToString() == r.CustomerID)
                        .Select(c => c.PartyName)
                        .FirstOrDefault(),
                    r.RefNo,
                    r.PaymentMode,
                    r.NetAmount,
                    ItemCount = _context.SaleReturnBody.Count(b => b.SaleReturnId == r.SaleReturnId)
                })
                .ToList();

            var result = returns.Select(r => new
            {
                r.SaleReturnId,
                ReturnDate = r.ReturnDate.HasValue ? r.ReturnDate.Value.ToString("yyyy-MM-dd") : "",
                CustomerName = r.CustomerName ?? $"#{r.CustomerID}",
                r.RefNo,
                PaymentMode = GetPaymentModeLabel(r.PaymentMode),
                r.NetAmount,
                r.ItemCount
            });

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLower();
                result = result.Where(r =>
                    (r.CustomerName ?? "").ToLower().Contains(q) ||
                    (r.RefNo ?? "").ToLower().Contains(q) ||
                    r.SaleReturnId.ToString().Contains(q));
            }

            var resultList = result.ToList();
            var totalCount = resultList.Count;
            var paged = resultList.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Json(new { data = paged, totalCount, page, pageSize });
        }

        // ── Single return (for view + edit) ────────────────────────────────────
        [HttpGet]
        public IActionResult GetSaleReturnById(int id)
        {
            var saleReturn = _context.SaleReturn.FirstOrDefault(r => r.SaleReturnId == id);
            if (saleReturn == null) return NotFound();

            var customerName = _context.Parties
                .Where(c => c.PartyId.ToString() == saleReturn.CustomerID)
                .Select(c => c.PartyName)
                .FirstOrDefault();

            var bodies = _context.SaleReturnBody
                .Where(b => b.SaleReturnId == id)
                .Select(b => new
                {
                    b.ItemId,
                    b.Descr,
                    b.Quantity,
                    b.SalePrice,
                    b.DiscPer,
                    b.DiscAmt,
                    b.Total
                })
                .ToList();

            return Json(new
            {
                saleReturn.SaleReturnId,
                ReturnDate = saleReturn.ReturnDate.HasValue ? saleReturn.ReturnDate.Value.ToString("yyyy-MM-dd") : "",
                saleReturn.CustomerID,
                CustomerName = customerName ?? $"#{saleReturn.CustomerID}",
                saleReturn.OriginalSaleId,
                saleReturn.BranchID,
                saleReturn.PaymentMode,
                saleReturn.RefNo,
                saleReturn.TotalAmount,
                saleReturn.NetAmount,
                saleReturn.Remarks,
                Items = bodies
            });
        }

        // ── Print view ────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Print(int id)
        {
            var saleReturn = _context.SaleReturn
                .Include(r => r.SaleReturnBodies)
                .FirstOrDefault(r => r.SaleReturnId == id);
            if (saleReturn == null) return NotFound();

            ViewBag.CustomerName = _context.Parties
                .Where(c => c.PartyId.ToString() == saleReturn.CustomerID)
                .Select(c => c.PartyName)
                .FirstOrDefault() ?? $"#{saleReturn.CustomerID}";
            ViewBag.PaymentMode = GetPaymentModeLabel(saleReturn.PaymentMode);

            return View(saleReturn);
        }

        [HttpGet]
        public IActionResult GetNextReturnNumber()
        {
            var nextId = (_context.SaleReturn.Max(c => (int?)c.SaleReturnId) ?? 0) + 1;
            return Json(new { returnNo = nextId });
        }

        // ───────────────────────────────────────────────────────────────────────
        //  Private helpers — single source of truth for stock + ledger effects
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>Adds line items and increases stock (customer returned goods).</summary>
        private async Task ApplyReturnLinesAndStockAsync(SaleReturn saleReturn, List<SaleReturnItemViewModel> validItems, int branchId)
        {
            foreach (var item in validItems)
            {
                _context.SaleReturnBody.Add(new SaleReturnBody
                {
                    SaleReturnId = saleReturn.SaleReturnId,
                    ItemId       = item.ItemId,
                    Descr        = item.Desc,
                    Quantity     = item.Quantity,
                    SalePrice    = item.SalePrice,
                    DiscPer      = item.DiscPer,
                    DiscAmt      = item.DiscAmt,
                    Total        = item.Total
                });

                await _stock.AdjustAsync(item.ItemId!.Value, branchId, item.Quantity);
            }
            _context.SaveChanges();
        }

        /// <summary>Posts the CreditNote against the customer + (for non-credit) the refund payment and ledger.</summary>
        private async Task PostLedgerForSaleReturnAsync(SaleReturn saleReturn)
        {
            if (!int.TryParse(saleReturn.CustomerID, out var customerPartyId) || customerPartyId <= 0)
                return;

            var modeLabel = GetPaymentModeLabel(saleReturn.PaymentMode);
            var entryDate = saleReturn.ReturnDate ?? DateTime.Now;

            _context.AccountLedger.Add(new AccountLedger
            {
                PartyId         = customerPartyId,
                EntryDate       = entryDate,
                TransactionType = TransactionTypes.CreditNote,
                ReferenceId     = saleReturn.SaleReturnId,
                Description     = $"Sale Return #{saleReturn.SaleReturnId}" +
                                  (saleReturn.OriginalSaleId > 0 ? $" | Ref. Invoice #{saleReturn.OriginalSaleId}" : ""),
                Debit           = 0,
                Credit          = saleReturn.NetAmount ?? 0,  // reduces what customer owes us
                BranchId        = saleReturn.BranchID,
                CreatedDate     = DateTime.Now
            });

            if (saleReturn.PaymentMode != 0)
            {
                var voucher = new PaymentVoucher
                {
                    PartyId     = customerPartyId,
                    VoucherType = "Payment",
                    VoucherDate = entryDate,
                    Amount      = saleReturn.NetAmount ?? 0,
                    PaymentMode = modeLabel,
                    ReferenceNo = saleReturn.RefNo,
                    SaleReturnId = saleReturn.SaleReturnId,
                    Notes       = $"Auto-recorded — Sale Return #{saleReturn.SaleReturnId}",
                    CreatedDate = DateTime.Now
                };
                _context.PaymentVoucher.Add(voucher);
                _context.SaveChanges();

                _context.AccountLedger.Add(new AccountLedger
                {
                    PartyId         = customerPartyId,
                    EntryDate       = entryDate,
                    TransactionType = TransactionTypes.Payment,
                    ReferenceId     = voucher.VoucherId,
                    Description     = $"{modeLabel} Refund | Sale Return #{saleReturn.SaleReturnId}",
                    Debit           = saleReturn.NetAmount ?? 0,
                    Credit          = 0,
                    BranchId        = saleReturn.BranchID,
                    CreatedDate     = DateTime.Now
                });
            }

            _context.SaveChanges();

            if (_accountingOptions.PostToGeneralLedger)
            {
                saleReturn.GLVoucherId = await _ledgerPosting.PostSaleReturnAsync(saleReturn);
                _context.SaveChanges();
            }
        }

        /// <summary>Restores stock and removes all ledger/voucher/body rows tied to this return.</summary>
        private async Task ReverseSaleReturnEffectsAsync(SaleReturn saleReturn, List<SaleReturnBody> bodies)
        {
            var branchId = saleReturn.BranchID ?? 1;

            // Restore stock: goods were added on save, so we subtract on reversal
            foreach (var body in bodies)
            {
                if (body.ItemId == null) continue;
                await _stock.AdjustAsync(body.ItemId.Value, branchId, -(body.Quantity ?? 0));
            }

            // Remove the credit-note ledger row(s)
            var returnLedgers = _context.AccountLedger
                .Where(l => l.TransactionType == TransactionTypes.CreditNote && l.ReferenceId == saleReturn.SaleReturnId)
                .ToList();
            _context.AccountLedger.RemoveRange(returnLedgers);

            // Remove auto-payment voucher(s) + their ledger rows
            var vouchers = _context.PaymentVoucher.Where(v => v.SaleReturnId == saleReturn.SaleReturnId).ToList();
            foreach (var v in vouchers)
            {
                var paymentLedgers = _context.AccountLedger
                    .Where(l => l.TransactionType == TransactionTypes.Payment && l.ReferenceId == v.VoucherId)
                    .ToList();
                _context.AccountLedger.RemoveRange(paymentLedgers);
            }
            _context.PaymentVoucher.RemoveRange(vouchers);

            // Void the matching double-entry voucher, if one was posted
            if (_accountingOptions.PostToGeneralLedger && saleReturn.GLVoucherId != null)
            {
                await _ledgerPosting.VoidAsync(saleReturn.GLVoucherId);
                saleReturn.GLVoucherId = null;
            }

            // Remove the old line items
            _context.SaleReturnBody.RemoveRange(bodies);
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
