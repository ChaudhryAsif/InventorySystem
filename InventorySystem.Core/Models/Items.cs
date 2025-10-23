using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InventorySystem.Core.Models
{
    [Table("Item")]
    public class Items
    {
        [Key]
        public long ItemID { get; set; }
        public int CategoryID { get; set; }
        public string? ItemName { get; set; }
        public string? CompanyName { get; set; }
        public string? ProductName { get; set; }
        public string? Description { get; set; }
        public string? Design { get; set; }
        public string? Size { get; set; }
        public string? PurchaseAccount { get; set; }
        public string? SaleAccount { get; set; }
        public bool? IsActive { get; set; }
        public bool? ismarinated { get; set; }
        public string? Packing { get; set; }
        public string? Origin { get; set; }
        public decimal? SalePrice { get; set; }
        public string? WHCOGS { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string? ImagePath { get; set; }

        public ItemCategory Category { get; set; }
    }
}
