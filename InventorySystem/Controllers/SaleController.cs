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

            var branchId   = model.BranchID ?? 1;
            var validItems = model.Items.Where(i => i.ItemId != null && i.Quantity > 0).ToList();

            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            // ── Pre-validate ALL stock (grouped by item) before touching the database ──
            var groupedItems = validItems
                .GroupBy(i => i.ItemId)
                .Select(g => new
                {
                    ItemId   = g.Key,
                    TotalQty = g.Sum(x => x.Quantity),
                    Desc     = g.First().Desc
                })
                .ToList();

            foreach (var group in groupedItems)
            {
                var stock = _context.Stock
                    .FirstOrDefault(s => s.ItemId == group.ItemId && s.BranchId == branchId);

                if (stock == null || stock.Quantity < group.TotalQty)
                    return Ok(new
                    {
                        success = false,
                        message = $"Insufficient stock for \"{group.Desc ?? group.ItemId.ToString()}\". " +
                                  $"Available: {stock?.Quantity ?? 0}, Requested: {group.TotalQty}"
                    });
            }

            // ── All checks passed — write inside a single transaction ─────────
            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var invoice = new SaleInvoice
                {
                    SaleDate    = model.SaleDate,
                    CustomerID  = model.CustomerID,
                    BranchID    = model.BranchID,
                    PaymentMode = model.PaymentMode,
                    RefNo       = model.RefNo,
                    GSTPer      = model.GSTPer,
                    GSTAmount   = model.GSTAmount,
                    Discount    = model.Discount,
                    FreightExp  = model.FreightExp,
                    OtherExp    = model.OtherExp,
                    TotalAmount = model.TotalAmount,
                    NetAmount   = model.NetAmount,
                    Remarks     = model.Remarks
                };

                _context.SaleInvoice.Add(invoice);
                _context.SaveChanges(); // materialise SaleId

                // ── Line items + stock deduction ──────────────────────────────
                foreach (var item in validItems)
                {
                    _context.SaleInvoiceBody.Add(new SaleInvoiceBody
                    {
                        SaleId    = invoice.SaleId,
                        ItemId    = item.ItemId,
                        Descr     = item.Desc,
                        Quantity  = item.Quantity,
                        SalePrice = item.SalePrice,
                        DiscPer   = item.DiscPer,
                        DiscAmt   = item.DiscAmt,
                        Total     = item.Total
                    });

                    // Stock already validated above — safe to use .First()
                    var stock = _context.Stock
                        .First(s => s.ItemId == item.ItemId && s.BranchId == branchId);

                    stock.Quantity   -= item.Quantity;
                    stock.LastUpdated = DateTime.Now;
                }

                _context.SaveChanges();

                // ── AccountLedger ─────────────────────────────────────────────
                int.TryParse(model.CustomerID, out var customerPartyId);

                if (customerPartyId > 0)
                {
                    var modeLabel = GetPaymentModeLabel(model.PaymentMode);

                    _context.AccountLedger.Add(new AccountLedger
                    {
                        PartyId         = customerPartyId,
                        EntryDate       = model.SaleDate ?? DateTime.Now,
                        TransactionType = TransactionTypes.SaleInvoice,
                        ReferenceId     = invoice.SaleId,
                        Description     = $"Sale Invoice #{invoice.SaleId}" +
                                          (model.RefNo != null ? $" | Ref: {model.RefNo}" : ""),
                        Debit           = model.NetAmount,   // customer owes us
                        Credit          = 0,
                        BranchId        = model.BranchID,
                        CreatedDate     = DateTime.Now
                    });

                    // Non-credit payment → also record the receipt
                    if (model.PaymentMode != 0)
                    {
                        var voucher = new PaymentVoucher
                        {
                            PartyId     = customerPartyId,
                            VoucherType = "Receipt",
                            VoucherDate = model.SaleDate ?? DateTime.Now,
                            Amount      = model.NetAmount,
                            PaymentMode = modeLabel,
                            SaleId      = invoice.SaleId,
                            Notes       = $"Auto-recorded — Sale #{invoice.SaleId}",
                            CreatedDate = DateTime.Now
                        };
                        _context.PaymentVoucher.Add(voucher);
                        _context.SaveChanges(); // materialise VoucherId

                        _context.AccountLedger.Add(new AccountLedger
                        {
                            PartyId         = customerPartyId,
                            EntryDate       = model.SaleDate ?? DateTime.Now,
                            TransactionType = TransactionTypes.Receipt,
                            ReferenceId     = voucher.VoucherId,
                            Description     = $"{modeLabel} Receipt | Sale #{invoice.SaleId}",
                            Debit           = 0,
                            Credit          = model.NetAmount,   // reduces what customer owes
                            BranchId        = model.BranchID,
                            CreatedDate     = DateTime.Now
                        });
                    }

                    _context.SaveChanges();
                }

                transaction.Commit();
                return Ok(new { success = true, message = "Sale invoice saved successfully", id = invoice.SaleId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error saving sale invoice: " + ex.Message });
            }
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
            var stock = _context.Stock
                .FirstOrDefault(s => s.ItemId == itemId && s.BranchId == branchId);
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