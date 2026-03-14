using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InventorySystem.Core.Models
{
    public static class TransactionTypes
    {
        public const string PurchaseInvoice = "PurchaseInvoice"; // Credit  (we owe supplier)
        public const string SaleInvoice = "SaleInvoice";     // Debit   (customer owes us)
        public const string Payment = "Payment";         // Debit   (we paid supplier)
        public const string Receipt = "Receipt";         // Credit  (customer paid us)
        public const string OpeningBalance = "Opening";
        public const string CreditNote = "CreditNote";      // future: supplier returns
        public const string DebitNote = "DebitNote";       // future: customer returns
        public const string Adjustment = "Adjustment";
    }
}
