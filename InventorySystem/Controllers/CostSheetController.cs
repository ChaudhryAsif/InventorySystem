using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        // ── List ────────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var permission = await this.CheckPermissionAsync(_permissionService, "CostSheet.View");
            if (permission != null) return permission;

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
                    c.FinalRateWithGST,
                    CustomerName = _context.Parties
                        .Where(p => p.PartyId.ToString() == c.CustomerId)
                        .Select(p => p.PartyName)
                        .FirstOrDefault() ?? c.CustomerId
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

        // ── Edit (GET) ──────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var permission = await this.CheckPermissionAsync(_permissionService, "CostSheet.Edit");
            if (permission != null) return permission;

            var sheet = await _context.CostSheet
                .Include(c => c.Plies)
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

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                CostSheet sheet;

                if (model.CostSheetId > 0)
                {
                    // ── Update existing ──────────────────────────────────────
                    sheet = await _context.CostSheet
                        .Include(c => c.Plies)
                        .FirstOrDefaultAsync(c => c.CostSheetId == model.CostSheetId)
                        ?? throw new Exception("Cost sheet not found.");

                    // Remove old plies
                    _context.CostSheetPly.RemoveRange(sheet.Plies!);
                }
                else
                {
                    // ── New ──────────────────────────────────────────────────
                    sheet = new CostSheet { CreatedDate = DateTime.Now };
                    _context.CostSheet.Add(sheet);
                }

                MapToEntity(model, sheet);
                await _context.SaveChangesAsync();

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

        // ── Delete ──────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var sheet = await _context.CostSheet
                .Include(c => c.Plies)
                .FirstOrDefaultAsync(c => c.CostSheetId == id);

            if (sheet == null) return NotFound();

            _context.CostSheetPly.RemoveRange(sheet.Plies!);
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
                    }).ToList()
                }
            });
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
                var items = await _context.Items
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
                            .Where(b => b.Itemid == i.ItemID)
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

        // ── Helper: map ViewModel → Entity ─────────────────────────────────────
        private static void MapToEntity(CostSheetViewModel m, CostSheet e)
        {
            e.SheetDate = m.SheetDate;
            e.CustomerId = m.CustomerId;
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