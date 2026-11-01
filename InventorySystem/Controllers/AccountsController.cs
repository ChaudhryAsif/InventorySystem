using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    [Authorize]
    public partial class AccountsController : Controller
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

        [HttpGet] public IActionResult PaymentVoucher() => View("Settlement", "Payment");
        [HttpGet] public IActionResult ReceiptVoucher() => View("Settlement", "Receipt");
        [HttpGet] public IActionResult JournalVoucher() => View("Voucher", "JV");
        [HttpGet] public IActionResult ContraVoucher() => View("Voucher", "CV");
        [HttpGet] public IActionResult VoucherList() => View();

        [HttpGet]
        public async Task<IActionResult> GetVouchers(string? type, DateTime? from, DateTime? to, int page = 1, int pageSize = 200)
        {
            var list = await _accounts.GetVouchersAsync(type, from, to);
            var totalCount = list.Count;
            var paged = list.Skip((page - 1) * pageSize).Take(pageSize);
            return Json(new
            {
                success = true,
                totalCount,
                page,
                pageSize,
                data = paged.Select(v => new {
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
            // Return the screen's fields, not EF navigation graphs with back references.
            return Json(new { success = true, data = new {
                v.VoucherId, v.VoucherNo, v.VoucherType, v.VoucherDate, v.TotalAmount,
                v.PartyId, partyName = v.Party?.PartyName, v.PaymentMode, v.ReferenceNo,
                v.Narration, v.Status, v.BranchId, v.IsVoid, v.CreatedBy, v.CreatedDate,
                details = v.Details.Select(d => new {
                    d.VoucherDetailId, d.VoucherId, d.AccountHeadId,
                    accountCode = d.AccountHead?.AccountCode, accountName = d.AccountHead?.AccountName,
                    d.PartyId, partyName = d.Party?.PartyName, d.Debit, d.Credit, d.Narration
                })
            } });
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

        [HttpPost]
        public async Task<IActionResult> ApproveVoucher([FromBody] int id)
        {
            var result = await _accounts.ApproveVoucherAsync(id);
            return Json(new { result.success, result.message });
        }

        [HttpPost]
        public async Task<IActionResult> PostVoucher([FromBody] int id)
        {
            var result = await _accounts.PostVoucherAsync(id);
            return Json(new { result.success, result.message });
        }

        [HttpGet]
        public async Task<IActionResult> GetNextVoucherNo(string type, DateTime? date = null)
        {
            var no = await _accounts.GenerateVoucherNoAsync(type, date ?? DateTime.Today);
            return Json(new { success = true, voucherNo = no });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // LEDGER VIEW
        // ═══════════════════════════════════════════════════════════════════════

        [HttpGet] public IActionResult GeneralLedgerView() => View();
        [HttpGet] public IActionResult PartyLedger() => View();

        [HttpGet]
        public async Task<IActionResult> GetGeneralLedger(int accountHeadId, DateTime? from, DateTime? to, int page = 1, int pageSize = 200)
        {
            var entries = await _accounts.GetAccountLedgerAsync(accountHeadId, from, to);
            decimal running = 0;
            // Running balance is computed over the FULL set first (order matters), then the page is sliced off.
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
            }).ToList();

            var totalCount = rows.Count;
            var pagedRows = rows.Skip((page - 1) * pageSize).Take(pageSize);

            return Json(new
            {
                success = true,
                totalCount,
                page,
                pageSize,
                data = pagedRows,
                summary = new { totalDebit = entries.Sum(e => e.Debit), totalCredit = entries.Sum(e => e.Credit), closing = running }
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetPartyStatement(int partyId, DateTime? from, DateTime? to)
        {
            var rows = await _accounts.GetPartyStatementAsync(partyId, from, to);
            return Json(new { success = true, data = rows });
        }

        [HttpPost]
        public async Task<IActionResult> SavePayment([FromBody] InventorySystem.Models.PartyPaymentRequest model)
        {
            if (model.Direction is not ("Receipt" or "Payment") || model.Mode is not ("Cash" or "Bank Transfer") || model.Amount <= 0 || model.Amount > 100000000 || model.Amount != decimal.Round(model.Amount, 2) || model.Reference?.Length > 100 || model.Date.Year < 2000 || model.Date.Date > DateTime.Today.AddDays(1))
                return BadRequest(new { success = false, message = "Enter a valid payment, amount and date." });
            var party = await _db.Parties.SingleOrDefaultAsync(p => p.PartyId == model.PartyId && p.Status != "inactive");
            if (party == null || party.PartyType is not ("supplier" or "customer" or "both"))
                return BadRequest(new { success = false, message = "Select an active customer or supplier for this payment." });
            var statement = await _accounts.GetPartyStatementAsync(party.PartyId, null, null);
            var balance = statement.LastOrDefault()?.Balance ?? 0;
            var position = PartyBalanceService.Position(party.PartyType, balance);
            if (position.Amount == 0 || model.Amount > position.Amount)
                return BadRequest(new { success = false, message = "Amount exceeds the outstanding payment or refund. Use an administrator voucher for an advance." });
            if ((model.Direction == "Receipt") != (position.BalanceType == "Receivable"))
                return BadRequest(new { success = false, message = position.BalanceType == "Receivable" ? "Use Receive Money for this balance." : "Use Pay Money for this balance." });
            var cash = await _db.AccountHeads.SingleAsync(a => a.AccountCode == (model.Mode == "Cash" ? "1101" : "1102"));
            var control = await _db.AccountHeads.SingleAsync(a => a.AccountCode == position.ControlCode);
            var receipt = model.Direction == "Receipt";
            var voucher = new Voucher { VoucherType = receipt ? "RV" : "PV", VoucherDate = model.Date, PartyId = model.PartyId,
                PaymentMode = model.Mode, ReferenceNo = model.Reference, BranchId = model.BranchId,
                Narration = (position.IsRefund ? receipt ? "Supplier refund received" : "Customer refund paid" : receipt ? "Customer collection" : "Supplier payment") + (string.IsNullOrWhiteSpace(model.Reference) ? "" : " | " + model.Reference),
                CreatedBy = User.Identity?.Name,
                Details = [new() { AccountHeadId = cash.AccountHeadId, Debit = receipt ? model.Amount : 0, Credit = receipt ? 0 : model.Amount },
                    new() { AccountHeadId = control.AccountHeadId, PartyId = model.PartyId, Debit = receipt ? 0 : model.Amount, Credit = receipt ? model.Amount : 0 }] };
            var result = await _accounts.SaveVoucherAsync(voucher);
            return result.success ? Ok(new { success = true, message = "Payment recorded.", id = result.voucherId })
                : BadRequest(new { success = false, message = result.message });
        }

        // ── Party Ledger (used by PartyLedger.cshtml) ─────────────────
        [HttpGet]
        public async Task<IActionResult> GetLedger(int partyId, DateTime? from = null, DateTime? to = null)
        {
            var party = await _db.Parties.FindAsync(partyId);
            if (party == null) return Json(new { success = false, message = "Party not found." });

            var fromDate = from ?? DateTime.MinValue;
            var toDate   = to.HasValue ? to.Value.Date.AddDays(1).AddTicks(-1) : DateTime.MaxValue;

            var entries = await _accounts.GetPartyStatementAsync(partyId, from, to);
            var runningBalance = entries.LastOrDefault()?.Balance ?? 0;
            var position = PartyBalanceService.Position(party.PartyType, runningBalance);
            var rows = entries.Select(e => new { date = e.Date.ToString("yyyy-MM-dd"), description = e.Description,
                transactionType = e.TransactionType, debit = e.Debit, credit = e.Credit, balance = e.Balance, refNo = e.VoucherNo }).ToList();
            var totalDebit = entries.Sum(e => e.Debit); var totalCredit = entries.Sum(e => e.Credit);

            return Json(new
            {
                success = true,
                data    = rows,
                summary = new
                {
                    totalDebit,
                    totalCredit,
                    balance     = position.Amount,
                    signedBalance = runningBalance,
                    balanceType = position.BalanceType,
                    isRefund = position.IsRefund,
                    balanceDescription = position.Description,
                    status = position.Amount == 0 ? "Cleared" : position.IsRefund ? "Refund due" : "Outstanding",
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
        public async Task<IActionResult> SaveOpeningBalances([FromBody] List<OpeningBalanceRequest> accounts)
        {
            var result = await _accounts.SaveOpeningBalancesAsync(accounts.Select(a => new AccountHead {
                AccountHeadId = a.AccountHeadId, OpeningBalance = a.OpeningBalance,
                OpeningBalanceType = a.OpeningBalanceType
            }).ToList());
            return Json(new { result.success, result.message });
        }
    }

    public class OpeningBalanceRequest
    {
        public int AccountHeadId { get; set; }
        public decimal OpeningBalance { get; set; }
        public string OpeningBalanceType { get; set; } = "Debit";
    }

    public class MarkClearedRequest
    {
        public long GlId { get; set; }
        public bool Cleared { get; set; }
    }
}