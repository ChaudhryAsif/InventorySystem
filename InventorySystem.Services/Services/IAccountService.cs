using InventorySystem.Core.Models;

namespace InventorySystem.Core.Services
{
    public interface IAccountService
    {
        // Chart of Accounts
        Task<List<AccountHead>> GetChartOfAccountsAsync();
        Task<AccountHead?> GetAccountHeadAsync(int id);
        Task<(bool success, string message)> SaveAccountHeadAsync(AccountHead model);
        Task<(bool success, string message)> DeleteAccountHeadAsync(int id);

        // Vouchers
        Task<List<Voucher>> GetVouchersAsync(string? type, DateTime? from, DateTime? to);
        Task<Voucher?> GetVoucherAsync(int id);
        Task<(bool success, string message, int? voucherId)> SaveVoucherAsync(Voucher voucher);
        Task<(bool success, string message)> VoidVoucherAsync(int voucherId);

        // Ledger
        Task<List<GeneralLedger>> GetAccountLedgerAsync(int accountHeadId, DateTime? from, DateTime? to);

        // Reports
        Task<TrialBalanceResult> GetTrialBalanceAsync(DateTime asOf);
        Task<ProfitLossResult> GetProfitLossAsync(DateTime from, DateTime to);
        Task<BalanceSheetResult> GetBalanceSheetAsync(DateTime asOf);
        Task<List<PartyStatementRow>> GetPartyStatementAsync(int partyId, DateTime? from, DateTime? to);

        // ── NEW ──────────────────────────────────────────────────────────────
        Task<CashBookResult> GetCashBookAsync(DateTime from, DateTime to);
        Task<AgingReportResult> GetAgingReportAsync(string partyType, DateTime asOf);
        Task<CashFlowResult> GetCashFlowAsync(DateTime from, DateTime to);
        Task<List<OutstandingRow>> GetOutstandingReportAsync(string partyType);

        // Auto voucher number
        Task<string> GenerateVoucherNoAsync(string voucherType);
    }

    // ════════════════════════════════════════════════════════════════════════
    // EXISTING REPORT DTOs
    // ════════════════════════════════════════════════════════════════════════

    public class TrialBalanceRow
    {
        public int AccountHeadId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public decimal OpeningDebit { get; set; }
        public decimal OpeningCredit { get; set; }
        public decimal PeriodDebit { get; set; }
        public decimal PeriodCredit { get; set; }
        public decimal ClosingDebit { get; set; }
        public decimal ClosingCredit { get; set; }
    }

    public class TrialBalanceResult
    {
        public DateTime AsOf { get; set; }
        public List<TrialBalanceRow> Rows { get; set; } = new();
        public decimal TotalDebit => Rows.Sum(r => r.ClosingDebit);
        public decimal TotalCredit => Rows.Sum(r => r.ClosingCredit);
        public bool IsBalanced => TotalDebit == TotalCredit;
    }

    public class ProfitLossRow
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class ProfitLossResult
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<ProfitLossRow> IncomeRows { get; set; } = new();
        public List<ProfitLossRow> ExpenseRows { get; set; } = new();
        public decimal TotalIncome => IncomeRows.Sum(r => r.Amount);
        public decimal TotalExpense => ExpenseRows.Sum(r => r.Amount);
        public decimal NetProfit => TotalIncome - TotalExpense;
        public bool IsProfit => NetProfit >= 0;
    }

    public class BalanceSheetRow
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public decimal Balance { get; set; }
    }

    public class BalanceSheetResult
    {
        public DateTime AsOf { get; set; }
        public List<BalanceSheetRow> AssetRows { get; set; } = new();
        public List<BalanceSheetRow> LiabilityRows { get; set; } = new();
        public List<BalanceSheetRow> EquityRows { get; set; } = new();
        public decimal TotalAssets => AssetRows.Sum(r => r.Balance);
        public decimal TotalLiabilities => LiabilityRows.Sum(r => r.Balance);
        public decimal TotalEquity => EquityRows.Sum(r => r.Balance);
        public bool IsBalanced => TotalAssets == TotalLiabilities + TotalEquity;
    }

    public class PartyStatementRow
    {
        public DateTime Date { get; set; }
        public string VoucherNo { get; set; } = string.Empty;
        public string TransactionType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
    }

    // ════════════════════════════════════════════════════════════════════════
    // NEW REPORT DTOs
    // ════════════════════════════════════════════════════════════════════════

    // ── Cash Book ────────────────────────────────────────────────────────────
    public class CashBookRow
    {
        public DateTime Date { get; set; }
        public string VoucherNo { get; set; } = string.Empty;
        public string VoucherType { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;  // Cash or Bank
        public string PartyName { get; set; } = string.Empty;
        public string Narration { get; set; } = string.Empty;
        public decimal Debit { get; set; }   // Cash In
        public decimal Credit { get; set; }   // Cash Out
        public decimal Balance { get; set; }   // Running balance
    }

    public class CashBookResult
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal TotalReceipts { get; set; }
        public decimal TotalPayments { get; set; }
        public decimal ClosingBalance { get; set; }
        public List<CashBookRow> Rows { get; set; } = new();
    }

    // ── Aging Report ─────────────────────────────────────────────────────────
    public class AgingRow
    {
        public int PartyId { get; set; }
        public string PartyName { get; set; } = string.Empty;
        public string PartyType { get; set; } = string.Empty;
        public decimal Current { get; set; }   // 0–30 days
        public decimal Days31_60 { get; set; }   // 31–60 days
        public decimal Days61_90 { get; set; }   // 61–90 days
        public decimal Over90 { get; set; }   // 90+ days
        public decimal Total { get; set; }
    }

    public class AgingReportResult
    {
        public DateTime AsOf { get; set; }
        public string PartyType { get; set; } = string.Empty;
        public List<AgingRow> Rows { get; set; } = new();
        public decimal TotalCurrent => Rows.Sum(r => r.Current);
        public decimal TotalDays31_60 => Rows.Sum(r => r.Days31_60);
        public decimal TotalDays61_90 => Rows.Sum(r => r.Days61_90);
        public decimal TotalOver90 => Rows.Sum(r => r.Over90);
        public decimal GrandTotal => Rows.Sum(r => r.Total);
    }

    // ── Cash Flow ─────────────────────────────────────────────────────────────
    public class CashFlowRow
    {
        public string Category { get; set; } = string.Empty;  // Operating / Financing / Other
        public string VoucherType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Inflow { get; set; }
        public decimal Outflow { get; set; }
        public decimal Net => Inflow - Outflow;
    }

    public class CashFlowResult
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public decimal OpeningCash { get; set; }
        public decimal TotalInflows { get; set; }
        public decimal TotalOutflows { get; set; }
        public decimal ClosingCash { get; set; }
        public List<CashFlowRow> Rows { get; set; } = new();
        public List<CashFlowRow> OperatingRows => Rows.Where(r => r.Category == "Operating Activities").ToList();
        public List<CashFlowRow> FinancingRows => Rows.Where(r => r.Category == "Financing Activities").ToList();
        public List<CashFlowRow> OtherRows => Rows.Where(r => r.Category == "Other Adjustments").ToList();
        public decimal NetOperating => OperatingRows.Sum(r => r.Net);
        public decimal NetFinancing => FinancingRows.Sum(r => r.Net);
        public decimal NetOther => OtherRows.Sum(r => r.Net);
    }

    // ── Outstanding Report ───────────────────────────────────────────────────
    public class OutstandingRow
    {
        public int PartyId { get; set; }
        public string PartyName { get; set; } = string.Empty;
        public string PartyType { get; set; } = string.Empty;
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal Balance { get; set; }
        public string BalanceType { get; set; } = string.Empty;  // Payable / Receivable
    }
}