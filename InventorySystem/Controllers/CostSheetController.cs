using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Claims;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class CostSheetController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IPermissionService _permissionService;

        public CostSheetController(ApplicationDbContext context, IPermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult DownloadExcelTemplate([FromServices] IWebHostEnvironment environment)
        {
            var path = Path.Combine(environment.WebRootPath, "templates", "Bright_Packages_Cost_Sheet_Template.xlsx");
            if (!System.IO.File.Exists(path)) return NotFound("The Excel template is unavailable.");
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            // A download filename sets Content-Disposition: attachment. Binary MIME avoids Office preview handling.
            return File(System.IO.File.ReadAllBytes(path), "application/octet-stream", "B_Packages_Cost_Sheet_Template.xlsx");
        }

        // ── List ────────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var permission = await this.CheckPermissionAsync(_permissionService, "CostSheet.View");
            if (permission != null) return permission;
            ViewBag.CanEditSheets = User.IsInRole("SuperAdmin") || await this.CheckPermissionAsync(_permissionService, "CostSheet.Edit") == null;

            var sheets = await _context.CostSheet
                .OrderByDescending(c => c.SheetDate)
                .Select(c => new
                {
                    c.CostSheetId,
                    c.SheetDate,
                    c.ItemName,
                    c.BoxStyle,
                    c.Status,
                    c.FinalCostWOGST,
                    c.FinalRateWithGST
                })
                .ToListAsync();

            return View(sheets);
        }

        // ── Create (GET) ────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var permission = await this.CheckPermissionAsync(_permissionService, "CostSheet.Create");
            if (permission != null) return permission;

            return View();
        }

        // Parsing creates only a preview. Existing Save rules remain authoritative.
        [HttpPost]
        [RequestSizeLimit(CostSheetExcelImport.MaxFileBytes + 65536)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportExcel(IFormFile? file, int costSheetId = 0)
        {
            var permission = await this.CheckPermissionAsync(_permissionService, costSheetId > 0 ? "CostSheet.Edit" : "CostSheet.Create");
            if (permission != null) return permission;
            if (costSheetId < 0) return BadRequest(new { success = false, message = "Invalid cost sheet." });
            if (costSheetId > 0 && !await _context.CostSheet.AnyAsync(x => x.CostSheetId == costSheetId)) return NotFound();
            if (file == null || file.Length == 0 || file.Length > CostSheetExcelImport.MaxFileBytes || !string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Choose an Excel .xlsx file up to 5 MB using the sample cost-sheet format." });
            try
            {
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                if (stream.Length > CostSheetExcelImport.MaxFileBytes) return BadRequest(new { success = false, message = "The Excel file exceeds 5 MB." });
                stream.Position = 0;
                var preview = CostSheetExcelImport.Read(stream);
                // Spreadsheet numbers must never be interpreted as inventory IDs.
                var items = await _context.Items.Where(i => i.IsActive == true && i.Category.IsActive == true).ToListAsync();
                foreach (var ply in preview.Data.Plies)
                {
                    var matches = items.Where(i => i.ItemType == "RawMaterial" && i.StockUnit == "kg" && string.Equals(i.ItemName?.Trim(), ply.Specification, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (matches.Count == 1) { ply.ItemId = matches[0].ItemID; ply.PlyName = matches[0].ItemName; }
                }
                var finished = items.Where(i => i.ItemType == "FinishedGoods" && i.StockUnit == "pcs" && string.Equals(i.ItemName?.Trim(), preview.Data.ItemName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (finished.Count == 1) preview.Data.FinishedItemId = finished[0].ItemID;
                return Ok(new { success = true, preview });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        }

        // ── Edit (GET) ──────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var permission = await this.CheckPermissionAsync(_permissionService, "CostSheet.Edit");
            if (permission != null) return permission;

            var sheet = await _context.CostSheet
                .Include(c => c.Plies)
                .Include(c => c.Materials)
                .FirstOrDefaultAsync(c => c.CostSheetId == id);

            if (sheet == null) return NotFound();
            return View("Create", sheet);
        }

        // ── Save (POST) ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Save([FromBody] CostSheetViewModel model)
        {
            if (model == null)
                return BadRequest("Invalid data.");

            try { await CostSheetCalculation.ValidateAndCalculateAsync(_context, model); }
            catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
            using var tx = await _context.Database.BeginAtomicTransactionAsync();
            try
            {
                CostSheet sheet;

                if (model.CostSheetId > 0)
                {
                    // ── Update existing ──────────────────────────────────────
                    sheet = await _context.CostSheet
                        .Include(c => c.Plies)
                        .Include(c => c.Materials)
                        .FirstOrDefaultAsync(c => c.CostSheetId == model.CostSheetId)
                        ?? throw new Exception("Cost sheet not found.");

                    // Remove old plies and other materials
                    _context.CostSheetPly.RemoveRange(sheet.Plies!);
                    _context.CostSheetMaterial.RemoveRange(sheet.Materials!);
                }
                else
                {
                    // ── New ──────────────────────────────────────────────────
                    sheet = new CostSheet { CreatedDate = DateTime.Now };
                    _context.CostSheet.Add(sheet);
                }

                MapToEntity(model, sheet);
                await _context.SaveChangesAsync();

                // ── Validate ply item references before saving ─────────────
                if (model.Plies != null)
                {
                    var plyItemIds = model.Plies
                        .Where(p => p.ItemId.HasValue)
                        .Select(p => p.ItemId!.Value)
                        .Distinct()
                        .ToList();

                    if (plyItemIds.Any())
                    {
                        var existingIds = await _context.Items
                            .Where(i => plyItemIds.Contains(i.ItemID))
                            .Select(i => i.ItemID)
                            .ToListAsync();

                        var missingIds = plyItemIds.Where(id => !existingIds.Contains(id)).ToList();
                        if (missingIds.Any())
                        {
                            await tx.RollbackAsync();
                            return StatusCode(400, new { success = false, message = $"Ply item ID {missingIds.First()} does not exist." });
                        }
                    }
                }

                // ── Save plies ───────────────────────────────────────────────
                if (model.Plies != null)
                {
                    int srno = 1;
                    foreach (var ply in model.Plies)
                    {
                        _context.CostSheetPly.Add(new CostSheetPly
                        {
                            CostSheetId   = sheet.CostSheetId,
                            SrNo          = srno++,
                            ItemId        = ply.ItemId,        // ← FIXED: was missing
                            PlyName       = ply.PlyName,
                            Specification = ply.Specification,
                            GSM           = ply.GSM,
                            Rate          = ply.Rate,
                            KGs           = ply.KGs,
                            Cost          = ply.Cost
                        });
                    }
                }

                int matNo = 1;
                foreach (var other in model.Materials)
                {
                    _context.CostSheetMaterial.Add(new CostSheetMaterial
                    {
                        CostSheetId = sheet.CostSheetId,
                        SrNo        = matNo++,
                        ItemId      = other.ItemId!.Value,
                        Unit        = other.Unit ?? "pcs",
                        Quantity    = other.Quantity ?? 0,
                        Rate        = other.Rate ?? 0,
                        Cost        = other.Cost ?? 0
                    });
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { success = true, id = sheet.CostSheetId, message = "Cost sheet saved successfully." });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ── Mark a Draft sheet Final without opening the full editor ────────────
        [HttpPost]
        public async Task<IActionResult> MarkFinal(int id)
        {
            var sheet = await _context.CostSheet.Include(c => c.Plies).FirstOrDefaultAsync(c => c.CostSheetId == id);
            if (sheet == null) return NotFound();
            if (sheet.Status == "Final") return Ok(new { success = true, message = "Already Final." });
            if (sheet.Plies == null || sheet.Plies.Count == 0 || sheet.Plies.Any(p => p.KGs is null or <= 0))
                return BadRequest(new { success = false, message = "Enter paper kg per box for each selected layer before marking Final." });
            sheet.Status = "Final";
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cost sheet marked Final." });
        }

        // ── Delete ──────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var sheet = await _context.CostSheet
                .Include(c => c.Plies)
                .Include(c => c.Materials)
                .FirstOrDefaultAsync(c => c.CostSheetId == id);

            if (sheet == null) return NotFound();

            if (await _context.ProductionOrder.AnyAsync(o => o.CostSheetId == id))
                return BadRequest(new { success = false, message = "A cost sheet used by production cannot be deleted. Keep its history." });
            _context.CostSheetPly.RemoveRange(sheet.Plies!);
            _context.CostSheetMaterial.RemoveRange(sheet.Materials!);
            _context.CostSheet.Remove(sheet);
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }

        // ── Get Settings ────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetSettings()
        {
            var s = await _context.CostSheetSettings.FirstOrDefaultAsync()
                    ?? new CostSheetSettings();

            return Json(new
            {
                labourRate = s.LabourRate,
                energyRate = s.EnergyRate,
                wastePercentage = s.WastePercentage,
                adminExpPercentage = s.AdminExpPercentage,
                sellingDistPercentage = s.SellingDistPercentage,
                repairMaintenancePercentage = s.RepairMaintenancePercentage,
                storeSparesPercentage = s.StoreSparesPercentage,
                manufacturingCostPercentage = s.ManufacturingCostPercentage,
                defaultFreightRate = s.DefaultFreightRate,
                defaultProfitPct = s.DefaultProfitPct
            });
        }

        // ── Settings Page (GET) ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Settings()
        {
            var permission = await this.CheckPermissionAsync(_permissionService, "CostSheet.Settings");
            if (permission != null) return permission;

            var s = await _context.CostSheetSettings.FirstOrDefaultAsync()
                    ?? new CostSheetSettings();
            return View(s);
        }

        // ── Save Settings (POST) ────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> SaveSettings([FromBody] CostSheetSettingsViewModel model)
        {
            if (model == null || new[] { model.LabourRate, model.EnergyRate, model.DefaultFreightRate }.Any(v => v < 0 || v > 100000000) ||
                new[] { model.WastePercentage, model.AdminExpPercentage, model.SellingDistPercentage,
                    model.RepairMaintenancePercentage, model.StoreSparesPercentage,
                    model.ManufacturingCostPercentage, model.DefaultProfitPct }.Any(v => v < 0 || v > 100))
                return BadRequest(new { success = false, message = "Enter nonnegative rates and percentages between 0 and 100." });
            var s = await _context.CostSheetSettings.FirstOrDefaultAsync();

            if (s == null)
            {
                s = new CostSheetSettings();
                _context.CostSheetSettings.Add(s);
            }

            s.LabourRate = model.LabourRate;
            s.EnergyRate = model.EnergyRate;
            s.WastePercentage = model.WastePercentage;
            s.AdminExpPercentage = model.AdminExpPercentage;
            s.SellingDistPercentage = model.SellingDistPercentage;
            s.RepairMaintenancePercentage = model.RepairMaintenancePercentage;
            s.StoreSparesPercentage = model.StoreSparesPercentage;
            s.ManufacturingCostPercentage = model.ManufacturingCostPercentage;
            s.DefaultFreightRate = model.DefaultFreightRate;
            s.DefaultProfitPct = model.DefaultProfitPct;
            s.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Settings saved successfully." });
        }

        // ── Get Cost Sheet JSON (for edit) ──────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetById(int id)
        {
            var sheet = await _context.CostSheet
                .Include(c => c.Plies!.OrderBy(p => p.SrNo))
                .Include(c => c.Materials!.OrderBy(p => p.SrNo))
                .FirstOrDefaultAsync(c => c.CostSheetId == id);

            if (sheet == null) return NotFound();

            // Project to anonymous DTO to avoid circular reference serialization crash
            return Json(new
            {
                success = true,
                data = new
                {
                    sheet.CostSheetId,
                    sheetDate        = sheet.SheetDate?.ToString("yyyy-MM-dd"),
                    sheet.CustomerId,
                    sheet.ItemName,
                    clientCosting = string.IsNullOrEmpty(sheet.ClientCostingJson) ? null : System.Text.Json.JsonSerializer.Deserialize<ClientCostingOptions>(sheet.ClientCostingJson),
                    sheet.FinishedItemId, sheet.GlueItemId, sheet.SilicateItemId, sheet.GlueKgPerBox, sheet.SilicateKgPerBox,
                    sheet.BranchID,
                    sheet.Status,
                    sheet.BoxStyle,
                    sheet.SizeType,
                    sheet.SizeUnit,
                    sheet.Length,    sheet.Width,  sheet.Height, sheet.Flap,
                    sheet.AutoFlapGap,
                    sheet.SheetWidthIn,  sheet.SheetLengthIn, sheet.WithFlap,
                    sheet.AdjWidthIn,    sheet.AdjLength1In,  sheet.AdjLength2In,
                    sheet.AdjWidthMm,    sheet.AdjLengthMm,   sheet.TotalSheetSqIn,
                    sheet.PrintingType,  sheet.PrintingColors, sheet.PrintingCost,
                    sheet.DickelCost,
                    sheet.HasLamination, sheet.LaminationCost,
                    sheet.GlueRate,      sheet.SilicateRate,
                    sheet.GlueSilicateKg, sheet.GlueSilicateCost,
                    sheet.PaperCost,
                    sheet.LabourRate,    sheet.LabourCost,
                    sheet.EnergyRate,    sheet.EnergyCost,
                    sheet.BindingType,   sheet.BindingPins,   sheet.BindingCost,
                    sheet.FreightRate,   sheet.FreightCost,
                    sheet.SubTotal,
                    sheet.WastePct,      sheet.WasteCost,
                    sheet.AdminExpPct,   sheet.AdminExpCost,
                    sheet.SellingDistPct, sheet.SellingDistCost,
                    sheet.RepairMaintPct, sheet.RepairMaintCost,
                    sheet.StoreSparesPct, sheet.StoreSparesCost,
                    sheet.MfgCostPct,    sheet.MfgCostValue,
                    sheet.ManufacturingTotal,
                    sheet.CommPersonA,   sheet.CommPersonAPct, sheet.CommPersonACost,
                    sheet.CommPersonB,   sheet.CommPersonBPct, sheet.CommPersonBCost,
                    sheet.WHTaxPct,
                    gSTPercentage    = sheet.GSTPercentage,
                    sheet.ProfitPct,     sheet.ProfitAmount,
                    sheet.FinalCostWOGST,
                    mOQ              = sheet.MOQ,
                    sheet.TaxPct,
                    sheet.FinalRateWithGST,
                    sheet.Remarks,
                    plies = sheet.Plies!.Select(p => new
                    {
                        p.PlyId,
                        p.SrNo,
                        p.ItemId,
                        p.PlyName,
                        p.Specification,
                        gSM  = p.GSM,
                        p.Rate,
                        kGs  = p.KGs,
                        p.Cost
                    }).ToList(),
                    materials = sheet.Materials!.Select(x => new
                    {
                        x.ItemId, x.Unit, x.Quantity, x.Rate, x.Cost
                    }).ToList()
                }
            });
        }

        public sealed class FinishedLink { public int CostSheetId{get;set;} public long FinishedItemId{get;set;} }
        [HttpPost]
        public async Task<IActionResult> UpdateFinishedItemLink([FromBody] FinishedLink? model)
        {
            if(model==null||model.CostSheetId<=0||model.FinishedItemId<=0)return BadRequest(new{success=false,message="Select a cost sheet and finished product."});
            var sheet=await _context.CostSheet.SingleOrDefaultAsync(c=>c.CostSheetId==model.CostSheetId);
            if(sheet==null||sheet.Status!="Final")return BadRequest(new{success=false,message="Select a Final cost sheet."});
            if(sheet.FinishedItemId.HasValue&&sheet.FinishedItemId!=model.FinishedItemId)return Conflict(new{success=false,message="This sheet already has a product link. Reload it before continuing."});
            if(!await _context.Items.AnyAsync(i=>i.ItemID==model.FinishedItemId&&i.IsActive==true&&i.Category.IsActive==true&&i.ItemType=="FinishedGoods"&&i.StockUnit=="pcs"))return BadRequest(new{success=false,message="Select an active Finished Goods item stocked in PCS."});
            sheet.FinishedItemId=model.FinishedItemId;await _context.SaveChangesAsync();
            return Ok(new{success=true,message="Finished product linked. You can continue production setup."});
        }

        // ── Print ───────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Print(int id)
        {
            var sheet = await _context.CostSheet
                .Include(c => c.Plies)
                .Include(c => c.Materials)
                .FirstOrDefaultAsync(c => c.CostSheetId == id);

            if (sheet == null) return NotFound();

            var materialIds = sheet.Materials!.Select(x => x.ItemId).ToList();
            ViewBag.MaterialNames = await _context.Items
                .Where(i => materialIds.Contains(i.ItemID))
                .ToDictionaryAsync(i => i.ItemID, i => i.ItemName ?? "");

            return View(sheet);
        }

        // ── Next Sheet Number ───────────────────────────────────────────────────
        [HttpGet]
        public IActionResult GetNextSheetNumber()
        {
            var next = (_context.CostSheet.Max(c => (int?)c.CostSheetId) ?? 0) + 1;
            return Json(new { sheetNo = next });
        }

        // ── Customer list for modal ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetCustomers()
        {
            var list = await _context.Parties
                .Where(p => p.PartyType == "customer" || p.PartyType == "both")
                .Select(p => new
                {
                    p.PartyId,
                    p.PartyName,
                    p.PartyType,
                    p.ContactPerson,
                    p.Phone,
                    p.Email,
                    p.Address,
                    openingBalance = p.OpeningBalance ?? 0
                })
                .ToListAsync();

            return Json(new { success = true, customers = list });
        }

        // ── Get Items with last Purchase Price + current Stock ──────────────────
        // Used by ply row item lookup in Cost Sheet
        [HttpGet]
        public async Task<IActionResult> GetItemsForPly(int branchId = 1)
        {
            try
            {
                var items = await _context.Items.Where(i => i.IsActive == true && i.ItemType == "RawMaterial" && i.StockUnit == "kg" && i.Category.IsActive == true)
                    .Select(i => new
                    {
                        itemId       = i.ItemID,
                        itemName     = i.ItemName ?? "",
                        categoryName = _context.ItemCategory
                            .Where(c => c.CategoryId == i.CategoryID)
                            .Select(c => c.CategoryName)
                            .FirstOrDefault() ?? "",
                        imageUrl     = i.ImagePath,

                        // Last purchase price for this item
                        lastPurPrice = _context.PurchaseInvoiceBody
                            .Where(b => b.Itemid == i.ItemID && _context.PurchaseInvoice.Any(p=>p.PurchaseId==b.PurchaseId))
                            .OrderByDescending(b => b.Srno)
                            .Select(b => (decimal?)b.PurPrice)
                            .FirstOrDefault() ?? 0,

                        // Current stock in selected branch
                        currentStock = _context.Stock
                            .Where(s => s.ItemId == i.ItemID && s.BranchId == branchId)
                            .Select(s => (decimal?)s.Quantity)
                            .FirstOrDefault() ?? 0
                    })
                    .ToListAsync();

                return Json(new { success = true, items });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ── Generic Calculation Guide (GET) ─────────────────────────────────────────
        [HttpGet]

        public IActionResult CalculationGuide()
        {
            var settings = _context.CostSheetSettings.FirstOrDefault()
                ?? new CostSheetSettings
                {
                    LabourRate = 5,
                    EnergyRate = 5.08m,
                    WastePercentage = 6,
                    AdminExpPercentage = 1.52m,
                    SellingDistPercentage = 1,
                    RepairMaintenancePercentage = 1.5m,
                    StoreSparesPercentage = 1.5m,
                    ManufacturingCostPercentage = 1.72m,
                    DefaultFreightRate = 0.5m,
                    DefaultProfitPct = 11
                };

            return View(settings);
        }

        [HttpGet]
        public async Task<IActionResult> GetMaterialOptions()
        {
            var items = await _context.Items.Where(i => i.IsActive == true && i.Category.IsActive == true && (i.ItemType == "RawMaterial" || i.ItemType == "FinishedGoods"))
                .OrderBy(i => i.ItemName).Select(i => new { id = i.ItemID, name = i.ItemName, type = i.ItemType, unit = i.StockUnit, i.MaterialRole, lastPurchasePrice = _context.PurchaseInvoiceBody.Where(b=>b.Itemid==i.ItemID&&_context.PurchaseInvoice.Any(p=>p.PurchaseId==b.PurchaseId)).OrderByDescending(b=>b.Srno).Select(b=>b.PurPrice).FirstOrDefault() }).ToListAsync();
            return Json(new { success = true, items=items.Select(i=>new{i.id,i.name,i.type,i.unit,materialRole=MaterialRoles.Resolve(i.MaterialRole,i.name),i.lastPurchasePrice}) });
        }

        // ── Helper: map ViewModel → Entity ─────────────────────────────────────
        private static void MapToEntity(CostSheetViewModel m, CostSheet e)
        {
            e.ClientCostingJson = m.ClientCosting == null ? null : System.Text.Json.JsonSerializer.Serialize(m.ClientCosting);
            e.FinishedItemId = m.FinishedItemId; e.GlueItemId = m.GlueItemId; e.SilicateItemId = m.SilicateItemId;
            e.GlueKgPerBox = m.GlueKgPerBox; e.SilicateKgPerBox = m.SilicateKgPerBox;
            e.SheetDate = m.SheetDate;
            // Reusable recipe: customer is selected on each production order.
            // Preserve legacy customer metadata on existing sheets; do not assign it on new sheets.
            e.ItemName = m.ItemName;
            e.BranchID = m.BranchID ?? 1;
            e.Status = m.Status ?? "Draft";
            e.BoxStyle = m.BoxStyle;
            e.SizeType = m.SizeType;
            e.SizeUnit = m.SizeUnit;
            e.Length = m.Length;
            e.Width = m.Width;
            e.Height = m.Height;
            e.Flap = m.Flap;
            e.AutoFlapGap = m.AutoFlapGap;
            e.SheetWidthIn = m.SheetWidthIn;
            e.SheetLengthIn = m.SheetLengthIn;
            e.WithFlap = m.WithFlap;
            e.AdjWidthIn = m.AdjWidthIn;
            e.AdjLength1In = m.AdjLength1In;
            e.AdjLength2In = m.AdjLength2In;
            e.AdjWidthMm = m.AdjWidthMm;
            e.AdjLengthMm = m.AdjLengthMm;
            e.TotalSheetSqIn = m.TotalSheetSqIn;
            e.PrintingType = m.PrintingType;
            e.PrintingColors = m.PrintingColors;
            e.PrintingCost = m.PrintingCost;
            e.DickelCost = m.DickelCost;
            e.HasLamination = m.HasLamination;
            e.LaminationCost = m.LaminationCost;
            e.GlueRate = m.GlueRate;
            e.SilicateRate = m.SilicateRate;
            e.GlueSilicateKg = m.GlueSilicateKg;
            e.GlueSilicateCost = m.GlueSilicateCost;
            e.PaperCost = m.PaperCost;
            e.LabourRate = m.LabourRate;
            e.LabourCost = m.LabourCost;
            e.EnergyRate = m.EnergyRate;
            e.EnergyCost = m.EnergyCost;
            e.BindingType = m.BindingType;
            e.BindingPins = m.BindingPins;
            e.BindingCost = m.BindingCost;
            e.FreightRate = m.FreightRate;
            e.FreightCost = m.FreightCost;
            e.SubTotal = m.SubTotal;
            e.WastePct = m.WastePct;
            e.WasteCost = m.WasteCost;
            e.AdminExpPct = m.AdminExpPct;
            e.AdminExpCost = m.AdminExpCost;
            e.SellingDistPct = m.SellingDistPct;
            e.SellingDistCost = m.SellingDistCost;
            e.RepairMaintPct = m.RepairMaintPct;
            e.RepairMaintCost = m.RepairMaintCost;
            e.StoreSparesPct = m.StoreSparesPct;
            e.StoreSparesCost = m.StoreSparesCost;
            e.MfgCostPct = m.MfgCostPct;
            e.MfgCostValue = m.MfgCostValue;
            e.ManufacturingTotal = m.ManufacturingTotal;
            e.CommPersonA = m.CommPersonA;
            e.CommPersonAPct = m.CommPersonAPct;
            e.CommPersonACost = m.CommPersonACost;
            e.CommPersonB = m.CommPersonB;
            e.CommPersonBPct = m.CommPersonBPct;
            e.CommPersonBCost = m.CommPersonBCost;
            e.WHTaxPct = m.WHTaxPct;
            e.GSTPercentage = m.GSTPercentage;
            e.ProfitPct = m.ProfitPct;
            e.ProfitAmount = m.ProfitAmount;
            e.FinalCostWOGST = m.FinalCostWOGST;
            e.MOQ = m.MOQ;
            e.TaxPct = m.TaxPct;
            e.FinalRateWithGST = m.FinalRateWithGST;
            e.Remarks = m.Remarks;
        }
    }
}
