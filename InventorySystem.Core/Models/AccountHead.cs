using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    /// <summary>
    /// Chart of Accounts — hierarchical account tree.
    /// Level 1 = Group (Asset, Liability, Equity, Income, Expense)
    /// Level 2 = Sub-Group (Current Assets, Fixed Assets, etc.)
    /// Level 3 = Ledger Account (Cash, Bank, Sales, Purchases, etc.)
    /// </summary>
    [Table("AccountHead")]
    public class AccountHead
    {
        [Key]
        public int AccountHeadId { get; set; }

        [Required, StringLength(20)]
        public string AccountCode { get; set; } = string.Empty;   // e.g. "1001", "2001"

        [Required, StringLength(150)]
        public string AccountName { get; set; } = string.Empty;

        /// <summary>Assets | Liabilities | Equity | Income | Expenses</summary>
        [Required, StringLength(50)]
        public string AccountType { get; set; } = string.Empty;

        /// <summary>Null = top-level group; set to parent AccountHeadId for sub-accounts.</summary>
        public int? ParentId { get; set; }

        /// <summary>1 = Group, 2 = Sub-Group, 3 = Ledger (postable)</summary>
        public int Level { get; set; } = 3;

        /// <summary>Only Level 3 accounts are postable (can receive journal entries).</summary>
        public bool IsPostable => Level == 3;

        /// <summary>Normal balance side: Debit or Credit</summary>
        [StringLength(10)]
        public string NormalBalance { get; set; } = "Debit";  // "Debit" | "Credit"

        public decimal OpeningBalance { get; set; } = 0;

        [StringLength(10)]
        public string OpeningBalanceType { get; set; } = "Debit"; // "Debit" | "Credit"

        public bool IsSystem { get; set; } = false;  // system accounts cannot be deleted
        public bool IsActive { get; set; } = true;

        [StringLength(300)]
        public string? Description { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Navigation
        [ForeignKey(nameof(ParentId))]
        public AccountHead? Parent { get; set; }
        public ICollection<AccountHead> Children { get; set; } = new List<AccountHead>();

        /// <summary>Ledger entries posted to this account.</summary>
        public ICollection<GeneralLedger> LedgerEntries { get; set; } = new List<GeneralLedger>();
    }
}