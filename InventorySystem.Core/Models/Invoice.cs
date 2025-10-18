namespace InventorySystem.Core.Models
{
    public class Invoice
    {
        public int Id { get; set; }
        public DateTime Date { get; set; } = DateTime.Now;
        public string Type { get; set; } = "Sale"; // "Sale" or "Purchase"
        public int? CustomerId { get; set; }
        public Customer? Customer { get; set; }
        public int? SupplierId { get; set; }
        public Supplier? Supplier { get; set; }
        public decimal TotalAmount { get; set; }
        public ICollection<InvoiceItem>? Items { get; set; }
    }
}
