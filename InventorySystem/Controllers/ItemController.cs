using InventorySystem.Core.Services;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class ItemController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ItemController(ApplicationDbContext context)
        {
            _context = context;
        }

        private async Task<bool> CanDeleteAsync() => User?.IsInRole("SuperAdmin") == true || User!=null && await new PermissionService(_context).HasPermissionAsync(User,"Item.Delete");

        public IActionResult Index()
        {
            ViewBag.ItemType = "RawMaterial";
            return View();
        }

        [HttpGet]
        public IActionResult FinishedGoods()
        {
            ViewBag.ItemType = "FinishedGoods";
            return View("Index");
        }

        /// <summary>
        /// Used by Purchase & Sale pages — includes live stock quantity.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetItems(int branchId = 1, int page = 1, int pageSize = 200, string? search = null, string? itemType = null)
        {
            try
            {
                page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
                if (!string.IsNullOrEmpty(itemType) && itemType is not ("RawMaterial" or "FinishedGoods"))
                    return BadRequest(new { success = false, message = "Select a valid item type." });
                var query = _context.Items.Where(i => i.IsActive == true &&
                    _context.ItemCategory.Any(c => c.CategoryId == i.CategoryID && c.IsActive == true));
                if (!string.IsNullOrEmpty(itemType)) query = query.Where(i => i.ItemType == itemType);
                if (!string.IsNullOrWhiteSpace(search)) query = query.Where(i => i.ItemName!.Contains(search) || i.Barcode == search);
                var totalCount = await query.CountAsync();

                var items = await query.OrderBy(i => i.ItemID)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(i => new
                    {
                        itemID = i.ItemID,
                        itemName = i.ItemName ?? "",
                        size = i.Size ?? "",
                        categoryName = _context.ItemCategory
                            .Where(c => c.CategoryId == i.CategoryID)
                            .Select(c => c.CategoryName)
                            .FirstOrDefault() ?? "",
                        itemType = i.ItemType, stockUnit = i.StockUnit,
                        barcode = i.Barcode ?? "",
                        salePrice = i.SalePrice ?? 0,
                        // Actual last-purchase cost for this item (not the sale price).
                        // Falls back to 0 when the item has never been purchased.
                        purchasePrice = _context.PurchaseInvoiceBody
                            .Where(b => b.Itemid == i.ItemID && _context.PurchaseInvoice.Any(p => p.PurchaseId == b.PurchaseId))
                            .OrderByDescending(b => b.Srno)
                            .Select(b => b.PurPrice)
                            .FirstOrDefault() ?? 0,
                        imageUrl = i.ImagePath,
                        currentStock = _context.Stock
                            .Where(s => s.ItemId == i.ItemID && s.BranchId == branchId)
                            .Select(s => (decimal?)s.Quantity)
                            .FirstOrDefault() ?? 0
                    })
                    .ToListAsync();

                return Json(new { success = true, items, totalCount, page, pageSize });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Print page: ticked ids, or every item matching the list filters. detail=1 prints full cards.
        [HttpGet]
        public async Task<IActionResult> Print(string? ids, string? itemType, string? search, string? status, int? groupId, bool detail = false, bool popup = false)
        {
            if (!string.IsNullOrEmpty(itemType) && itemType is not ("RawMaterial" or "FinishedGoods") ||
                !string.IsNullOrEmpty(status) && status is not ("Active" or "Inactive")) return BadRequest();
            var query = _context.Items.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(itemType)) query = query.Where(i => i.ItemType == itemType);
            if (ids != null)
            {
                var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => long.TryParse(x, out var n) ? n : 0).Where(n => n > 0).Distinct().Take(2000).ToList();
                query = query.Where(i => idList.Contains(i.ItemID));
            }
            else
            {
                if (groupId.HasValue) query = query.Where(i => i.CategoryID == groupId);
                if (!string.IsNullOrEmpty(status)) query = status == "Active" ? query.Where(i => i.IsActive == true) : query.Where(i => i.IsActive != true);
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim().ToLowerInvariant();
                    query = query.Where(i => (i.ItemName ?? "").ToLower().Contains(term) ||
                        _context.ItemCategory.Any(c => c.CategoryId == i.CategoryID && c.CategoryName.ToLower().Contains(term)));
                }
            }
            var rows = await query.OrderBy(i => i.ItemID).Take(5000).Select(i => new ItemPrintRow(i.ItemID, i.ItemName ?? "",
                _context.ItemCategory.Where(c => c.CategoryId == i.CategoryID).Select(c => c.CategoryName).FirstOrDefault() ?? "",
                i.ItemType, i.StockUnit, i.MaterialRole, i.SalePrice ?? 0, i.Size, i.Description, i.Barcode, i.ReorderLevel, i.IsActive ?? false, i.ImagePath, i.CreatedDate)).ToListAsync();
            ViewBag.ItemType = itemType;
            return View(new PrintPage<ItemPrintRow>(rows, detail, popup));
        }

        [HttpGet]
        public async Task<JsonResult> GetList(int page = 1, int pageSize = 50, string? itemType = null, string? search = null, string? status = null, int? groupId = null)
        {
            try
            {
                page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
                if (!string.IsNullOrEmpty(itemType) && itemType is not ("RawMaterial" or "FinishedGoods") ||
                    !string.IsNullOrEmpty(status) && status is not ("Active" or "Inactive"))
                    return new JsonResult(new { success = false, message = "Select a valid item type and status." }) { StatusCode = 400 };
                var query = _context.Items.AsQueryable();
                if(groupId.HasValue) query=query.Where(i=>i.CategoryID==groupId && (itemType==null || i.Category.GroupType==itemType));
                if (!string.IsNullOrEmpty(itemType)) query = query.Where(i => i.ItemType == itemType);
                if (!string.IsNullOrEmpty(status)) query = status == "Active" ? query.Where(i => i.IsActive == true) : query.Where(i => i.IsActive != true);
                if (!string.IsNullOrWhiteSpace(search)) {
                    var term = search.Trim().ToLowerInvariant();
                    query = query.Where(i => (i.ItemName ?? "").ToLower().Contains(term) ||
                        _context.ItemCategory.Any(c => c.CategoryId == i.CategoryID && c.CategoryName.ToLower().Contains(term)));
                }
                var totalCount = await query.CountAsync();

                var items = await query.OrderBy(i => i.ItemID)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(i => new
                    {
                        ItemID = i.ItemID,
                        CategoryId = i.CategoryID,
                        ItemName = i.ItemName ?? "",
                        ProductName = i.ProductName ?? "",
                        CompanyName = i.CompanyName ?? "",
                        Size = i.Size ?? "",
                        ImagePath = i.ImagePath,
                        Design = i.Design ?? "",
                        Origin = i.Origin ?? "",
                        SalePrice = i.SalePrice ?? 0,
                        IsActive = i.IsActive ?? false,
                        WHCOGS = i.WHCOGS ?? "",
                        ItemType = i.ItemType, StockUnit = i.StockUnit,
                        Barcode = i.Barcode ?? "",
                        CategoryName = _context.ItemCategory
                            .Where(c => c.CategoryId == i.CategoryID)
                            .Select(c => c.CategoryName)
                            .FirstOrDefault() ?? ""
                    })
                    .ToListAsync();

                return Json(new { data = items, totalCount, page, pageSize, canDelete = await CanDeleteAsync() });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error loading item list", error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetItemById(long id, string? itemType = null)
        {
            var item = await _context.Items
                .Where(x => x.ItemID == id && (itemType == null || x.ItemType == itemType))
                .Select(x => new
                {
                    x.ItemID,
                    x.CategoryID,
                    x.Size,
                    x.ItemName,
                    x.SalePrice,
                    x.Description,
                    //x.IsMarinated,
                    x.IsActive,
                    x.ImagePath,
                    x.Barcode,
                    x.ItemType, x.StockUnit, x.MaterialRole,
                    x.ReorderLevel
                    //Barcodes = _context.Barcodes
                    //    .Where(b => b.ItemId == x.ItemId)
                    //    .Select(b => b.Code)
                     //   .ToList()
                })
                .FirstOrDefaultAsync();

            if (item == null)
                return NotFound();

            return Json(item);
        }

        [HttpPost]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Save([FromForm] ItemViewModel model, IFormFile? File)
        {
            if (model == null || !ModelState.IsValid)
                return BadRequest(new { success = false, message = "Review the item details and try again.",
                    errors = ModelState.Where(x => x.Value!.Errors.Count > 0).Select(x => x.Key) });
            if (model.PageItemType != null && (model.PageItemType is not ("RawMaterial" or "FinishedGoods") || model.ItemType != model.PageItemType ||
                model.ItemID > 0 && !await _context.Items.AnyAsync(i => i.ItemID == model.ItemID && i.ItemType == model.PageItemType)))
                return BadRequest(new { success = false, message = "This item belongs to a different inventory section." });
            if (model.ItemID < 0)
                return BadRequest(new { success = false, message = "The item reference is invalid. Reopen the item and try again." });

            if (string.IsNullOrWhiteSpace(model.ItemName) || model.SalePrice < 0 || model.ReorderLevel < 0 ||
                !await _context.ItemCategory.AnyAsync(c => c.CategoryId == model.CategoryID && c.IsActive == true))
                return BadRequest(new { success = false, message = "Enter a valid name, group, price and reorder level." });
            if (model.ItemType is not ("RawMaterial" or "FinishedGoods") || model.StockUnit is not ("kg" or "pcs" or "in"))
                return BadRequest(new { success = false, message = "Select KG, PCS or Inches as the item stock unit." });
            if(model.MaterialRole is not (null or "" or "General" or "Glue" or "Silicate" or "LaminationGlue" or "LaminationChemical")) return BadRequest(new{success=false,message="Select a valid costing material use."});
            if(!string.IsNullOrEmpty(model.MaterialRole)&&model.MaterialRole!="General"&&(model.ItemType!="RawMaterial"||model.StockUnit!="kg")) return BadRequest(new{success=false,message="Adhesive costing materials must be Raw Material items stocked in KG."});
            var groupType = await _context.ItemCategory.Where(c => c.CategoryId == model.CategoryID)
                .Select(c => c.GroupType).SingleAsync();
            if (groupType != null && model.ItemType != groupType)
                return BadRequest(new { success = false, message = "The item type must match the selected group type." });
            var existingUnit = await _context.Items.Where(i => i.ItemID == model.ItemID).Select(i => i.StockUnit).FirstOrDefaultAsync();
            if (existingUnit != null && existingUnit != model.StockUnit && (await _context.Stock.AnyAsync(s => s.ItemId == model.ItemID && s.Quantity != 0) || await _context.PurchaseInvoiceBody.IgnoreQueryFilters().AnyAsync(b=>b.Itemid==model.ItemID) || await _context.SaleInvoiceBody.IgnoreQueryFilters().AnyAsync(b=>b.ItemId==model.ItemID) || await _context.GatePassLines.IgnoreQueryFilters().AnyAsync(l=>l.ItemId==model.ItemID)))
                return BadRequest(new { success = false, message = "Cannot change units after stock or document history exists. Use a separate item to preserve quantities." });
            model.Barcode = string.IsNullOrWhiteSpace(model.Barcode) ? null : model.Barcode.Trim();
            if (model.Barcode?.Length > 100 || (model.Barcode != null && await _context.Items.AnyAsync(i => i.Barcode == model.Barcode && i.ItemID != model.ItemID)))
                return BadRequest(new { success = false, message = "Barcode must be unique and at most 100 characters." });
            if (model.ItemID > 0 && !await _context.Items.AnyAsync(i => i.ItemID == model.ItemID)) return NotFound();
            try
            {
                string? imagePath = null;

                if (File != null && File.Length > 0)
                {
                    if (File.Length > 2 * 1024 * 1024) return BadRequest(new { success = false, message = "Image must be at most 2 MB." });
                    var extension = Path.GetExtension(File.FileName).ToLowerInvariant();
                    using var input = File.OpenReadStream();
                    var header = new byte[8]; var read = await input.ReadAsync(header);
                    var png = read == 8 && header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                    var jpg = read >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255;
                    if (!(png && extension == ".png" || jpg && extension is ".jpg" or ".jpeg"))
                        return BadRequest(new { success = false, message = "Upload a valid PNG or JPEG image." });
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    var fileName = Guid.NewGuid() + Path.GetExtension(File.FileName);
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await File.CopyToAsync(stream);
                    }

                    imagePath = "/uploads/" + fileName;
                }

                var item = model.ItemID == 0
                    ? new Items()
                    : await _context.Items.FindAsync(model.ItemID) ?? new Items();

                item.ItemType = model.ItemType;
                item.StockUnit = model.StockUnit;
                item.MaterialRole = string.IsNullOrEmpty(model.MaterialRole)?null:model.MaterialRole;
                item.ItemName = model.ItemName.Trim();
                item.Barcode = model.Barcode;
                item.ReorderLevel = model.ReorderLevel;
                item.CategoryID = model.CategoryID;
                item.SalePrice = model.SalePrice;
                item.Description = model.Description;
                //item.IsMarinated = model.IsMarinated;
                item.IsActive = model.IsActive;
                item.Size = model.Size;
                if (imagePath != null) item.ImagePath = imagePath;

                if (model.ItemID == 0)
                    _context.Items.Add(item);
                else
                    _context.Items.Update(item);

                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<JsonResult> Delete([FromBody] DeleteRequest request)
        {
            if (!await CanDeleteAsync())
                return new JsonResult(new { success = false, message = "You do not have permission to delete items." }) { StatusCode = 403 };
            if (request == null || request.Id <= 0)
                return new JsonResult(new { success = false, message = "Select a valid item." }) { StatusCode = 400 };
            try
            {
                var item = await _context.Items.FindAsync(request.Id);
                if (item == null)
                    return Json(new { success = false, message = "Item not found" });

                item.IsActive = false;
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Item deactivated successfully; invoice history was preserved" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

    public class DeleteRequest
    {
        public long Id { get; set; }
    }
}
