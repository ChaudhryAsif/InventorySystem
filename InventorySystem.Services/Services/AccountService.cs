using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Core.Services
{
    public class AccountService : IAccountService
    {
        private readonly ApplicationDbContext _db;

        public AccountService(ApplicationDbContext db)
        {
            _db = db;
        }

        // ── CHART OF ACCOUNTS ─────────────────────────────────────────────────

        public async Task<List<AccountHead>> GetChartOfAccountsAsync()
            => await _db.AccountHeads
                .Include(a => a.Parent)
                .OrderBy(a => a.AccountCode)
                .ToListAsync();

        public async Task<AccountHead?> GetAccountHeadAsync(int id)
            => await _db.AccountHeads.FindAsync(id);

        public async Task<(bool success, string message)> SaveAccountHeadAsync(AccountHead model)
        {
            try
            {
                // Code uniqueness check
                bool codeExists = await _db.AccountHeads
                    .AnyAsync(a => a.AccountCode == model.AccountCode && a.AccountHeadId != model.AccountHeadId);
                if (codeExists)
                    return (false, $"Account Code '{model.AccountCode}' already exists.");

                if (model.AccountHeadId == 0)
                {
                    model.CreatedDate = DateTime.Now;
                    _db.AccountHeads.Add(model);
                }
                else
                {
                    var existing = await _db.AccountHeads.FindAsync(model.AccountHeadId);
                    if (existing == null) return (false, "Account not found.");
                    if (existing.IsSystem && existing.AccountCode != model.AccountCode)
                        return (false, "Cannot change code of a system account.");

                    existing.AccountName        = model.AccountName;
                    existing.AccountType        = model.AccountType;
                    existing.ParentId           = model.ParentId;
                    existing.Level              = model.Level;
                    existing.NormalBalance      = model.NormalBalance;
                    existing.OpeningBalance     = model.OpeningBalance;
                    existing.OpeningBalanceType = model.OpeningBalanceType;
                    existing.Description        = model.Description;
                    existing.IsActive           = model.IsActive;
                }

                await _db.SaveChangesAsync();
                return (true, "Account saved successfully.");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> DeleteAccountHeadAsync(int id)
        {
            var account = await _db.AccountHeads.FindAsync(id);
            if (account == null) return (false, "Account not found.");
            if (account.IsSystem) return (false, "System accounts cannot be deleted.");

            bool hasEntries = await _db.GeneralLedger.AnyAsync(g => g.AccountHeadId == id && !g.IsVoid);
            if (hasEntries) return (false, "Cannot delete account with posted ledger entries.");

            bool hasChildren = await _db.AccountHeads.AnyAsync(a => a.ParentId == id);
            if (hasChildren) return (false, "Cannot delete account that has sub-accounts.");

            _db.AccountHeads.Remove(account);
            await _db.SaveChangesAsync();
            return (true, "Account deleted.");
        }

        // ── VOUCHERS ──────────────────────────────────────────────────────────

        public async Task<List<Voucher>> GetVouchersAsync(string? type, DateTime? from, DateTime? to)
        {
            var query = _db.Vouchers
                .Include(v => v.Party)
                .Where(v => !v.IsVoid)
                .AsQueryable();

            if (!string.IsNullOrEmpty(type))
                query = query.Where(v => v.VoucherType == type);
            if (from.HasValue)
                query = query.Where(v => v.VoucherDate >= from.Value);
            if (to.HasValue)
                query = query.Where(v => v.VoucherDate <= to.Value.Date.AddDays(1).AddTicks(-1));

            return await query.OrderByDescending(v => v.VoucherDate).ToListAsync();
        }

        public async Task<Voucher?> GetVoucherAsync(int id)
            => await _db.Vouchers
                .Include(v => v.Party)
                .Include(v => v.Details).ThenInclude(d => d.AccountHead)
                .Include(v => v.Details).ThenInclude(d => d.Party)
                .FirstOrDefaultAsync(v => v.VoucherId == id);

        public async Task<(bool success, string message, int? voucherId)> SaveVoucherAsync(Voucher voucher)
        {
            // Validate double-entry balance
            var totalDebit  = voucher.Details.Sum(d => d.Debit);
            var totalCredit = voucher.Details.Sum(d => d.Credit);
            if (totalDebit != totalCredit)
                return (false, $"Voucher not balanced. Debit {totalDebit:F2} ≠ Credit {totalCredit:F2}.", null);

            if (!voucher.Details.Any())
                return (false, "Voucher must have at least one line.", null);

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                if (voucher.VoucherId == 0)
                {
                    voucher.VoucherNo   = await GenerateVoucherNoAsync(voucher.VoucherType);
                    voucher.CreatedDate = DateTime.Now;
                    voucher.TotalAmount = totalDebit;
                    _db.Vouchers.Add(voucher);
                    await _db.SaveChangesAsync();
                }
                else
                {
                    // Void old GL entries, re-post
                    var oldGL = await _db.GeneralLedger
                        .Where(g => g.VoucherId == voucher.VoucherId && !g.IsVoid)
                        .ToListAsync();
                    oldGL.ForEach(g => g.IsVoid = true);
                    voucher.TotalAmount = totalDebit;
                    _db.Vouchers.Update(voucher);
                    await _db.SaveChangesAsync();
                }

                // Post to General Ledger
                foreach (var detail in voucher.Details)
                {
                    _db.GeneralLedger.Add(new GeneralLedger
                    {
                        VoucherNo      = voucher.VoucherNo,
                        VoucherType    = voucher.VoucherType,
                        VoucherDate    = voucher.VoucherDate,
                        AccountHeadId  = detail.AccountHeadId,
                        PartyId        = detail.PartyId,
                        Debit          = detail.Debit,
                        Credit         = detail.Credit,
                        Narration      = detail.Narration ?? voucher.Narration,
                        VoucherId      = voucher.VoucherId,
                        BranchId       = voucher.BranchId,
                        CreatedBy      = voucher.CreatedBy,
                        CreatedDate    = DateTime.Now
                    });
                }
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return (true, $"Voucher {voucher.VoucherNo} saved.", voucher.VoucherId);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return (false, $"Error: {ex.Message}", null);
            }
        }

        public async Task<(bool success, string message)> VoidVoucherAsync(int voucherId)
        {
            var voucher = await _db.Vouchers.FindAsync(voucherId);
            if (voucher == null) return (false, "Voucher not found.");
            if (voucher.IsVoid)  return (false, "Voucher is already void.");

            voucher.IsVoid  = true;
            voucher.Status  = "Void";

            var glEntries = await _db.GeneralLedger
                .Where(g => g.VoucherId == voucherId && !g.IsVoid)
                .ToListAsync();
            glEntries.ForEach(g => g.IsVoid = true);

            await _db.SaveChangesAsync();
            return (true, $"Voucher {voucher.VoucherNo} voided.");
        }

        // ── LEDGER ────────────────────────────────────────────────────────────

        public async Task<List<GeneralLedger>> GetAccountLedgerAsync(int accountHeadId, DateTime? from, DateTime? to)
        {
            var query = _db.GeneralLedger
                .Include(g => g.Party)
                .Where(g => g.AccountHeadId == accountHeadId && !g.IsVoid)
                .AsQueryable();

            if (from.HasValue) query = query.Where(g => g.VoucherDate >= from.Value);
            if (to.HasValue)   query = query.Where(g => g.VoucherDate <= to.Value.Date.AddDays(1).AddTicks(-1));

            return await query.OrderBy(g => g.VoucherDate).ThenBy(g => g.GLId).ToListAsync();
        }

        // ── REPORTS ───────────────────────────────────────────────────────────

        public async Task<TrialBalanceResult> GetTrialBalanceAsync(DateTime asOf)
        {
            var accounts = await _db.AccountHeads
                .Where(a => a.Level == 3 && a.IsActive)
                .OrderBy(a => a.AccountCode)
                .ToListAsync();

            var glData = await _db.GeneralLedger
                .Where(g => !g.IsVoid && g.VoucherDate <= asOf)
                .GroupBy(g => g.AccountHeadId)
                .Select(g => new
                {
                    AccountHeadId = g.Key,
                    TotalDebit    = g.Sum(x => x.Debit),
                    TotalCredit   = g.Sum(x => x.Credit)
                })
                .ToListAsync();

            var rows = accounts.Select(a =>
            {
                var gl         = glData.FirstOrDefault(g => g.AccountHeadId == a.AccountHeadId);
                var glDebit    = gl?.TotalDebit  ?? 0;
                var glCredit   = gl?.TotalCredit ?? 0;
                var openDebit  = a.OpeningBalanceType == "Debit"  ? a.OpeningBalance : 0;
                var openCredit = a.OpeningBalanceType == "Credit" ? a.OpeningBalance : 0;

                var closingDebit  = openDebit  + glDebit;
                var closingCredit = openCredit + glCredit;

                // Net out for trial balance display
                if (closingDebit >= closingCredit)
                    return new TrialBalanceRow
                    {
                        AccountHeadId = a.AccountHeadId,
                        AccountCode   = a.AccountCode,
                        AccountName   = a.AccountName,
                        AccountType   = a.AccountType,
                        OpeningDebit  = openDebit,
                        OpeningCredit = openCredit,
                        PeriodDebit   = glDebit,
                        PeriodCredit  = glCredit,
                        ClosingDebit  = closingDebit - closingCredit,
                        ClosingCredit = 0
                    };
                else
                    return new TrialBalanceRow
                    {
                        AccountHeadId = a.AccountHeadId,
                        AccountCode   = a.AccountCode,
                        AccountName   = a.AccountName,
                        AccountType   = a.AccountType,
                        OpeningDebit  = openDebit,
                        OpeningCredit = openCredit,
                        PeriodDebit   = glDebit,
                        PeriodCredit  = glCredit,
                        ClosingDebit  = 0,
                        ClosingCredit = closingCredit - closingDebit
                    };
            }).Where(r => r.ClosingDebit != 0 || r.ClosingCredit != 0).ToList();

            return new TrialBalanceResult { AsOf = asOf, Rows = rows };
        }

        public async Task<ProfitLossResult> GetProfitLossAsync(DateTime from, DateTime to)
        {
            var glData = await _db.GeneralLedger
                .Include(g => g.AccountHead)
                .Where(g => !g.IsVoid && g.VoucherDate >= from && g.VoucherDate <= to.Date.AddDays(1).AddTicks(-1))
                .GroupBy(g => new { g.AccountHeadId, g.AccountHead!.AccountName, g.AccountHead.AccountCode, g.AccountHead.AccountType, g.AccountHead.NormalBalance })
                .Select(g => new
                {
                    g.Key.AccountHeadId,
                    g.Key.AccountName,
                    g.Key.AccountCode,
                    g.Key.AccountType,
                    g.Key.NormalBalance,
                    Debit  = g.Sum(x => x.Debit),
                    Credit = g.Sum(x => x.Credit)
                })
                .ToListAsync();

            var incomeRows = glData
                .Where(g => g.AccountType == "Income")
                .Select(g => new ProfitLossRow
                {
                    AccountCode = g.AccountCode,
                    AccountName = g.AccountName,
                    Amount      = g.NormalBalance == "Credit" ? g.Credit - g.Debit : g.Debit - g.Credit
                }).ToList();

            var expenseRows = glData
                .Where(g => g.AccountType == "Expenses")
                .Select(g => new ProfitLossRow
                {
                    AccountCode = g.AccountCode,
                    AccountName = g.AccountName,
                    Amount      = g.NormalBalance == "Debit" ? g.Debit - g.Credit : g.Credit - g.Debit
                }).ToList();

            return new ProfitLossResult { From = from, To = to, IncomeRows = incomeRows, ExpenseRows = expenseRows };
        }

        public async Task<BalanceSheetResult> GetBalanceSheetAsync(DateTime asOf)
        {
            var trialBalance = await GetTrialBalanceAsync(asOf);

            var netProfit = (await GetProfitLossAsync(new DateTime(asOf.Year, 1, 1), asOf)).NetProfit;

            var assets = trialBalance.Rows
                .Where(r => r.AccountType == "Assets")
                .Select(r => new BalanceSheetRow { AccountCode = r.AccountCode, AccountName = r.AccountName, AccountType = r.AccountType, Balance = r.ClosingDebit - r.ClosingCredit })
                .ToList();

            var liabilities = trialBalance.Rows
                .Where(r => r.AccountType == "Liabilities")
                .Select(r => new BalanceSheetRow { AccountCode = r.AccountCode, AccountName = r.AccountName, AccountType = r.AccountType, Balance = r.ClosingCredit - r.ClosingDebit })
                .ToList();

            var equity = trialBalance.Rows
                .Where(r => r.AccountType == "Equity")
                .Select(r => new BalanceSheetRow { AccountCode = r.AccountCode, AccountName = r.AccountName, AccountType = r.AccountType, Balance = r.ClosingCredit - r.ClosingDebit })
                .ToList();

            // Add net profit to retained earnings in equity
            equity.Add(new BalanceSheetRow { AccountCode = "NET", AccountName = "Net Profit / (Loss)", AccountType = "Equity", Balance = netProfit });

            return new BalanceSheetResult { AsOf = asOf, AssetRows = assets, LiabilityRows = liabilities, EquityRows = equity };
        }

        public async Task<List<PartyStatementRow>> GetPartyStatementAsync(int partyId, DateTime? from, DateTime? to)
        {
            var query = _db.GeneralLedger
                .Where(g => g.PartyId == partyId && !g.IsVoid)
                .AsQueryable();

            if (from.HasValue) query = query.Where(g => g.VoucherDate >= from.Value);
            if (to.HasValue)   query = query.Where(g => g.VoucherDate <= to.Value.Date.AddDays(1).AddTicks(-1));

            var entries = await query.OrderBy(g => g.VoucherDate).ThenBy(g => g.GLId).ToListAsync();

            var party = await _db.Parties.FindAsync(partyId);
            bool isSupplier = party?.PartyType is "supplier" or "both";

            decimal running = 0;
            return entries.Select(e =>
            {
                running += isSupplier ? e.Credit - e.Debit : e.Debit - e.Credit;
                return new PartyStatementRow
                {
                    Date            = e.VoucherDate,
                    VoucherNo       = e.VoucherNo,
                    TransactionType = e.VoucherType,
                    Description     = e.Narration ?? "",
                    Debit           = e.Debit,
                    Credit          = e.Credit,
                    Balance         = running
                };
            }).ToList();
        }

        // ── VOUCHER NUMBER GENERATOR ──────────────────────────────────────────

        public async Task<string> GenerateVoucherNoAsync(string voucherType)
        {
            var prefix = voucherType switch
            {
                "PV" => "PV",
                "RV" => "RV",
                "JV" => "JV",
                "CV" => "CV",
                _    => "VCH"
            };

            var year  = DateTime.Now.Year;
            var count = await _db.Vouchers
                .CountAsync(v => v.VoucherType == voucherType
                              && v.VoucherDate.Year == year);

            return $"{prefix}-{year}-{(count + 1):D4}";
        }

        // ── CASH BOOK ─────────────────────────────────────────────────────────

        public async Task<CashBookResult> GetCashBookAsync(DateTime from, DateTime to)
        {
            // Identify Cash (1101) and Bank (1102) account head IDs
            var cashBankAccounts = await _db.AccountHeads
                .Where(a => a.AccountCode == "1101" || a.AccountCode == "1102")
                .ToListAsync();

            var cashBankIds = cashBankAccounts.Select(a => a.AccountHeadId).ToList();

            // Opening balance = account opening balances + all GL entries before 'from'
            decimal openingBalance = 0;
            foreach (var acct in cashBankAccounts)
            {
                var glBefore = await _db.GeneralLedger
                    .Where(g => !g.IsVoid && g.AccountHeadId == acct.AccountHeadId && g.VoucherDate < from)
                    .SumAsync(g => (decimal?)(g.Debit - g.Credit)) ?? 0;

                var openBal = acct.OpeningBalanceType == "Debit"
                    ? acct.OpeningBalance
                    : -acct.OpeningBalance;

                openingBalance += openBal + glBefore;
            }

            // Period entries on Cash / Bank accounts
            var entries = await _db.GeneralLedger
                .Include(g => g.Party)
                .Include(g => g.AccountHead)
                .Where(g => !g.IsVoid
                         && cashBankIds.Contains(g.AccountHeadId)
                         && g.VoucherDate >= from
                         && g.VoucherDate <= to.Date.AddDays(1).AddTicks(-1))
                .OrderBy(g => g.VoucherDate)
                .ThenBy(g => g.GLId)
                .ToListAsync();

            decimal running = openingBalance;
            var rows = entries.Select(e =>
            {
                running += e.Debit - e.Credit;
                return new CashBookRow
                {
                    Date = e.VoucherDate,
                    VoucherNo = e.VoucherNo,
                    VoucherType = e.VoucherType,
                    AccountName = e.AccountHead?.AccountName ?? "",
                    PartyName = e.Party?.PartyName ?? "",
                    Narration = e.Narration ?? "",
                    Debit = e.Debit,
                    Credit = e.Credit,
                    Balance = running
                };
            }).ToList();

            return new CashBookResult
            {
                From = from,
                To = to,
                OpeningBalance = openingBalance,
                TotalReceipts = entries.Sum(e => e.Debit),
                TotalPayments = entries.Sum(e => e.Credit),
                ClosingBalance = running,
                Rows = rows
            };
        }

        // ── AGING REPORT ──────────────────────────────────────────────────────

        public async Task<AgingReportResult> GetAgingReportAsync(string partyType, DateTime asOf)
        {
            var partiesQuery = _db.Parties.Where(p => p.Status != "inactive");
            if (!string.IsNullOrEmpty(partyType) && partyType != "all")
                partiesQuery = partiesQuery.Where(p => p.PartyType == partyType || p.PartyType == "both");

            var parties = await partiesQuery.OrderBy(p => p.PartyName).ToListAsync();
            var partyIds = parties.Select(p => p.PartyId).ToList();

            // Load GL data grouped by partyId + date bucket
            var glData = await _db.GeneralLedger
                .Where(g => !g.IsVoid
                         && g.PartyId != null
                         && partyIds.Contains(g.PartyId!.Value)
                         && g.VoucherDate <= asOf)
                .Select(g => new
                {
                    g.PartyId,
                    Date = g.VoucherDate.Date,
                    g.Debit,
                    g.Credit
                })
                .ToListAsync();

            var rows = new List<AgingRow>();

            foreach (var party in parties)
            {
                bool isSupplier = party.PartyType is "supplier" or "both";
                var entries = glData.Where(g => g.PartyId == party.PartyId).ToList();
                if (!entries.Any()) continue;

                decimal current = 0, d31_60 = 0, d61_90 = 0, over90 = 0;

                // Group by date so each day's net is aged together
                var byDate = entries
                    .GroupBy(e => e.Date)
                    .Select(g => new
                    {
                        Date = g.Key,
                        Net = isSupplier
                                ? g.Sum(e => e.Credit) - g.Sum(e => e.Debit)
                                : g.Sum(e => e.Debit) - g.Sum(e => e.Credit)
                    });

                foreach (var day in byDate)
                {
                    if (day.Net == 0) continue;
                    int age = (int)(asOf - day.Date).TotalDays;
                    if (age <= 30) current += day.Net;
                    else if (age <= 60) d31_60 += day.Net;
                    else if (age <= 90) d61_90 += day.Net;
                    else over90 += day.Net;
                }

                // Only show parties with a positive outstanding balance
                decimal total = current + d31_60 + d61_90 + over90;
                if (total <= 0) continue;

                rows.Add(new AgingRow
                {
                    PartyId = party.PartyId,
                    PartyName = party.PartyName,
                    PartyType = party.PartyType ?? "",
                    Current = current > 0 ? current : 0,
                    Days31_60 = d31_60 > 0 ? d31_60 : 0,
                    Days61_90 = d61_90 > 0 ? d61_90 : 0,
                    Over90 = over90 > 0 ? over90 : 0,
                    Total = total
                });
            }

            return new AgingReportResult
            {
                AsOf = asOf,
                PartyType = partyType,
                Rows = rows.OrderByDescending(r => r.Total).ToList()
            };
        }

        // ── CASH FLOW STATEMENT ───────────────────────────────────────────────

        public async Task<CashFlowResult> GetCashFlowAsync(DateTime from, DateTime to)
        {
            var cashBankAccounts = await _db.AccountHeads
                .Where(a => a.AccountCode == "1101" || a.AccountCode == "1102")
                .ToListAsync();

            var cashBankIds = cashBankAccounts.Select(a => a.AccountHeadId).ToList();

            // Opening cash balance
            decimal openingCash = 0;
            foreach (var acct in cashBankAccounts)
            {
                var glBefore = await _db.GeneralLedger
                    .Where(g => !g.IsVoid && g.AccountHeadId == acct.AccountHeadId && g.VoucherDate < from)
                    .SumAsync(g => (decimal?)(g.Debit - g.Credit)) ?? 0;

                var openBal = acct.OpeningBalanceType == "Debit"
                    ? acct.OpeningBalance : -acct.OpeningBalance;

                openingCash += openBal + glBefore;
            }

            // Period entries on Cash / Bank
            var entries = await _db.GeneralLedger
                .Where(g => !g.IsVoid
                         && cashBankIds.Contains(g.AccountHeadId)
                         && g.VoucherDate >= from
                         && g.VoucherDate <= to.Date.AddDays(1).AddTicks(-1))
                .ToListAsync();

            // Group by VoucherType → inflows (Debit) and outflows (Credit)
            var vtypeGroups = entries
                .GroupBy(e => e.VoucherType)
                .Select(g => new
                {
                    VoucherType = g.Key,
                    Inflow = g.Sum(e => e.Debit),
                    Outflow = g.Sum(e => e.Credit)
                })
                .ToList();

            static string GetCategory(string vType) => vType switch
            {
                "PV" or "RV" => "Operating Activities",
                "CV" => "Financing Activities",
                _ => "Other Adjustments"
            };

            static string GetDescription(string vType) => vType switch
            {
                "PV" => "Payment Vouchers (cash paid out)",
                "RV" => "Receipt Vouchers (cash received)",
                "CV" => "Contra Vouchers (bank ↔ cash transfers)",
                "JV" => "Journal Vouchers (adjustments)",
                _ => vType
            };

            var rows = vtypeGroups.Select(g => new CashFlowRow
            {
                Category = GetCategory(g.VoucherType),
                VoucherType = g.VoucherType,
                Description = GetDescription(g.VoucherType),
                Inflow = g.Inflow,
                Outflow = g.Outflow
            }).OrderBy(r => r.Category).ThenBy(r => r.VoucherType).ToList();

            decimal totalInflows = entries.Sum(e => e.Debit);
            decimal totalOutflows = entries.Sum(e => e.Credit);
            decimal closingCash = openingCash + totalInflows - totalOutflows;

            return new CashFlowResult
            {
                From = from,
                To = to,
                OpeningCash = openingCash,
                TotalInflows = totalInflows,
                TotalOutflows = totalOutflows,
                ClosingCash = closingCash,
                Rows = rows
            };
        }

        // ── OUTSTANDING REPORT ────────────────────────────────────────────────

        public async Task<List<OutstandingRow>> GetOutstandingReportAsync(string partyType)
        {
            var partiesQuery = _db.Parties.Where(p => p.Status != "inactive");
            if (!string.IsNullOrEmpty(partyType) && partyType != "all")
                partiesQuery = partiesQuery.Where(p => p.PartyType == partyType || p.PartyType == "both");

            var parties = await partiesQuery.OrderBy(p => p.PartyName).ToListAsync();
            var partyIds = parties.Select(p => p.PartyId).ToList();

            var glTotals = await _db.GeneralLedger
                .Where(g => !g.IsVoid && g.PartyId != null && partyIds.Contains(g.PartyId!.Value))
                .GroupBy(g => g.PartyId)
                .Select(g => new
                {
                    PartyId = g.Key!.Value,
                    TotalDebit = g.Sum(x => x.Debit),
                    TotalCredit = g.Sum(x => x.Credit)
                })
                .ToListAsync();

            var rows = new List<OutstandingRow>();

            foreach (var party in parties)
            {
                var gl = glTotals.FirstOrDefault(g => g.PartyId == party.PartyId);
                if (gl == null) continue;

                bool isSupplier = party.PartyType is "supplier" or "both";
                decimal balance = isSupplier
                    ? gl.TotalCredit - gl.TotalDebit
                    : gl.TotalDebit - gl.TotalCredit;

                if (balance <= 0) continue;  // only show outstanding (positive) balances

                rows.Add(new OutstandingRow
                {
                    PartyId = party.PartyId,
                    PartyName = party.PartyName,
                    PartyType = party.PartyType ?? "",
                    TotalDebit = gl.TotalDebit,
                    TotalCredit = gl.TotalCredit,
                    Balance = balance,
                    BalanceType = isSupplier ? "Payable" : "Receivable"
                });
            }

            return rows.OrderByDescending(r => r.Balance).ToList();
        }
    }
}