using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Mvc;

namespace InventorySystem.Controllers
{
    public class SaleController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SaleController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index() => View();

        [HttpPost]
        public IActionResult Save([FromBody] SaleInvoiceViewModel model)
        {
            if (model == null || model.Items == null || !model.Items.Any())
                return BadRequest("Invalid sale data.");

            var invoice = new SaleInvoice
            {
                SaleDate = model.SaleDate,
                CustomerID = model.CustomerID,
                BranchID = model.BranchID,
                PaymentMode = model.PaymentMode,
                RefNo = model.RefNo,
                GSTPer = model.GSTPer,
                GSTAmount = model.GSTAmount,
                Discount = model.Discount,
                FreightExp = model.FreightExp,
                OtherExp = model.OtherExp,
                TotalAmount = model.TotalAmount,
                NetAmount = model.NetAmount,
                Remarks = model.Remarks
            };

            _context.SaleInvoice.Add(invoice);
            _context.SaveChanges();

            foreach (var item in model.Items)
            {
                if (item.ItemId == null || item.Quantity <= 0) continue;

                var branchId = model.BranchID ?? 1;
                var stock = _context.Stock.FirstOrDefault(s => s.ItemId == item.ItemId && s.BranchId == branchId);

                if (stock == null || stock.Quantity < item.Quantity)
                    return Ok(new { success = false, message = $"Insufficient stock for item ID {item.ItemId}. Available: {stock?.Quantity ?? 0}" });

                _context.SaleInvoiceBody.Add(new SaleInvoiceBody
                {
                    SaleId = invoice.SaleId,
                    ItemId = item.ItemId,
                    Descr = item.Desc,
                    Quantity = item.Quantity,
                    SalePrice = item.SalePrice,
                    DiscPer = item.DiscPer,
                    DiscAmt = item.DiscAmt,
                    Total = item.Total
                });

                stock.Quantity -= item.Quantity;
                stock.LastUpdated = DateTime.Now;
            }

            _context.SaveChanges();

            // ── AccountLedger: Debit = customer owes us ───────────────────────
            int.TryParse(model.CustomerID, out var customerPartyId);

            if (customerPartyId > 0)
            {
                var modeLabel = GetPaymentModeLabel(model.PaymentMode);

                _context.AccountLedger.Add(new AccountLedger
                {
                    PartyId = customerPartyId,
                    EntryDate = model.SaleDate ?? DateTime.Now,
                    TransactionType = TransactionTypes.SaleInvoice,
                    ReferenceId = invoice.SaleId,
                    Description = $"Sale Invoice #{invoice.SaleId}" +
                                      (model.RefNo != null ? $" | Ref: {model.RefNo}" : ""),
                    Debit = model.NetAmount,  // customer owes us this
                    Credit = 0,
                    BranchId = model.BranchID,
                    CreatedDate = DateTime.Now
                });

                // If paid immediately → also record the credit (receipt)
                if (model.PaymentMode != 0)
                {
                    var voucher = new PaymentVoucher
                    {
                        PartyId = customerPartyId,
                        VoucherType = "Receipt",
                        VoucherDate = model.SaleDate ?? DateTime.Now,
                        Amount = model.NetAmount,
                        PaymentMode = modeLabel,
                        SaleId = invoice.SaleId,
                        Notes = $"Auto-recorded — Sale #{invoice.SaleId}",
                        CreatedDate = DateTime.Now
                    };
                    _context.PaymentVoucher.Add(voucher);
                    _context.SaveChanges();

                    _context.AccountLedger.Add(new AccountLedger
                    {
                        PartyId = customerPartyId,
                        EntryDate = model.SaleDate ?? DateTime.Now,
                        TransactionType = TransactionTypes.Receipt,
                        ReferenceId = voucher.VoucherId,
                        Description = $"{modeLabel} Receipt | Sale #{invoice.SaleId}",
                        Debit = 0,
                        Credit = model.NetAmount,  // customer paid — reduces what they owe
                        BranchId = model.BranchID,
                        CreatedDate = DateTime.Now
                    });
                }

                _context.SaveChanges();
            }
            // ─────────────────────────────────────────────────────────────────

            return Ok(new { success = true, message = "Sale saved successfully", id = invoice.SaleId });
        }

        [HttpGet]
        public IActionResult GetNextInvoiceNumber()
        {
            var nextId = (_context.SaleInvoice.Max(s => (int?)s.SaleId) ?? 0) + 1;
            return Json(new { invoiceNo = nextId });
        }

        [HttpGet]
        public IActionResult GetStockByItem(long itemId, int branchId = 1)
        {
            var stock = _context.Stock.FirstOrDefault(s => s.ItemId == itemId && s.BranchId == branchId);
            return Json(new { quantity = stock?.Quantity ?? 0 });
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