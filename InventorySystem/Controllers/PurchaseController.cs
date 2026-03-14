using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    public class PurchaseController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PurchaseController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index() => View();

        [HttpPost]
        public IActionResult Save([FromBody] PurchaseInvoiceViewModel model)
        {
            if (model == null || model.Items == null || !model.Items.Any())
                return BadRequest("Invalid invoice data.");

            var invoice = new PurchaseInvoice
            {
                PurchaseDate = model.PurchaseDate,
                VendorID = model.VendorID,
                BillNo = model.BillNo,
                BranchID = model.BranchID,
                PaymentMode = model.PaymentMode,
                Remarks = model.Remarks,
                GSTPer = model.GSTPer,
                GSTAmount = model.GSTAmount,
                FreightExp = model.FreightExp,
                OtherExp = model.OtherExp,
                Discount = model.Discount,
                TotalAmount = model.TotalAmount,
                AmountPaid = model.NetAmount   // total amount the invoice is worth
            };

            _context.PurchaseInvoice.Add(invoice);
            _context.SaveChanges();

            // ── Line items + stock ────────────────────────────────────────────
            foreach (var item in model.Items)
            {
                if (item.Itemid == null || item.Quantity <= 0) continue;

                _context.PurchaseInvoiceBody.Add(new PurchaseInvoiceBody
                {
                    PurchaseId = invoice.PurchaseId,
                    Itemid = item.Itemid,
                    Descr = item.Desc,
                    Quantity = item.Quantity,
                    PurPrice = item.PurPrice,
                    SalePrice = item.SalePrice,
                    DiscPer = item.DiscPer,
                    DiscAmt = item.DiscAmt
                });

                var itemId = long.Parse(item.Itemid.ToString()!);
                var branchId = model.BranchID ?? 1;
                var stock = _context.Stock.FirstOrDefault(s => s.ItemId == itemId && s.BranchId == branchId);

                if (stock == null)
                    _context.Stock.Add(new Stock { ItemId = itemId, BranchId = branchId, Quantity = item.Quantity ?? 0, LastUpdated = DateTime.Now });
                else
                {
                    stock.Quantity += item.Quantity ?? 0;
                    stock.LastUpdated = DateTime.Now;
                }
            }

            _context.SaveChanges();

            // ── AccountLedger: Credit = we owe supplier ───────────────────────
            int.TryParse(model.VendorID, out var vendorPartyId);

            if (vendorPartyId > 0)
            {
                var modeLabel = GetPaymentModeLabel(model.PaymentMode);

                _context.AccountLedger.Add(new AccountLedger
                {
                    PartyId = vendorPartyId,
                    EntryDate = model.PurchaseDate ?? DateTime.Now,
                    TransactionType = TransactionTypes.PurchaseInvoice,
                    ReferenceId = invoice.PurchaseId,
                    Description = $"Purchase Invoice #{invoice.PurchaseId}" +
                                      (model.BillNo != null ? $" | Bill: {model.BillNo}" : ""),
                    Debit = 0,
                    Credit = model.NetAmount ?? 0,   // we owe this amount
                    BranchId = model.BranchID,
                    CreatedDate = DateTime.Now
                });

                // If paid immediately (non-credit) → also record the debit
                if (model.PaymentMode != 0)
                {
                    var voucher = new PaymentVoucher
                    {
                        PartyId = vendorPartyId,
                        VoucherType = "Payment",
                        VoucherDate = model.PurchaseDate ?? DateTime.Now,
                        Amount = model.NetAmount ?? 0,
                        PaymentMode = modeLabel,
                        ReferenceNo = model.BillNo,
                        PurchaseId = invoice.PurchaseId,
                        Notes = $"Auto-recorded — Invoice #{invoice.PurchaseId}",
                        CreatedDate = DateTime.Now
                    };
                    _context.PaymentVoucher.Add(voucher);
                    _context.SaveChanges(); // get VoucherId

                    _context.AccountLedger.Add(new AccountLedger
                    {
                        PartyId = vendorPartyId,
                        EntryDate = model.PurchaseDate ?? DateTime.Now,
                        TransactionType = TransactionTypes.Payment,
                        ReferenceId = voucher.VoucherId,
                        Description = $"{modeLabel} Payment | Invoice #{invoice.PurchaseId}",
                        Debit = model.NetAmount ?? 0,   // we paid — reduces what we owe
                        Credit = 0,
                        BranchId = model.BranchID,
                        CreatedDate = DateTime.Now
                    });
                }

                _context.SaveChanges();
            }
            // ─────────────────────────────────────────────────────────────────

            return Ok(new { success = true, message = "Invoice saved successfully", id = invoice.PurchaseId });
        }

        [HttpGet]
        public IActionResult GetNextInvoiceNumber()
        {
            var nextId = (_context.PurchaseInvoice.Max(c => (int?)c.PurchaseId) ?? 0) + 1;
            return Json(new { invoiceNo = nextId });
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