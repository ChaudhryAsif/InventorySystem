using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Mvc;

namespace InventorySystem.Controllers
{
    public class SaleReturnController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SaleReturnController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index() => View();

        [HttpPost]
        public IActionResult Save([FromBody] SaleReturnViewModel model)
        {
            if (model == null || model.Items == null || !model.Items.Any())
                return BadRequest("Invalid return data.");

            var validItems = model.Items.Where(i => i.ItemId != null && i.Quantity > 0).ToList();
            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var saleReturn = new SaleReturn
                {
                    ReturnDate     = model.ReturnDate,
                    CustomerID     = model.CustomerID,
                    OriginalSaleId = model.OriginalSaleId,
                    BranchID       = model.BranchID,
                    PaymentMode    = model.PaymentMode,
                    RefNo          = model.RefNo,
                    TotalAmount    = model.TotalAmount,
                    NetAmount      = model.NetAmount,
                    Remarks        = model.Remarks
                };

                _context.SaleReturn.Add(saleReturn);
                _context.SaveChanges();

                var branchId = model.BranchID ?? 1;

                foreach (var item in validItems)
                {
                    _context.SaleReturnBody.Add(new SaleReturnBody
                    {
                        SaleReturnId = saleReturn.SaleReturnId,
                        ItemId       = item.ItemId,
                        Descr        = item.Desc,
                        Quantity     = item.Quantity,
                        SalePrice    = item.SalePrice,
                        DiscPer      = item.DiscPer,
                        DiscAmt      = item.DiscAmt,
                        Total        = item.Total
                    });

                    // Customer returns goods → stock increases
                    var stock = _context.Stock
                        .FirstOrDefault(s => s.ItemId == item.ItemId && s.BranchId == branchId);

                    if (stock == null)
                        _context.Stock.Add(new Stock
                        {
                            ItemId      = item.ItemId!.Value,
                            BranchId    = branchId,
                            Quantity    = item.Quantity,
                            LastUpdated = DateTime.Now
                        });
                    else
                    {
                        stock.Quantity    += item.Quantity;
                        stock.LastUpdated  = DateTime.Now;
                    }
                }

                _context.SaveChanges();

                // ── AccountLedger: CreditNote = reduces what customer owes us ────
                int.TryParse(model.CustomerID, out var customerPartyId);

                if (customerPartyId > 0)
                {
                    var modeLabel = GetPaymentModeLabel(model.PaymentMode);

                    _context.AccountLedger.Add(new AccountLedger
                    {
                        PartyId         = customerPartyId,
                        EntryDate       = model.ReturnDate ?? DateTime.Now,
                        TransactionType = TransactionTypes.CreditNote,
                        ReferenceId     = saleReturn.SaleReturnId,
                        Description     = $"Sale Return #{saleReturn.SaleReturnId}" +
                                          (model.OriginalSaleId > 0 ? $" | Ref. Invoice #{model.OriginalSaleId}" : ""),
                        Debit           = 0,
                        Credit          = model.NetAmount,  // reduces what customer owes us
                        BranchId        = model.BranchID,
                        CreatedDate     = DateTime.Now
                    });

                    // We refund customer immediately (non-credit)
                    if (model.PaymentMode != 0)
                    {
                        var voucher = new PaymentVoucher
                        {
                            PartyId     = customerPartyId,
                            VoucherType = "Payment",
                            VoucherDate = model.ReturnDate ?? DateTime.Now,
                            Amount      = model.NetAmount,
                            PaymentMode = modeLabel,
                            ReferenceNo = model.RefNo,
                            Notes       = $"Auto-recorded — Sale Return #{saleReturn.SaleReturnId}",
                            CreatedDate = DateTime.Now
                        };
                        _context.PaymentVoucher.Add(voucher);
                        _context.SaveChanges();

                        _context.AccountLedger.Add(new AccountLedger
                        {
                            PartyId         = customerPartyId,
                            EntryDate       = model.ReturnDate ?? DateTime.Now,
                            TransactionType = TransactionTypes.Payment,
                            ReferenceId     = voucher.VoucherId,
                            Description     = $"{modeLabel} Refund | Sale Return #{saleReturn.SaleReturnId}",
                            Debit           = model.NetAmount,
                            Credit          = 0,
                            BranchId        = model.BranchID,
                            CreatedDate     = DateTime.Now
                        });
                    }

                    _context.SaveChanges();
                }

                transaction.Commit();
                return Ok(new { success = true, message = "Sale return saved successfully.", id = saleReturn.SaleReturnId });
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
            var nextId = (_context.SaleReturn.Max(c => (int?)c.SaleReturnId) ?? 0) + 1;
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