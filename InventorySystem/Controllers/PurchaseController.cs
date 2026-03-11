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
        public IActionResult Index()
        {
            return View();
        }

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
                AmountPaid = model.NetAmount
            };

            _context.PurchaseInvoice.Add(invoice);
            _context.SaveChanges();

            foreach (var item in model.Items)
            {
                if (item.Itemid == null || item.Quantity <= 0) continue;

                var body = new PurchaseInvoiceBody
                {
                    PurchaseId = invoice.PurchaseId,
                    Itemid = item.Itemid,
                    Descr = item.Desc,
                    Quantity = item.Quantity,
                    PurPrice = item.PurPrice,
                    SalePrice = item.SalePrice,
                    DiscPer = item.DiscPer,
                    DiscAmt = item.DiscAmt
                };
                _context.PurchaseInvoiceBody.Add(body);

                // ── Update Stock ──────────────────────────────────────
                var itemId = long.Parse(item.Itemid.ToString()!);
                var branchId = model.BranchID ?? 1;

                var stock = _context.Stock
                    .FirstOrDefault(s => s.ItemId == itemId && s.BranchId == branchId);

                if (stock == null)
                {
                    _context.Stock.Add(new Stock
                    {
                        ItemId = itemId,
                        BranchId = branchId,
                        Quantity = item.Quantity ?? 0,
                        LastUpdated = DateTime.Now
                    });
                }
                else
                {
                    stock.Quantity += item.Quantity ?? 0;
                    stock.LastUpdated = DateTime.Now;
                }
                // ─────────────────────────────────────────────────────
            }

            _context.SaveChanges();

            return Ok(new { success = true, message = "Invoice saved successfully", id = invoice.PurchaseId });
        }

        [HttpGet]
        public IActionResult GetNextInvoiceNumber()
        {
            var nextId = GetPurchaseInvoiceMaxItemId();
            return Json(new { invoiceNo = nextId });
        }

        private int GetPurchaseInvoiceMaxItemId()
        {
            var maxId = _context.PurchaseInvoice.Max(c => (int?)c.PurchaseId) ?? 0;
            return maxId + 1;
        }
    }
}
