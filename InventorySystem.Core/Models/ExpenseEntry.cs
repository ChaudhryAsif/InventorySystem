using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace InventorySystem.Core.Models;

// One cash expense; saving posts a payment voucher: category debit, Cash in Hand credit.
public class ExpenseEntry
{
    public int ExpenseEntryId { get; set; }
    public DateTime ExpenseDate { get; set; }
    public int AccountHeadId { get; set; }
    public AccountHead? AccountHead { get; set; }
    [Required, MaxLength(200)] public string Description { get; set; } = "";
    [Column(TypeName="decimal(18,2)")] public decimal Amount { get; set; }
    public int? VoucherId { get; set; }
    [MaxLength(20)] public string VoucherNo { get; set; } = "";
    // Posted | Void
    [Required, MaxLength(10)] public string Status { get; set; } = "Posted";
    [MaxLength(100)] public string CreatedBy { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
