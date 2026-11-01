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

        [HttpGet]
        public async Task<IActionResult> GetCustomerOptions(int? orderId=null)
        {
            var groups=await _context.ItemCategory.AsNoTracking().Where(g=>g.GroupType=="FinishedGoods"&&g.IsActive==true).OrderBy(g=>g.CategoryName).ToListAsync();
            var parties=await _context.Parties.AsNoTracking().Where(p=>p.Status=="active"&&(p.PartyType=="customer"||p.PartyType=="both")).ToListAsync();
            var customers=groups.Select(g=>{
                var party=parties.FirstOrDefault(p=>p.PartyCode==FinishedGoodsCustomers.Prefix+g.CategoryId);
                if(party==null){var matches=parties.Where(p=>p.PartyName==g.CategoryName&&string.IsNullOrEmpty(p.PartyCode)).ToList();if(matches.Count==1)party=matches[0];}
                return new{selectionId="group:"+g.CategoryId,partyName=g.CategoryName,partyId=party?.PartyId};
            }).ToList();
            if(orderId.HasValue)
            {
                var current=await _context.ProductionOrder.Where(o=>o.ProductionOrderId==orderId).Select(o=>o.CustomerId).SingleOrDefaultAsync();
                if(int.TryParse(current,out var id)&&!customers.Any(c=>c.partyId==id))
                {
                    var legacy=parties.FirstOrDefault(p=>p.PartyId==id&&!((p.PartyCode??"").StartsWith(FinishedGoodsCustomers.Prefix)));
                    if(legacy!=null)customers.Add(new{selectionId=id.ToString(),partyName=legacy.PartyName+" (saved customer)",partyId=(int?)id});
                }
            }
            return Json(new{success=true,customers});
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
                    HasBatches = _context.ProductionBatch.Any(b => b.ProductionOrderId == o.ProductionOrderId),
                    Invoiced = _context.SaleInvoiceBody.Where(b => _context.SaleInvoice.Any(x => x.SaleId == b.SaleId && x.ProductionOrderId == o.ProductionOrderId)).Sum(b => (decimal?)(b.Quantity ?? 0)) ?? 0m,
                    o.ClosedShortUtc,
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
        // SuperAdmin, or a user whose role has the CostSheet.Edit permission.
        private async Task<bool> CanEditCostSheetsAsync()
        {
            if (HttpContext?.User == null) return false;
            if (HttpContext.User.IsInRole("SuperAdmin")) return true;
            var permissions = HttpContext.RequestServices.GetService<IPermissionService>();
            return permissions != null && int.TryParse(HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId)
                && await permissions.HasPermissionAsync(userId, "CostSheet.Edit");
        }

        [HttpGet]
        public async Task<IActionResult> GetCostSheetInfo(int id)
        {
            var sheet=await _context.CostSheet.AsNoTracking().Include(c=>c.Plies).Include(c=>c.Materials).SingleOrDefaultAsync(c=>c.CostSheetId==id);
            if(sheet==null)return NotFound(new{success=false});
            // Each problem names what is missing so the screen can say exactly what to fix.
            var problems=new List<(string Key,string Title,string Detail)>();
            var finishedReady=await _context.Items.AnyAsync(i=>i.ItemID==sheet.FinishedItemId&&i.IsActive==true&&i.Category.IsActive==true&&i.ItemType=="FinishedGoods"&&i.StockUnit=="pcs");
            if(sheet.Status!="Final")problems.Add(("final","Cost sheet is not Final","Mark the cost sheet as Final before using it for production."));
            if(!finishedReady)problems.Add(("finished","Finished product is not linked","Link an active finished product stocked in PCS to this cost sheet."));
            var plyInputs=(sheet.Plies??[]).Where(p=>p.KGs>0).ToList();
            var materialInputs=(sheet.Materials??[]).Where(m=>m.Quantity>0).ToList();
            var glueUsed=sheet.GlueKgPerBox>0;var silicateUsed=sheet.SilicateKgPerBox>0;
            if(plyInputs.Count==0&&!glueUsed&&!silicateUsed&&materialInputs.Count==0)problems.Add(("estimates","No material quantities per box","Add the material quantities per box to this cost sheet."));
            var candidateIds=plyInputs.Select(p=>p.ItemId).Concat(materialInputs.Select(m=>(long?)m.ItemId)).Append(sheet.GlueItemId).Append(sheet.SilicateItemId).Where(x=>x.HasValue).Select(x=>x!.Value).Distinct().ToList();
            var validIds=(await _context.Items.Where(i=>candidateIds.Contains(i.ItemID)&&i.IsActive==true&&i.Category.IsActive==true&&i.ItemType=="RawMaterial"&&(i.StockUnit=="kg"||i.StockUnit=="pcs"||i.StockUnit=="in")).Select(i=>i.ItemID).ToListAsync()).ToHashSet();
            bool Linked(long? id)=>id.HasValue&&validIds.Contains(id.Value);
            const string rawRule="Choose an active Raw Material item from your inventory (stocked in kg, pcs or in).";
            var unlinkedPlies=plyInputs.Where(p=>!Linked(p.ItemId)).Select(p=>string.IsNullOrWhiteSpace(p.PlyName)?"Layer "+p.SrNo:p.PlyName!.Trim()).ToList();
            if(unlinkedPlies.Count>0)problems.Add(("plies",unlinkedPlies.Count+" of "+plyInputs.Count+" paper layer"+(plyInputs.Count==1?"":"s")+" not linked to inventory",string.Join(", ",unlinkedPlies)+". "+rawRule));
            if(glueUsed&&!Linked(sheet.GlueItemId))problems.Add(("glue","Glue stock item is not linked",rawRule));
            if(silicateUsed&&!Linked(sheet.SilicateItemId))problems.Add(("silicate","Silicate stock item is not linked",rawRule));
            var unlinkedOthers=materialInputs.Where(m=>!Linked(m.ItemId)).Count();
            if(unlinkedOthers>0)problems.Add(("others",unlinkedOthers+" other material line"+(unlinkedOthers==1?"":"s")+" not linked to inventory",rawRule));
            if(sheet.HasLamination==true&&!string.IsNullOrEmpty(sheet.ClientCostingJson))
            {
                try{
                    var options=System.Text.Json.JsonSerializer.Deserialize<ClientCostingOptions>(sheet.ClientCostingJson);
                    if(options!=null){
                        foreach(var link in new[]{(Key:"laminationGlue",Name:"glue lamination",Count:options.GlueLaminationApplications,Id:options.GlueLaminationItemId),(Key:"laminationChemical",Name:"lamination chemical",Count:options.LaminationChemicalApplications,Id:options.LaminationChemicalItemId)})
                            if(link.Count>0&&!await _context.Items.AnyAsync(i=>i.ItemID==link.Id&&i.IsActive==true&&i.Category.IsActive==true&&i.ItemType=="RawMaterial"&&i.StockUnit=="kg"))problems.Add((link.Key,"The "+link.Name+" stock item is not linked","Choose an active Raw Material item stocked in KG."));
                    }
                }catch(System.Text.Json.JsonException){problems.Add(("lamination","Lamination settings need review","Open the cost sheet and review the saved lamination settings."));}
            }
            var issues=problems.Select(p=>p.Title).ToList();
            var products=await _context.Items.Where(i=>i.IsActive==true&&i.Category.IsActive==true&&i.ItemType=="FinishedGoods"&&i.StockUnit=="pcs").OrderBy(i=>i.ItemName).Select(i=>new{id=i.ItemID,name=i.ItemName,group=i.Category.CategoryName}).ToListAsync();
            return Json(new{success=true,data=new{sheet.CostSheetId,sheet.ItemName,sheet.BoxStyle,sheet.FinalRateWithGST,sheet.FinalCostWOGST,sheet.FinishedItemId,finishedReady,issues,problems=problems.Select(p=>new{p.Key,p.Title,p.Detail}),canEditSheet=await CanEditCostSheetsAsync(),products}});
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
                    return BadRequest(new { success = false, message = "Complete the product link shown in the cost-sheet setup panel before creating production." });

                model.CustomerId=await FinishedGoodsCustomers.ResolveAsync(_context,model.CustomerId);

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

                model.CustomerId=await FinishedGoodsCustomers.ResolveAsync(_context,model.CustomerId);
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
            ViewBag.Batches = await _context.ProductionBatch.AsNoTracking().Where(b => b.ProductionOrderId == id).OrderBy(b => b.BatchNo).ToListAsync();
            ViewBag.Sales = await _context.SaleInvoice.IgnoreQueryFilters()
                .Where(s => s.ProductionOrderId == id && (!_context.RestrictBranches || _context.AllowedBranchIds.Contains(s.BranchID ?? 1)))
                .OrderBy(s => s.SaleId)
                .Select(s => new OrderSaleRow(s.SaleId, s.SaleDate, s.IsCancelled, s.NetAmount ?? 0,
                    _context.SaleInvoiceBody.Where(b => b.SaleId == s.SaleId).Sum(b => (decimal?)(b.Quantity ?? 0)) ?? 0))
                .ToListAsync();

            return View(order);
        }

        // ── Helper: auto-generate material requirements from cost sheet plies ──
        private Task GenerateMaterialsAsync(ProductionOrder order, CostSheet sheet)
            => new EstimatedProductionService(_context, _stock).SnapshotAsync(order, sheet);

        // ════════════════════════════════════════════════════════════════════════
        // MATERIALS — View + refresh material requirements
        // ════════════════════════════════════════════════════════════════════════
        // Total materials still needed by open orders against current stock.
        [HttpGet]
        public async Task<IActionResult> PurchaseNeeds() => View(await PurchaseNeedsService.BuildAsync(_context));

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
            ViewBag.Batches = await _context.ProductionBatch.AsNoTracking().Where(b => b.ProductionOrderId == id).OrderBy(b => b.BatchNo).ToListAsync();
            ViewBag.Invoiced = await OrderPricing.InvoicedQuantity(_context, id);
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
                await new EstimatedProductionService(_context, _stock).RecordAsync(model.ProductionOrderId, model.ProducedQty, User.Identity?.Name);
                return Ok(new { success = true, message = "Batch recorded. Estimated materials deducted and finished boxes added to stock." });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
            catch (DbUpdateConcurrencyException) { return Conflict(new { success = false, message = "This order changed while you were recording. Reload and try again." }); }
        }

        [HttpPost]
        public async Task<IActionResult> Reverse([FromBody] RecordProductionRequest model)
        {
            try
            {
                var service = new EstimatedProductionService(_context, _stock);
                if (model.BatchId > 0)
                {
                    await service.ReverseBatchAsync(model.BatchId, model.Reason);
                    return Ok(new { success = true, message = "Batch reversed. Its materials were restored and its boxes removed from stock." });
                }
                await service.ReverseAsync(model.ProductionOrderId, model.Reason);
                return Ok(new { success = true, message = "Production reversed. Materials restored and finished boxes removed. Create a new order for corrected production." });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
            catch (DbUpdateConcurrencyException) { return Conflict(new { success = false, message = "This order changed while you were reversing. Reload and try again." }); }
        }

        [HttpPost]
        public async Task<IActionResult> Complete([FromBody] RecordProductionRequest model)
        {
            try
            {
                await new EstimatedProductionService(_context, _stock).CloseShortAsync(model.ProductionOrderId, model.Reason);
                return Ok(new { success = true, message = "Order closed. Boxes already produced stay in stock and can be invoiced." });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
            catch (DbUpdateConcurrencyException) { return Conflict(new { success = false, message = "This order changed. Reload and try again." }); }
        }

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

    public record OrderSaleRow(int SaleId, DateTime? SaleDate, bool IsCancelled, decimal NetAmount, decimal Boxes);
}
