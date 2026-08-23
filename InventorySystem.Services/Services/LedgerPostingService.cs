using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Core.Services
{
    public class LedgerPostingService : ILedgerPostingService
    {
        private readonly ApplicationDbContext _db;
        private readonly IAccountService _accounts;
        private Dictionary<string, int>? _codeToId;

        private const string Cash = "1101";
        private const string Bank = "1102";
        private const string TradeReceivables = "1301";
        private const string TradePayables = "2201";
        private const string Sales = "4101";
        private const string SalesReturns = "4102";
        private const string Purchases = "5101";
        private const string PurchaseReturns = "5102";

        public LedgerPostingService(ApplicationDbContext db, IAccountService accounts)
        {
            _db = db;
            _accounts = accounts;
        }

        private async Task<Dictionary<string, int>> GetAccountLookupAsync()
        {
            if (_codeToId != null) return _codeToId;

            _codeToId = await _db.AccountHeads
                .Where(a => a.AccountCode == Cash || a.AccountCode == Bank ||
                            a.AccountCode == TradeReceivables || a.AccountCode == TradePayables ||
                            a.AccountCode == Sales || a.AccountCode == SalesReturns ||
                            a.AccountCode == Purchases || a.AccountCode == PurchaseReturns)
                .ToDictionaryAsync(a => a.AccountCode, a => a.AccountHeadId);

            return _codeToId;
        }

        /// <summary>Cash=1101 / Cheque or Bank Transfer=1102 / Credit(0 or null)=null.</summary>
        private static string? CashOrBankCode(int? paymentMode) => paymentMode switch
        {
            1 => Cash,
            2 => Bank,
            3 => Bank,
            _ => null
        };

        public async Task<int?> PostSaleAsync(SaleInvoice invoice)
        {
            if (!int.TryParse(invoice.CustomerID, out var customerPartyId) || customerPartyId <= 0)
                return null;

            var amount = invoice.NetAmount ?? 0;
            if (amount <= 0) return null;

            var accounts = await GetAccountLookupAsync();
            var drCode = CashOrBankCode(invoice.PaymentMode) ?? TradeReceivables;

            var voucher = new Voucher
            {
                VoucherType = "JV",
                VoucherDate = invoice.SaleDate ?? DateTime.Now,
                PartyId = customerPartyId,
                ReferenceNo = $"SALE-{invoice.SaleId}",
                Narration = $"Sale Invoice #{invoice.SaleId}" + (invoice.RefNo != null ? $" | Ref: {invoice.RefNo}" : ""),
                BranchId = invoice.BranchID,
                CreatedBy = "system",
                Details = new List<VoucherDetail>
                {
                    new VoucherDetail { AccountHeadId = accounts[drCode], PartyId = customerPartyId, Debit = amount },
                    new VoucherDetail { AccountHeadId = accounts[Sales], Credit = amount }
                }
            };

            var (success, _, voucherId) = await _accounts.SaveVoucherAsync(voucher);
            return success ? voucherId : null;
        }

        public async Task<int?> PostPurchaseAsync(PurchaseInvoice invoice)
        {
            if (!int.TryParse(invoice.VendorID, out var vendorPartyId) || vendorPartyId <= 0)
                return null;

            var amount = invoice.AmountPaid ?? 0;
            if (amount <= 0) return null;

            var accounts = await GetAccountLookupAsync();
            var crCode = CashOrBankCode(invoice.PaymentMode) ?? TradePayables;

            var voucher = new Voucher
            {
                VoucherType = "JV",
                VoucherDate = invoice.PurchaseDate ?? DateTime.Now,
                PartyId = vendorPartyId,
                ReferenceNo = $"PUR-{invoice.PurchaseId}",
                Narration = $"Purchase Invoice #{invoice.PurchaseId}" + (invoice.BillNo != null ? $" | Bill: {invoice.BillNo}" : ""),
                BranchId = invoice.BranchID,
                CreatedBy = "system",
                Details = new List<VoucherDetail>
                {
                    new VoucherDetail { AccountHeadId = accounts[Purchases], Debit = amount },
                    new VoucherDetail { AccountHeadId = accounts[crCode], PartyId = vendorPartyId, Credit = amount }
                }
            };

            var (success, _, voucherId) = await _accounts.SaveVoucherAsync(voucher);
            return success ? voucherId : null;
        }

        public async Task<int?> PostSaleReturnAsync(SaleReturn saleReturn)
        {
            if (!int.TryParse(saleReturn.CustomerID, out var customerPartyId) || customerPartyId <= 0)
                return null;

            var amount = saleReturn.NetAmount ?? 0;
            if (amount <= 0) return null;

            var accounts = await GetAccountLookupAsync();
            // Refunded in cash/bank → money goes out of Cash/Bank; on credit → reduces what customer owes.
            var crCode = CashOrBankCode(saleReturn.PaymentMode) ?? TradeReceivables;

            var voucher = new Voucher
            {
                VoucherType = "JV",
                VoucherDate = saleReturn.ReturnDate ?? DateTime.Now,
                PartyId = customerPartyId,
                ReferenceNo = $"SRET-{saleReturn.SaleReturnId}",
                Narration = $"Sale Return #{saleReturn.SaleReturnId}" + (saleReturn.OriginalSaleId > 0 ? $" | Ref. Invoice #{saleReturn.OriginalSaleId}" : ""),
                BranchId = saleReturn.BranchID,
                CreatedBy = "system",
                Details = new List<VoucherDetail>
                {
                    new VoucherDetail { AccountHeadId = accounts[SalesReturns], Debit = amount },
                    new VoucherDetail { AccountHeadId = accounts[crCode], PartyId = customerPartyId, Credit = amount }
                }
            };

            var (success, _, voucherId) = await _accounts.SaveVoucherAsync(voucher);
            return success ? voucherId : null;
        }

        public async Task<int?> PostPurchaseReturnAsync(PurchaseReturn purchaseReturn)
        {
            if (!int.TryParse(purchaseReturn.VendorID, out var vendorPartyId) || vendorPartyId <= 0)
                return null;

            var amount = purchaseReturn.NetAmount ?? 0;
            if (amount <= 0) return null;

            var accounts = await GetAccountLookupAsync();
            // Refunded in cash/bank → money comes into Cash/Bank; on credit → reduces what we owe supplier.
            var drCode = CashOrBankCode(purchaseReturn.PaymentMode) ?? TradePayables;

            var voucher = new Voucher
            {
                VoucherType = "JV",
                VoucherDate = purchaseReturn.ReturnDate ?? DateTime.Now,
                PartyId = vendorPartyId,
                ReferenceNo = $"PRET-{purchaseReturn.PurchaseReturnId}",
                Narration = $"Purchase Return #{purchaseReturn.PurchaseReturnId}" + (purchaseReturn.OriginalPurchaseId > 0 ? $" | Ref. Invoice #{purchaseReturn.OriginalPurchaseId}" : ""),
                BranchId = purchaseReturn.BranchID,
                CreatedBy = "system",
                Details = new List<VoucherDetail>
                {
                    new VoucherDetail { AccountHeadId = accounts[drCode], PartyId = vendorPartyId, Debit = amount },
                    new VoucherDetail { AccountHeadId = accounts[PurchaseReturns], Credit = amount }
                }
            };

            var (success, _, voucherId) = await _accounts.SaveVoucherAsync(voucher);
            return success ? voucherId : null;
        }

        public async Task VoidAsync(int? voucherId)
        {
            if (voucherId is null or <= 0) return;
            await _accounts.VoidVoucherAsync(voucherId.Value);
        }
    }
}
