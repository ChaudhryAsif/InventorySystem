using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class AccountsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IAccountService _accounts;

        public AccountsController(ApplicationDbContext db, IAccountService accounts)
        {
            _db = db;
            _accounts = accounts;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CHART OF ACCOUNTS
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult ChartOfAccounts() => View();

        [HttpGet]
        public async Task<IActionResult> GetChartOfAccounts()
        {
            var list = await _accounts.GetChartOfAccountsAsync();
            return Json(new
            {
                success = true,
                data = list.Select(a => new {
                    a.AccountHeadId,
                    a.AccountCode,
                    a.AccountName,
                    a.AccountType,
                    a.Level,
                    a.NormalBalance,
                    a.IsPostable,
                    a.IsSystem,
                    a.IsActive,
                    a.OpeningBalance,
                    a.OpeningBalanceType,
                    a.ParentId,
                    parentName = a.Parent?.AccountName ?? ""
                })
            });
        }

        [HttpPost]
        public async Task<IActionResult> SaveAccountHead([FromBody] AccountHead model)
        {
            var result = await _accounts.SaveAccountHeadAsync(model);
            return Json(new { result.success, result.message });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAccountHead([FromBody] int id)
        {
            var result = await _accounts.DeleteAccountHeadAsync(id);
            return Json(new { result.success, result.message });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // VOUCHERS
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet] public IActionResult PaymentVoucher() => View("Voucher", "PV");
        [HttpGet] public IActionResult ReceiptVoucher() => View("Voucher", "RV");
        [HttpGet] public IActionResult JournalVoucher() => View("Voucher", "JV");
        [HttpGet] public IActionResult ContraVoucher() => View("Voucher", "CV");
        [HttpGet] public IActionResult VoucherList() => View();

        [HttpGet]
        public async Task<IActionResult> GetVouchers(string? type, DateTime? from, DateTime? to)
        {
            var list = await _accounts.GetVouchersAsync(type, from, to);
            return Json(new
            {
                success = true,
                data = list.Select(v => new {
                    v.VoucherId,
                    v.VoucherNo,
                    v.VoucherType,
                    v.TotalAmount,
                    voucherDate = v.VoucherDate.ToString("yyyy-MM-dd"),
                    partyName = v.Party?.PartyName ?? "-",
                    v.PaymentMode,
                    v.ReferenceNo,
                    v.Narration,
                    v.Status
                })
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetVoucher(int id)
        {
            var v = await _accounts.GetVoucherAsync(id);
            if (v == null) return Json(new { success = false, message = "Not found." });
            return Json(new { success = true, data = v });
        }

        [HttpPost]
        public async Task<IActionResult> SaveVoucher([FromBody] Voucher model)
        {
            model.CreatedBy = User.Identity?.Name ?? "system";
            var result = await _accounts.SaveVoucherAsync(model);
            return Json(new { result.success, result.message, result.voucherId });
        }

        [HttpPost]
        public async Task<IActionResult> VoidVoucher([FromBody] int id)
        {
            var result = await _accounts.VoidVoucherAsync(id);
            return Json(new { result.success, result.message });
        }

        [HttpGet]
        public async Task<IActionResult> GetNextVoucherNo(string type)
        {
            var no = await _accounts.GenerateVoucherNoAsync(type);
            return Json(new { success = true, voucherNo = no });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // LEDGER VIEW
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet] public IActionResult GeneralLedgerView() => View();
        [HttpGet] public IActionResult PartyLedger() => View();

        [HttpGet]
        public async Task<IActionResult> GetGeneralLedger(int accountHeadId, DateTime? from, DateTime? to)
        {
            var entries = await _accounts.GetAccountLedgerAsync(accountHeadId, from, to);
            decimal running = 0;
            var rows = entries.Select(e => {
                running += e.Debit - e.Credit;
                return new
                {
                    date = e.VoucherDate.ToString("yyyy-MM-dd"),
                    e.VoucherNo,
                    e.VoucherType,
                    narration = e.Narration,
                    partyName = e.Party?.PartyName ?? "",
                    e.Debit,
                    e.Credit,
                    balance = running
                };
            });
            return Json(new
            {
                success = true,
                data = rows,
                summary = new { totalDebit = entries.Sum(e => e.Debit), totalCredit = entries.Sum(e => e.Credit), closing = running }
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetPartyStatement(int partyId, DateTime? from, DateTime? to)
        {
            var rows = await _accounts.GetPartyStatementAsync(partyId, from, to);
            return Json(new { success = true, data = rows });
        }

        // ── Party Ledger (used by PartyLedger.cshtml) ─────────────────
        [HttpGet]
        public async Task<IActionResult> GetLedger(int partyId, DateTime? from = null, DateTime? to = null)
        {
            var party = await _db.Parties.FindAsync(partyId);
            if (party == null) return Json(new { success = false, message = "Party not found." });

            var fromDate = from ?? DateTime.MinValue;
            var toDate   = to.HasValue ? to.Value.Date.AddDays(1).AddTicks(-1) : DateTime.MaxValue;

            bool isSupplier = party.PartyType is "supplier" or "both";

            var entries = await _db.GeneralLedger
                .Where(l => l.PartyId == partyId && !l.IsVoid
                         && l.VoucherDate >= fromDate && l.VoucherDate <= toDate)
                .OrderBy(l => l.VoucherDate)
                .ThenBy(l => l.GLId)
                .ToListAsync();

            decimal runningBalance = 0;
            var rows = entries.Select(e =>
            {
                runningBalance += isSupplier
                    ? e.Credit - e.Debit
                    : e.Debit  - e.Credit;
                return new
                {
                    date            = e.VoucherDate.ToString("yyyy-MM-dd"),
                    description     = e.Narration ?? e.VoucherType,
                    transactionType = e.VoucherType,
                    debit           = e.Debit,
                    credit          = e.Credit,
                    balance         = runningBalance,
                    refNo           = e.VoucherNo
                };
            }).ToList();

            var totalDebit  = entries.Sum(e => e.Debit);
            var totalCredit = entries.Sum(e => e.Credit);

            return Json(new
            {
                success = true,
                data    = rows,
                summary = new
                {
                    totalDebit,
                    totalCredit,
                    balance     = runningBalance,
                    balanceType = isSupplier ? "Payable" : "Receivable",
                    status      = runningBalance <= 0 ? "Cleared" : "Outstanding",
                    partyName   = party.PartyName,
                    partyType   = party.PartyType
                }
            });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // REPORTS
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet] public IActionResult TrialBalance() => View();
        [HttpGet] public IActionResult ProfitAndLoss() => View();
        [HttpGet] public IActionResult BalanceSheet() => View();

        [HttpGet]
        public async Task<IActionResult> GetTrialBalance(DateTime? asOf)
        {
            var result = await _accounts.GetTrialBalanceAsync(asOf ?? DateTime.Today);
            return Json(new { success = true, data = result });
        }

        [HttpGet]
        public async Task<IActionResult> GetProfitLoss(DateTime? from, DateTime? to)
        {
            var result = await _accounts.GetProfitLossAsync(
                from ?? new DateTime(DateTime.Today.Year, 1, 1),
                to ?? DateTime.Today);
            return Json(new { success = true, data = result });
        }

        [HttpGet]
        public async Task<IActionResult> GetBalanceSheet(DateTime? asOf)
        {
            var result = await _accounts.GetBalanceSheetAsync(asOf ?? DateTime.Today);
            return Json(new { success = true, data = result });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // HELPERS (kept from original)
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> GetParties(string? type = null)
        {
            var query = _db.Parties.AsQueryable();
            if (!string.IsNullOrEmpty(type))
                query = query.Where(p => p.PartyType == type || p.PartyType == "both");

            var parties = await query
                .Where(p => p.Status != "inactive")
                .Select(p => new { value = p.PartyId, text = p.PartyName, p.PartyType, phone = p.Phone ?? "" })
                .OrderBy(p => p.text)
                .ToListAsync();

            return Json(new { success = true, data = parties });
        }

        [HttpGet]
        public async Task<IActionResult> GetPostableAccounts()
        {
            var accounts = await _db.AccountHeads
                .Where(a => a.Level == 3 && a.IsActive)
                .OrderBy(a => a.AccountCode)
                .Select(a => new { a.AccountHeadId, a.AccountCode, a.AccountName, a.AccountType })
                .ToListAsync();
            return Json(new { success = true, data = accounts });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CASH BOOK
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet] public IActionResult CashBook() => View();

        [HttpGet]
        public async Task<IActionResult> GetCashBook(DateTime? from, DateTime? to)
        {
            var result = await _accounts.GetCashBookAsync(
                from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
                to ?? DateTime.Today);
            return Json(new { success = true, data = result });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // AGING REPORT
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet] public IActionResult AgingReport() => View();

        [HttpGet]
        public async Task<IActionResult> GetAgingReport(string partyType = "all", DateTime? asOf = null)
        {
            var result = await _accounts.GetAgingReportAsync(partyType, asOf ?? DateTime.Today);
            return Json(new { success = true, data = result });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CASH FLOW STATEMENT
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet] public IActionResult CashFlow() => View();

        [HttpGet]
        public async Task<IActionResult> GetCashFlow(DateTime? from, DateTime? to)
        {
            var result = await _accounts.GetCashFlowAsync(
                from ?? new DateTime(DateTime.Today.Year, 1, 1),
                to ?? DateTime.Today);
            return Json(new { success = true, data = result });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // OUTSTANDING REPORT
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet] public IActionResult OutstandingReport() => View();

        [HttpGet]
        public async Task<IActionResult> GetOutstandingReport(string partyType = "all")
        {
            var rows = await _accounts.GetOutstandingReportAsync(partyType);
            return Json(new { success = true, data = rows });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PRINT VOUCHER
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> PrintVoucher(int id)
        {
            var voucher = await _accounts.GetVoucherAsync(id);
            if (voucher == null) return NotFound("Voucher not found.");
            return View(voucher);
        }


        // ═══════════════════════════════════════════════════════════════════════
        // DAY BOOK
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet] public IActionResult DayBook() => View();

        [HttpGet]
        public async Task<IActionResult> GetDayBook(DateTime? from, DateTime? to)
        {
            var result = await _accounts.GetDayBookAsync(
                from ?? DateTime.Today,
                to ?? DateTime.Today);
            return Json(new { success = true, data = result });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // BANK RECONCILIATION
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet] public IActionResult BankReconciliation() => View();

        [HttpGet]
        public async Task<IActionResult> GetBankRecon(int accountHeadId, DateTime? from, DateTime? to)
        {
            var result = await _accounts.GetBankReconAsync(accountHeadId, from, to);
            return Json(new { success = true, data = result });
        }

        [HttpPost]
        public async Task<IActionResult> MarkCleared([FromBody] MarkClearedRequest req)
        {
            var result = await _accounts.MarkClearedAsync(req.GlId, req.Cleared);
            return Json(new { result.success, result.message });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // DEBIT NOTE / CREDIT NOTE  (re-use Voucher view with DN / CN type)
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet] public IActionResult DebitNote() => View("Voucher", "DN");
        [HttpGet] public IActionResult CreditNote() => View("Voucher", "CN");

        // ═══════════════════════════════════════════════════════════════════════
        // OPENING BALANCE
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet] public IActionResult OpeningBalance() => View();

        [HttpPost]
        public async Task<IActionResult> SaveOpeningBalances([FromBody] List<AccountHead> accounts)
        {
            var result = await _accounts.SaveOpeningBalancesAsync(accounts);
            return Json(new { result.success, result.message });
        }
    }

    public class MarkClearedRequest
    {
        public long GlId { get; set; }
        public bool Cleared { get; set; }
    }
}