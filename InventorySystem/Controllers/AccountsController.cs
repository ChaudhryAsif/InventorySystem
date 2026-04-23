using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class AccountsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet] public IActionResult PaymentVoucher() => View();
        [HttpGet] public IActionResult PartyLedger()    => View();

        // ── Parties dropdown ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetParties(string? type = null)
        {
            var query = _context.Parties.AsQueryable();

            if (!string.IsNullOrEmpty(type))
                query = query.Where(p => p.PartyType == type || p.PartyType == "both");

            var parties = await query
                .Where(p => p.Status != "inactive")
                .Select(p => new
                {
                    value     = p.PartyId,
                    text      = p.PartyName,
                    partyType = p.PartyType,
                    phone     = p.Phone ?? ""
                })
                .OrderBy(p => p.text)
                .ToListAsync();

            return Json(new { success = true, data = parties });
        }

        // ── Party balance (live, computed from AccountLedger) ────────────────
        [HttpGet]
        public async Task<IActionResult> GetPartyBalance(int partyId)
        {
            var party = await _context.Parties.FindAsync(partyId);
            if (party == null) return Json(new { success = false, message = "Party not found." });

            var entries = await _context.AccountLedger
                .Where(l => l.PartyId == partyId && !l.IsVoid)
                .ToListAsync();

            var totalDebit  = entries.Sum(e => e.Debit);
            var totalCredit = entries.Sum(e => e.Credit);

            bool isSupplier = party.PartyType == "supplier" || party.PartyType == "both";
            var balance     = isSupplier ? totalCredit - totalDebit : totalDebit - totalCredit;

            return Json(new
            {
                success     = true,
                partyId,
                partyName   = party.PartyName,
                partyType   = party.PartyType,
                totalDebit,
                totalCredit,
                balance,
                balanceType = isSupplier ? "Payable" : "Receivable",
                status      = balance <= 0 ? "Cleared" : "Outstanding"
            });
        }

        // ── Pending invoices for a party (unpaid / partially paid) ──────────
        [HttpGet]
        public async Task<IActionResult> GetPendingInvoices(int partyId)
        {
            var party = await _context.Parties.FindAsync(partyId);
            if (party == null) return Json(new { success = true, data = Array.Empty<object>() });

            bool isSupplier = party.PartyType is "supplier" or "both";

            if (isSupplier)
            {
                var invoices = await _context.PurchaseInvoice
                    .Where(p => p.VendorID == partyId.ToString())
                    .OrderByDescending(p => p.PurchaseDate)
                    .Select(p => new
                    {
                        id            = p.PurchaseId,
                        date          = p.PurchaseDate,
                        refNo         = p.BillNo ?? "-",
                        invoiceAmount = p.AmountPaid ?? 0,
                        paidSoFar     = _context.AccountLedger
                            .Where(l => l.PartyId == partyId
                                     && l.TransactionType == TransactionTypes.Payment
                                     && l.ReferenceId != null
                                     && _context.PaymentVoucher
                                            .Where(v => v.PurchaseId == p.PurchaseId)
                                            .Select(v => (int?)v.VoucherId)
                                            .Contains(l.ReferenceId)
                                     && !l.IsVoid)
                            .Sum(l => (decimal?)l.Debit) ?? 0
                    })
                    .ToListAsync();

                var pending = invoices
                    .Select(i => new
                    {
                        i.id,
                        date      = i.date.HasValue ? i.date.Value.ToString("yyyy-MM-dd") : "-",
                        label     = $"Purchase #{i.id}  ({(i.date.HasValue ? i.date.Value.ToString("yyyy-MM-dd") : "-")})  — ${(i.invoiceAmount - i.paidSoFar):F2} remaining",
                        i.invoiceAmount,
                        i.paidSoFar,
                        remaining = i.invoiceAmount - i.paidSoFar
                    })
                    .Where(i => i.remaining > 0.01m)
                    .ToList();

                return Json(new { success = true, data = pending });
            }
            else
            {
                var invoices = await _context.SaleInvoice
                    .Where(s => s.CustomerID == partyId.ToString())
                    .OrderByDescending(s => s.SaleDate)
                    .Select(s => new
                    {
                        id            = s.SaleId,
                        date          = s.SaleDate,
                        refNo         = s.RefNo ?? "-",
                        invoiceAmount = s.NetAmount ?? 0,
                        paidSoFar     = _context.AccountLedger
                            .Where(l => l.PartyId == partyId
                                     && l.TransactionType == TransactionTypes.Receipt
                                     && l.ReferenceId != null
                                     && _context.PaymentVoucher
                                            .Where(v => v.SaleId == s.SaleId)
                                            .Select(v => (int?)v.VoucherId)
                                            .Contains(l.ReferenceId)
                                     && !l.IsVoid)
                            .Sum(l => (decimal?)l.Credit) ?? 0
                    })
                    .ToListAsync();

                var pending = invoices
                    .Select(i => new
                    {
                        i.id,
                        date      = i.date.HasValue ? i.date.Value.ToString("yyyy-MM-dd") : "-",
                        label     = $"Sale #{i.id}  ({(i.date.HasValue ? i.date.Value.ToString("yyyy-MM-dd") : "-")})  — ${(i.invoiceAmount - i.paidSoFar):F2} remaining",
                        i.invoiceAmount,
                        i.paidSoFar,
                        remaining = i.invoiceAmount - i.paidSoFar
                    })
                    .Where(i => i.remaining > 0.01m)
                    .ToList();

                return Json(new { success = true, data = pending });
            }
        }

        // ── Full ledger for a party with running balance ─────────────────────
        [HttpGet]
        public async Task<IActionResult> GetLedger(int partyId, DateTime? from = null, DateTime? to = null)
        {
            var party = await _context.Parties.FindAsync(partyId);
            if (party == null) return Json(new { success = false, message = "Party not found." });

            var fromDate = from ?? DateTime.MinValue;
            var toDate   = to.HasValue ? to.Value.Date.AddDays(1).AddTicks(-1) : DateTime.MaxValue;

            bool isSupplier = party.PartyType is "supplier" or "both";

            var entries = await _context.AccountLedger
                .Where(l => l.PartyId == partyId && !l.IsVoid
                         && l.EntryDate >= fromDate && l.EntryDate <= toDate)
                .OrderBy(l => l.EntryDate)
                .ThenBy(l => l.LedgerId)
                .ToListAsync();

            decimal runningBalance = 0;
            var rows = entries.Select(e =>
            {
                runningBalance += isSupplier
                    ? e.Credit - e.Debit      // supplier: credit=owed, debit=paid
                    : e.Debit  - e.Credit;    // customer: debit=owed, credit=received
                return new
                {
                    date            = e.EntryDate.ToString("yyyy-MM-dd"),
                    description     = e.Description,
                    transactionType = e.TransactionType,
                    debit           = e.Debit,
                    credit          = e.Credit,
                    balance         = runningBalance,
                    refNo           = $"{e.TransactionType[..3].ToUpper()}-{e.ReferenceId}"
                };
            }).ToList();

            return Json(new
            {
                success  = true,
                data     = rows,
                summary  = new
                {
                    totalDebit  = entries.Sum(e => e.Debit),
                    totalCredit = entries.Sum(e => e.Credit),
                    balance     = runningBalance,
                    balanceType = isSupplier ? "Payable" : "Receivable"
                }
            });
        }

        // ── Save payment / receipt voucher ───────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> SaveVoucher([FromBody] PaymentVoucherViewModel model)
        {
            if (model.PartyId == 0 || model.Amount <= 0)
                return Ok(new { success = false, message = "Party and a valid amount are required." });

            var party = await _context.Parties.FindAsync(model.PartyId);
            if (party == null) return Ok(new { success = false, message = "Party not found." });

            // Overpayment guard
            var balanceResult = await GetPartyBalance(model.PartyId) as JsonResult;
            // (simple re-compute inline)
            var entries     = await _context.AccountLedger.Where(l => l.PartyId == model.PartyId && !l.IsVoid).ToListAsync();
            bool isSupplier = party.PartyType is "supplier" or "both";
            var balance     = isSupplier
                ? entries.Sum(e => e.Credit) - entries.Sum(e => e.Debit)
                : entries.Sum(e => e.Debit)  - entries.Sum(e => e.Credit);

            if (model.Amount > balance && balance > 0)
                return Ok(new
                {
                    success = false,
                    message = $"Amount ${model.Amount:F2} exceeds outstanding balance of ${balance:F2}."
                });

            var voucher = new PaymentVoucher
            {
                PartyId     = model.PartyId,
                VoucherType = model.VoucherType,
                VoucherDate = model.VoucherDate == default ? DateTime.Now : model.VoucherDate,
                Amount      = model.Amount,
                PaymentMode = model.PaymentMode,
                ReferenceNo = model.ReferenceNo,
                PurchaseId  = model.PurchaseId,
                SaleId      = model.SaleId,
                BranchId    = model.BranchId,
                Notes       = model.Notes,
                CreatedDate = DateTime.Now
            };

            _context.PaymentVoucher.Add(voucher);
            await _context.SaveChangesAsync();

            // Create corresponding ledger entry
            bool isPayment = model.VoucherType == "Payment";
            _context.AccountLedger.Add(new AccountLedger
            {
                PartyId         = model.PartyId,
                EntryDate       = voucher.VoucherDate,
                TransactionType = isPayment ? TransactionTypes.Payment : TransactionTypes.Receipt,
                ReferenceId     = voucher.VoucherId,
                Description     = isPayment
                    ? $"Payment Voucher #{voucher.VoucherId}" +
                      (model.PurchaseId.HasValue ? $" | Invoice #{model.PurchaseId}" : "")
                    : $"Receipt Voucher #{voucher.VoucherId}" +
                      (model.SaleId.HasValue ? $" | Sale #{model.SaleId}" : ""),
                Debit           = isPayment ? model.Amount : 0,
                Credit          = isPayment ? 0 : model.Amount,
                BranchId        = model.BranchId,
                CreatedDate     = DateTime.Now
            });

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = $"{model.VoucherType} recorded successfully.", id = voucher.VoucherId });
        }

        // ── Void (soft-delete) a voucher ─────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> VoidVoucher([FromBody] int voucherId)
        {
            var voucher = await _context.PaymentVoucher.FindAsync(voucherId);
            if (voucher == null) return Ok(new { success = false, message = "Voucher not found." });

            voucher.IsVoid = true;

            // Also void the corresponding ledger entry
            var ledgerEntry = await _context.AccountLedger
                .FirstOrDefaultAsync(l => l.ReferenceId == voucherId
                    && (l.TransactionType == TransactionTypes.Payment
                     || l.TransactionType == TransactionTypes.Receipt)
                    && !l.IsVoid);

            if (ledgerEntry != null) ledgerEntry.IsVoid = true;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Voucher voided." });
        }
    }
}