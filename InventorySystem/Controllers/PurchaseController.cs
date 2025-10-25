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
                PurchaseId = GetPurchaseInvoiceMaxItemId(),
                PurchaseDate = model.PurchaseDate,
                //VendorID = model.VendorID,
                BillNo = model.BillNo,
                //BranchID = model.BranchID,
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
            }

            _context.SaveChanges();

            return Ok(new { success = true, message = "Invoice saved successfully", id = invoice.PurchaseId });
        }

        private int GetPurchaseInvoiceMaxItemId()
        {
            var maxId = _context.PurchaseInvoice.Max(c => (int?)c.PurchaseId) ?? 0;
            return maxId + 1;
        }
    }
}
