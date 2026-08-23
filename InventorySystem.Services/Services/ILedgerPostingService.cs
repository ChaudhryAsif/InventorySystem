using InventorySystem.Core.Models;

namespace InventorySystem.Core.Services
{
    /// <summary>
    /// Posts Sale/Purchase/Returns transactions to the double-entry ledger
    /// (AccountHead/GeneralLedger/Voucher) on top of the legacy AccountLedger/PaymentVoucher
    /// writes those controllers already perform. Only called when AccountingOptions.PostToGeneralLedger is true.
    /// </summary>
    public interface ILedgerPostingService
    {
        Task<int?> PostSaleAsync(SaleInvoice invoice);
        Task<int?> PostPurchaseAsync(PurchaseInvoice invoice);
        Task<int?> PostSaleReturnAsync(SaleReturn saleReturn);
        Task<int?> PostPurchaseReturnAsync(PurchaseReturn purchaseReturn);
        Task VoidAsync(int? voucherId);
    }
}
