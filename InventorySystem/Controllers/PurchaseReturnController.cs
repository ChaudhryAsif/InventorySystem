using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class PurchaseReturnController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PurchaseReturnController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index() => View();

        [HttpPost]
        public IActionResult Save([FromBody] PurchaseReturnViewModel model)
        {
            if (model == null || model.Items == null || !model.Items.Any())
                return BadRequest("Invalid return data.");

            var validItems = model.Items.Where(i => i.ItemId != null && i.Quantity > 0).ToList();
            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var purchaseReturn = new PurchaseReturn
                {
                    ReturnDate         = model.ReturnDate,
                    VendorID           = model.VendorID,
                    OriginalPurchaseId = model.OriginalPurchaseId,
                    BranchID           = model.BranchID,
                    PaymentMode        = model.PaymentMode,
                    RefNo              = model.RefNo,
                    TotalAmount        = model.TotalAmount,
                    NetAmount          = model.NetAmount,
                    Remarks            = model.Remarks
                };

                _context.PurchaseReturn.Add(purchaseReturn);
                _context.SaveChanges();

                var branchId = model.BranchID ?? 1;

                foreach (var item in validItems)
                {
                    _context.PurchaseReturnBody.Add(new PurchaseReturnBody
                    {
                        PurchaseReturnId = purchaseReturn.PurchaseReturnId,
                        ItemId           = item.ItemId,
                        Descr            = item.Desc,
                        Quantity         = item.Quantity,
                        PurPrice         = item.PurPrice,
                        DiscPer          = item.DiscPer,
                        DiscAmt          = item.DiscAmt,
                        Total            = item.Total
                    });

                    // Returning goods to supplier → stock decreases
                    var stock = _context.Stock
                        .FirstOrDefault(s => s.ItemId == item.ItemId && s.BranchId == branchId);

                    if (stock != null)
                    {
                        stock.Quantity    -= item.Quantity;
                        stock.LastUpdated  = DateTime.Now;
                    }
                }

                _context.SaveChanges();

                // ── AccountLedger: DebitNote = reduces what we owe supplier ──────
                int.TryParse(model.VendorID, out var vendorPartyId);

                if (vendorPartyId > 0)
                {
                    var modeLabel = GetPaymentModeLabel(model.PaymentMode);

                    _context.AccountLedger.Add(new AccountLedger
                    {
                        PartyId         = vendorPartyId,
                        EntryDate       = model.ReturnDate ?? DateTime.Now,
                        TransactionType = TransactionTypes.DebitNote,
                        ReferenceId     = purchaseReturn.PurchaseReturnId,
                        Description     = $"Purchase Return #{purchaseReturn.PurchaseReturnId}" +
                                          (model.OriginalPurchaseId > 0 ? $" | Ref. Invoice #{model.OriginalPurchaseId}" : ""),
                        Debit           = model.NetAmount,  // reduces what we owe supplier
                        Credit          = 0,
                        BranchId        = model.BranchID,
                        CreatedDate     = DateTime.Now
                    });

                    // Supplier refunds immediately (non-credit)
                    if (model.PaymentMode != 0)
                    {
                        var voucher = new PaymentVoucher
                        {
                            PartyId     = vendorPartyId,
                            VoucherType = "Receipt",
                            VoucherDate = model.ReturnDate ?? DateTime.Now,
                            Amount      = model.NetAmount,
                            PaymentMode = modeLabel,
                            ReferenceNo = model.RefNo,
                            Notes       = $"Auto-recorded — Purchase Return #{purchaseReturn.PurchaseReturnId}",
                            CreatedDate = DateTime.Now
                        };
                        _context.PaymentVoucher.Add(voucher);
                        _context.SaveChanges();

                        _context.AccountLedger.Add(new AccountLedger
                        {
                            PartyId         = vendorPartyId,
                            EntryDate       = model.ReturnDate ?? DateTime.Now,
                            TransactionType = TransactionTypes.Receipt,
                            ReferenceId     = voucher.VoucherId,
                            Description     = $"{modeLabel} Refund | Purchase Return #{purchaseReturn.PurchaseReturnId}",
                            Debit           = 0,
                            Credit          = model.NetAmount,
                            BranchId        = model.BranchID,
                            CreatedDate     = DateTime.Now
                        });
                    }

                    _context.SaveChanges();
                }

                transaction.Commit();
                return Ok(new { success = true, message = "Purchase return saved successfully.", id = purchaseReturn.PurchaseReturnId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetNextReturnNumber()
        {
            var nextId = (_context.PurchaseReturn.Max(c => (int?)c.PurchaseReturnId) ?? 0) + 1;
            return Json(new { returnNo = nextId });
        }

        private static string GetPaymentModeLabel(int? mode) => mode switch
        {
            1 => "Cash",
            2 => "Cheque",
            3 => "Bank Transfer",
            _ => "Credit"
        };
    }
}