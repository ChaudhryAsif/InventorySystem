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

                // ── Check available stock ─────────────────────────────
                var stock = _context.Stock
                    .FirstOrDefault(s => s.ItemId == item.ItemId && s.BranchId == branchId);

                if (stock == null || stock.Quantity < item.Quantity)
                {
                    return Ok(new
                    {
                        success = false,
                        message = $"Insufficient stock for item ID {item.ItemId}. " +
                                  $"Available: {stock?.Quantity ?? 0}"
                    });
                }
                // ─────────────────────────────────────────────────────

                var body = new SaleInvoiceBody
                {
                    SaleId = invoice.SaleId,
                    ItemId = item.ItemId,
                    Descr = item.Desc,
                    Quantity = item.Quantity,
                    SalePrice = item.SalePrice,
                    DiscPer = item.DiscPer,
                    DiscAmt = item.DiscAmt,
                    Total = item.Total
                };
                _context.SaleInvoiceBody.Add(body);

                // ── Decrement Stock ───────────────────────────────────
                stock.Quantity -= item.Quantity;
                stock.LastUpdated = DateTime.Now;
                // ─────────────────────────────────────────────────────
            }

            _context.SaveChanges();

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
            var stock = _context.Stock
                .FirstOrDefault(s => s.ItemId == itemId && s.BranchId == branchId);

            return Json(new { quantity = stock?.Quantity ?? 0 });
        }
    }
}
