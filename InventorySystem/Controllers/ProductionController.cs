using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class ProductionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IStockService _stock;
        public ProductionController(ApplicationDbContext context, IStockService stock)
        {
            _context = context;
            _stock = stock;
        }

        // ════════════════════════════════════════════════════════════════════════
        // INDEX — All production orders
        // ════════════════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var orders = await _context.ProductionOrder
                .Include(o => o.CostSheet)
                .OrderByDescending(o => o.CreatedDate)
                .Select(o => new
                {
                    o.ProductionOrderId,
                    o.ProductName,
                    o.ProducedQty, o.UsesEstimatedConsumption,
                    o.OrderQty,
                    o.OrderDate,
                    o.DeliveryDate,
                    o.Status,
                    o.CostSheetId,
                    o.AgreedUnitPrice, o.AgreedGstPercent, o.CustomerReference,
                    BoxStyle = o.CostSheet != null ? o.CostSheet.BoxStyle : null,
                    FinalRate = (decimal?)(o.UsesEstimatedConsumption ? o.EstimatedRate : o.CostSheet != null ? o.CostSheet.FinalRateWithGST ?? 0 : 0),
                    TotalValue = o.AgreedUnitPrice.HasValue ? (decimal?)OrderPricing.Total(o.OrderQty, o.AgreedUnitPrice.Value, o.AgreedGstPercent) : null,
                    CustomerName = _context.Parties
                        .Where(p => p.PartyId.ToString() == o.CustomerId)
                        .Select(p => p.PartyName)
                        .FirstOrDefault() ?? o.CustomerId,
                    DaysLeft = (int)(o.DeliveryDate - DateTime.Today).TotalDays
                })
                .ToListAsync();

            return View(orders);
        }

        // ════════════════════════════════════════════════════════════════════════
        // CREATE (GET)
        // ════════════════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Create(int? costSheetId)
        {
            ViewBag.FinalSheets = await _context.CostSheet
                .Where(c => c.Status == "Final")
                .OrderByDescending(c => c.CostSheetId)
                .Select(c => new
                {
                    c.CostSheetId,
                    c.ItemName,
                    c.BoxStyle,
                    c.FinalRateWithGST
                })
                .ToListAsync();

            ViewBag.SelectedCostSheetId = costSheetId ?? 0;
            return View();
        }

        // ════════════════════════════════════════════════════════════════════════
        // GET COST SHEET INFO (AJAX) — fills form when cost sheet selected
        // ════════════════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetCostSheetInfo(int id)
        {
            var sheet = await _context.CostSheet
                .Where(c => c.CostSheetId == id)
                .Select(c => new
                {
                    c.CostSheetId,
                    c.ItemName,
                    c.BoxStyle,
                    c.FinalRateWithGST,
                    c.FinalCostWOGST
                })
                .FirstOrDefaultAsync();

            if (sheet == null)
                return NotFound(new { success = false });

            return Json(new { success = true, data = sheet });
        }

        // ════════════════════════════════════════════════════════════════════════
        // SAVE — Create production order + auto-generate material requirements
        // ════════════════════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> Save([FromBody] ProductionOrderViewModel model)
        {
            if (model == null || model.OrderQty <= 0 || model.OrderQty > 1000000 || model.OrderDate == default || model.DeliveryDate < model.OrderDate)
                return BadRequest(new { success = false, message = "Enter valid quantities and order/delivery dates." });

            if (OrderPricing.Validate(model) is { } pricingError)
                return BadRequest(new { success = false, message = pricingError });
            try { model.CustomerId = await EstimatedProductionService.ValidateCustomerAsync(_context, model.CustomerId); }
            catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }

            if (model.OrderQty <= 0 || model.OrderQty > 1000000 || model.OrderDate == default)
                return BadRequest(new { success = false, message = "Enter a valid order quantity and date." });
            using var tx = await _context.Database.BeginAtomicTransactionAsync();
            try
            {
                var sheet = await _context.CostSheet
                    .Include(c => c.Plies)
                    .Include(c => c.Materials)
                    .FirstOrDefaultAsync(c => c.CostSheetId == model.CostSheetId);

                if (sheet == null || sheet.Status != "Final" || sheet.FinishedItemId == null)
                    return BadRequest(new { success = false, message = "Select a Final cost sheet linked to a finished-box item." });

                var order = new ProductionOrder
                {
                    CostSheetId = model.CostSheetId,
                    CustomerId = model.CustomerId,
                    ProductName = model.ProductName ?? sheet.ItemName,
                    OrderQty = model.OrderQty,
                    OrderDate = model.OrderDate,
                    DeliveryDate = model.DeliveryDate,
                    Status = "Pending",
                    Remarks = model.Remarks,
                    AgreedUnitPrice = model.AgreedUnitPrice,
                    AgreedGstPercent = model.AgreedGstPercent,
                    CustomerReference = model.CustomerReference?.Trim(),
                    CreatedDate = DateTime.Now
                };

                _context.ProductionOrder.Add(order);
                await _context.SaveChangesAsync();

                await GenerateMaterialsAsync(order, sheet);

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new
                {
                    success = true,
                    id = order.ProductionOrderId,
                    message = "Production Order created successfully."
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return BadRequest(new { success = false, message = ex is InvalidOperationException ? ex.Message : "The operation could not be saved. Refresh and retry." });
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // GET BY ID (AJAX) — fills edit form
        // ════════════════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetById(int id)
        {
            var order = await _context.ProductionOrder
                .Where(o => o.ProductionOrderId == id)
                .Select(o => new
                {
                    o.ProductionOrderId,
                    o.CostSheetId,
                    o.CustomerId,
                    CustomerName = _context.Parties
                        .Where(p => p.PartyId.ToString() == o.CustomerId)
                        .Select(p => p.PartyName)
                        .FirstOrDefault() ?? o.CustomerId,
                    o.ProductName,
                    o.ProducedQty, o.UsesEstimatedConsumption,
                    o.OrderQty,
                    o.OrderDate,
                    o.DeliveryDate,
                    o.Status,
                    o.Remarks, o.AgreedUnitPrice, o.AgreedGstPercent, o.CustomerReference,
                    o.EstimatedRate,
                    AgreedTotalValue = o.AgreedUnitPrice.HasValue ? (decimal?)OrderPricing.Total(o.OrderQty, o.AgreedUnitPrice.Value, o.AgreedGstPercent) : null
                })
                .FirstOrDefaultAsync();

            if (order == null)
                return NotFound(new { success = false });

            return Json(new { success = true, data = order });
        }

        // ════════════════════════════════════════════════════════════════════════
        // EDIT (GET) — reuses Create view (mirrors CostSheetController.Edit)
        // ════════════════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var order = await _context.ProductionOrder.FirstOrDefaultAsync(o => o.ProductionOrderId == id);
            if (order == null) return NotFound();

            ViewBag.FinalSheets = await _context.CostSheet
                .Where(c => c.Status == "Final" || c.CostSheetId == order.CostSheetId)
                .OrderByDescending(c => c.CostSheetId)
                .Select(c => new
                {
                    c.CostSheetId,
                    c.ItemName,
                    c.BoxStyle,
                    c.FinalRateWithGST
                })
                .ToListAsync();

            ViewBag.SelectedCostSheetId = order.CostSheetId;
            return View("Create", order);
        }

        // ════════════════════════════════════════════════════════════════════════
        // UPDATE — edit a Pending order + regenerate material requirements
        // ════════════════════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] ProductionOrderViewModel model)
        {
            if (model == null || model.OrderQty <= 0 || model.OrderQty > 1000000 || model.OrderDate == default || model.DeliveryDate < model.OrderDate)
                return BadRequest(new { success = false, message = "Enter valid quantities and order/delivery dates." });

            if (OrderPricing.Validate(model) is { } pricingError)
                return BadRequest(new { success = false, message = pricingError });
            try { model.CustomerId = await EstimatedProductionService.ValidateCustomerAsync(_context, model.CustomerId); }
            catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }

            using var tx = await _context.Database.BeginAtomicTransactionAsync();
            try
            {
                var order = await _context.ProductionOrder
                    .FirstOrDefaultAsync(o => o.ProductionOrderId == model.ProductionOrderId);

                if (order == null)
                    return Ok(new { success = false, message = "Production Order not found." });

                if (order.Status != "Pending")
                    return Ok(new
                    {
                        success = false,
                        message = "Only Pending orders can be edited. For completed production, reverse it first when eligible, then create a corrected order."
                    });

                var sheet = await _context.CostSheet
                    .Include(c => c.Plies)
                    .Include(c => c.Materials)
                    .FirstOrDefaultAsync(c => c.CostSheetId == model.CostSheetId);

                if (sheet == null)
                    return Ok(new { success = false, message = "Cost Sheet not found." });

                order.CostSheetId = model.CostSheetId;
                order.CustomerId = model.CustomerId;
                order.ProductName = model.ProductName ?? sheet.ItemName;
                order.OrderQty = model.OrderQty;
                order.OrderDate = model.OrderDate;
                order.DeliveryDate = model.DeliveryDate;
                order.Remarks = model.Remarks;
                order.AgreedUnitPrice = model.AgreedUnitPrice;
                order.AgreedGstPercent = model.AgreedGstPercent;
                order.CustomerReference = model.CustomerReference?.Trim();

                var oldMaterials = _context.ProductionMaterial.Where(m => m.ProductionOrderId == order.ProductionOrderId);
                _context.ProductionMaterial.RemoveRange(oldMaterials);
                await _context.SaveChangesAsync();

                await GenerateMaterialsAsync(order, sheet);

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "Production Order updated successfully.",
                    id = order.ProductionOrderId
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return BadRequest(new { success = false, message = ex is InvalidOperationException ? ex.Message : "The operation could not be saved. Refresh and retry." });
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // PRINT
        // ════════════════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Print(int id)
        {
            var order = await _context.ProductionOrder
                .Include(o => o.CostSheet)
                .Include(o => o.Materials)
                .FirstOrDefaultAsync(o => o.ProductionOrderId == id);

            if (order == null) return NotFound();

            ViewBag.CustomerName = await _context.Parties
                .Where(p => p.PartyId.ToString() == order.CustomerId)
                .Select(p => p.PartyName)
                .FirstOrDefaultAsync() ?? order.CustomerId;

            return View(order);
        }

        // ── Helper: auto-generate material requirements from cost sheet plies ──
        private Task GenerateMaterialsAsync(ProductionOrder order, CostSheet sheet)
            => new EstimatedProductionService(_context, _stock).SnapshotAsync(order, sheet);

        // ════════════════════════════════════════════════════════════════════════
        // MATERIALS — View + refresh material requirements
        // ════════════════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Materials(int id)
        {
            var order = await _context.ProductionOrder
                .Include(o => o.CostSheet)
                .Include(o => o.Materials)
                .FirstOrDefaultAsync(o => o.ProductionOrderId == id);

            if (order == null) return NotFound();
            int branchId = order.UsesEstimatedConsumption ? order.BranchId : order.CostSheet?.BranchID ?? 1;

            // Refresh live stock values
            foreach (var mat in order.Materials ?? [])
            {
                var stock = await _context.Stock
                    .FirstOrDefaultAsync(s => s.ItemId == mat.ItemId
                                           && s.BranchId == branchId);
                mat.AvailableKg = stock?.Quantity ?? 0;
                mat.ShortfallKg = Math.Max(0, mat.RequiredKg - mat.AvailableKg);
                mat.StockStatus = mat.ShortfallKg == 0 ? "OK"
                                : mat.ShortfallKg < mat.RequiredKg * 0.5m ? "Low"
                                : "Critical";
            }

            ViewBag.CustomerName = await _context.Parties
                .Where(p => p.PartyId.ToString() == order.CustomerId)
                .Select(p => p.PartyName)
                .FirstOrDefaultAsync() ?? order.CustomerId;

            ViewBag.TotalOrderValue = order.OrderQty * (order.UsesEstimatedConsumption ? order.EstimatedRate : order.CostSheet?.FinalRateWithGST ?? 0);

            return View(order);
        }

        // ════════════════════════════════════════════════════════════════════════
        // CONSUME — Show production consumption form
        // ════════════════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Consume(int id)
        {
            var order = await _context.ProductionOrder
                .Include(o => o.CostSheet)
                .Include(o => o.Materials)
                .FirstOrDefaultAsync(o => o.ProductionOrderId == id);

            if (order == null) return NotFound();

            // Check if already consumed
            var alreadyConsumed = await _context.ProductionConsumption
                .AnyAsync(c => c.ProductionOrderId == id);

            ViewBag.AlreadyConsumed = alreadyConsumed;
            ViewBag.CustomerName = await _context.Parties
                .Where(p => p.PartyId.ToString() == order.CustomerId)
                .Select(p => p.PartyName)
                .FirstOrDefaultAsync() ?? order.CustomerId;

            return View(order);
        }

        // ════════════════════════════════════════════════════════════════════════
        // SAVE CONSUMPTION — Record actual usage + deduct stock
        // ════════════════════════════════════════════════════════════════════════
        [HttpPost]
        public Task<IActionResult> SaveConsumption([FromBody] SaveConsumptionRequest model)
            => Task.FromResult<IActionResult>(BadRequest(new { success = false, message = "Use Record Production: enter boxes produced; material consumption is estimated automatically." }));

        [HttpPost]
        public async Task<IActionResult> Record([FromBody] RecordProductionRequest model)
        {
            try
            {
                await new EstimatedProductionService(_context, _stock).RecordAsync(model.ProductionOrderId, model.ProducedQty);
                return Ok(new { success = true, message = "Production recorded. Estimated materials deducted and finished boxes added to stock." });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> Reverse([FromBody] RecordProductionRequest model)
        {
            try
            {
                await new EstimatedProductionService(_context, _stock).ReverseAsync(model.ProductionOrderId, model.Reason);
                return Ok(new { success = true, message = "Production reversed. Materials restored and finished boxes removed. Create a new order for corrected production." });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        public IActionResult Complete(int id)
            => BadRequest(new { success = false, message = "Use Record Production to complete the order and post stock together." });

        // ════════════════════════════════════════════════════════════════════════
        // CANCEL
        // ════════════════════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> Cancel(int id)
        {
            var order = await _context.ProductionOrder.FindAsync(id);
            if (order == null) return NotFound();

            if (order.Status != "Pending")
                return BadRequest(new { success = false, message = "Only an unstarted order can be cancelled." });
            order.Status = "Cancelled";
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Production Order cancelled." });
        }

        // ════════════════════════════════════════════════════════════════════════
        // DELETE
        // ════════════════════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var order = await _context.ProductionOrder
                .Include(o => o.Materials)
                .Include(o => o.Consumptions)
                .FirstOrDefaultAsync(o => o.ProductionOrderId == id);

            if (order == null) return NotFound();

            if (order.Status != "Pending" || order.Consumptions!.Any())
                return BadRequest(new { success = false, message = "Started production cannot be deleted." });
            _context.ProductionMaterial.RemoveRange(order.Materials!);
            _context.ProductionConsumption.RemoveRange(order.Consumptions!);
            _context.ProductionOrder.Remove(order);
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }
    }
}
