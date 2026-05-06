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

        // Auto voucher number
        Task<string> GenerateVoucherNoAsync(string voucherType);
    }

    // ── Report DTOs ────────────────────────────────────────────────────────────

    public class TrialBalanceRow
    {
        public int AccountHeadId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public decimal OpeningDebit  { get; set; }
        public decimal OpeningCredit { get; set; }
        public decimal PeriodDebit   { get; set; }
        public decimal PeriodCredit  { get; set; }
        public decimal ClosingDebit  { get; set; }
        public decimal ClosingCredit { get; set; }
    }

    public class TrialBalanceResult
    {
        public DateTime AsOf { get; set; }
        public List<TrialBalanceRow> Rows { get; set; } = new();
        public decimal TotalDebit  => Rows.Sum(r => r.ClosingDebit);
        public decimal TotalCredit => Rows.Sum(r => r.ClosingCredit);
        public bool IsBalanced     => TotalDebit == TotalCredit;
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
        public DateTime To   { get; set; }
        public List<ProfitLossRow> IncomeRows   { get; set; } = new();
        public List<ProfitLossRow> ExpenseRows  { get; set; } = new();
        public decimal TotalIncome  => IncomeRows.Sum(r => r.Amount);
        public decimal TotalExpense => ExpenseRows.Sum(r => r.Amount);
        public decimal NetProfit    => TotalIncome - TotalExpense;
        public bool IsProfit        => NetProfit >= 0;
    }

    public class BalanceSheetRow
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public decimal Balance    { get; set; }
    }

    public class BalanceSheetResult
    {
        public DateTime AsOf { get; set; }
        public List<BalanceSheetRow> AssetRows      { get; set; } = new();
        public List<BalanceSheetRow> LiabilityRows  { get; set; } = new();
        public List<BalanceSheetRow> EquityRows      { get; set; } = new();
        public decimal TotalAssets      => AssetRows.Sum(r => r.Balance);
        public decimal TotalLiabilities => LiabilityRows.Sum(r => r.Balance);
        public decimal TotalEquity      => EquityRows.Sum(r => r.Balance);
        public bool IsBalanced          => TotalAssets == TotalLiabilities + TotalEquity;
    }

    public class PartyStatementRow
    {
        public DateTime Date            { get; set; }
        public string   VoucherNo       { get; set; } = string.Empty;
        public string   TransactionType { get; set; } = string.Empty;
        public string   Description     { get; set; } = string.Empty;
        public decimal  Debit           { get; set; }
        public decimal  Credit          { get; set; }
        public decimal  Balance         { get; set; }
    }
}