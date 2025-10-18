namespace InventorySystem.Core.Models
{
    public class JournalEntry
    {
        public int Id { get; set; }
        public DateTime Date { get; set; } = DateTime.Now;
        public int DebitAccountId { get; set; }
        public Account? DebitAccount { get; set; }
        public int CreditAccountId { get; set; }
        public Account? CreditAccount { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}
