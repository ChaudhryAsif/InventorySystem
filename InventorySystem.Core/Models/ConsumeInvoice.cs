using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    [Table("ConsumeInvoice")]
    public class ConsumeInvoice
    {
        [Key]
        public int ConsumeId { get; set; }

        public DateTime? ConsumeDate { get; set; }

        public int? BranchID { get; set; }

        [StringLength(20)]
        public string? RefNo { get; set; }

        [StringLength(200)]
        public string? Purpose { get; set; }

        [StringLength(500)]
        public string? Remarks { get; set; }

        public int? UserNo { get; set; }

        public ICollection<ConsumeInvoiceBody>? ConsumeInvoiceBodies { get; set; }
    }
}