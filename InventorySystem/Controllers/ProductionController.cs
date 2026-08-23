using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
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
                    o.OrderQty,
                    o.OrderDate,
                    o.DeliveryDate,
                    o.Status,
                    o.CostSheetId,
                    BoxStyle = o.CostSheet != null ? o.CostSheet.BoxStyle : null,
                    FinalRate = o.CostSheet != null ? o.CostSheet.FinalRateWithGST : null,
                    TotalValue = o.OrderQty * (o.CostSheet != null ? o.CostSheet.FinalRateWithGST ?? 0 : 0),
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
                    c.FinalRateWithGST,
                    c.CustomerId
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
                    c.CustomerId,
                    CustomerName = _context.Parties
                        .Where(p => p.PartyId.ToString() == c.CustomerId)
                        .Select(p => p.PartyName)
                        .FirstOrDefault() ?? c.CustomerId
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
            if (model == null)
                return BadRequest(new { success = false, message = "Invalid data." });

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var sheet = await _context.CostSheet
                    .Include(c => c.Plies)
                    .FirstOrDefaultAsync(c => c.CostSheetId == model.CostSheetId);

                if (sheet == null)
                    return BadRequest(new { success = false, message = "Cost Sheet not found." });

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
                return StatusCode(500, new { success = false, message = ex.Message });
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
                    o.OrderQty,
                    o.OrderDate,
                    o.DeliveryDate,
                    o.Status,
                    o.Remarks
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
                    c.FinalRateWithGST,
                    c.CustomerId
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
            if (model == null)
                return BadRequest(new { success = false, message = "Invalid data." });

            using var tx = await _context.Database.BeginTransactionAsync();
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
                        message = "Cannot edit — production has already started or been completed/cancelled. Cancel this order and create a new one instead."
                    });

                var sheet = await _context.CostSheet
                    .Include(c => c.Plies)
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
                return StatusCode(500, new { success = false, message = ex.Message });
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
        private async Task GenerateMaterialsAsync(ProductionOrder order, CostSheet sheet)
        {
            if (sheet.Plies == null) return;

            foreach (var ply in sheet.Plies.Where(p => p.ItemId.HasValue && p.KGs > 0))
            {
                var kgPerUnit = ply.KGs ?? 0;
                var requiredKg = kgPerUnit * order.OrderQty;

                var stock = await _context.Stock
                    .FirstOrDefaultAsync(s => s.ItemId == ply.ItemId!.Value
                                           && s.BranchId == (sheet.BranchID ?? 1));
                var availableKg = stock?.Quantity ?? 0;
                var shortfall = Math.Max(0, requiredKg - availableKg);

                var itemName = await _context.Items
                    .Where(i => i.ItemID == ply.ItemId!.Value)
                    .Select(i => i.ItemName)
                    .FirstOrDefaultAsync() ?? ply.PlyName;

                string stockStatus = shortfall == 0 ? "OK"
                                   : shortfall < requiredKg * 0.5m ? "Low"
                                   : "Critical";

                _context.ProductionMaterial.Add(new ProductionMaterial
                {
                    ProductionOrderId = order.ProductionOrderId,
                    ItemId = ply.ItemId!.Value,
                    ItemName = itemName,
                    Specification = ply.Specification,
                    KgPerUnit = kgPerUnit,
                    RequiredKg = requiredKg,
                    AvailableKg = availableKg,
                    ShortfallKg = shortfall,
                    StockStatus = stockStatus
                });
            }
        }

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
            int branchId = order.CostSheet?.BranchID ?? 1;

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

            ViewBag.TotalOrderValue = order.OrderQty * (order.CostSheet?.FinalRateWithGST ?? 0);

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
        public async Task<IActionResult> SaveConsumption([FromBody] SaveConsumptionRequest model)
        {
            if (model == null || model.Items == null || !model.Items.Any())
                return BadRequest(new { success = false, message = "No consumption data." });

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var order = await _context.ProductionOrder
                    .Include(o => o.CostSheet)
                    .FirstOrDefaultAsync(o => o.ProductionOrderId == model.ProductionOrderId);

                if (order == null)
                    return BadRequest(new { success = false, message = "Production Order not found." });

                // Remove previous consumption if re-submitting
                var existing = _context.ProductionConsumption
                    .Where(c => c.ProductionOrderId == model.ProductionOrderId);
                _context.ProductionConsumption.RemoveRange(existing);

                int branchId = order.CostSheet?.BranchID ?? 1;

                foreach (var item in model.Items)
                {
                    if (item.ActualKg <= 0) continue;

                    // Deduct from Stock — never below zero, and only if a stock row exists (matches pre-refactor behavior)
                    var currentStock = await _context.Stock.FirstOrDefaultAsync(s => s.ItemId == item.ItemId && s.BranchId == branchId);
                    if (currentStock == null) continue;
                    var deductQty = Math.Min(item.ActualKg, currentStock.Quantity);
                    if (deductQty > 0)
                        await _stock.AdjustAsync(item.ItemId, branchId, -deductQty);

                    _context.ProductionConsumption.Add(new ProductionConsumption
                    {
                        ProductionOrderId = model.ProductionOrderId,
                        ItemId = item.ItemId,
                        ItemName = item.ItemName,
                        PlannedKg = item.PlannedKg,
                        ActualKg = item.ActualKg,
                        VarianceKg = item.ActualKg - item.PlannedKg,
                        ConsumedDate = DateTime.Now
                    });
                }

                // Update order status to InProduction
                order.Status = "InProduction";

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { success = true, message = "Consumption recorded. Stock updated." });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // COMPLETE — Mark production order as completed
        // ════════════════════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> Complete(int id)
        {
            var order = await _context.ProductionOrder.FindAsync(id);
            if (order == null) return NotFound();

            order.Status = "Completed";
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Production Order marked as Completed." });
        }

        // ════════════════════════════════════════════════════════════════════════
        // CANCEL
        // ════════════════════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> Cancel(int id)
        {
            var order = await _context.ProductionOrder.FindAsync(id);
            if (order == null) return NotFound();

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

            _context.ProductionMaterial.RemoveRange(order.Materials!);
            _context.ProductionConsumption.RemoveRange(order.Consumptions!);
            _context.ProductionOrder.Remove(order);
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }
    }
}