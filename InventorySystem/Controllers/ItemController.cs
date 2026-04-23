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

        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Used by Purchase & Sale pages — includes live stock quantity.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetItems(int branchId = 1)
        {
            try
            {
                var items = await _context.Items
                    .Select(i => new
                    {
                        itemID = i.ItemID,
                        itemName = i.ItemName ?? "",
                        categoryName = _context.ItemCategory
                            .Where(c => c.CategoryId == i.CategoryID)
                            .Select(c => c.CategoryName)
                            .FirstOrDefault() ?? "",
                        barcode = "",
                        salePrice = i.SalePrice ?? 0,
                        purchasePrice = i.SalePrice ?? 0,
                        imageUrl = i.ImagePath,
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

        [HttpGet]
        public async Task<JsonResult> GetList()
        {
            try
            {
                var items = await _context.Items
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
                        Barcode = "",
                        CategoryName = _context.ItemCategory
                            .Where(c => c.CategoryId == i.CategoryID)
                            .Select(c => c.CategoryName)
                            .FirstOrDefault() ?? ""
                    })
                    .ToListAsync();

                return Json(items);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error loading item list", error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetItemById(int id)
        {
            var item = await _context.Items
                .Where(x => x.ItemID == id)
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
                    x.ImagePath
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
            if (model == null)
                return BadRequest("Invalid data.");

            try
            {
                string? imagePath = null;

                if (File != null && File.Length > 0)
                {
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

                item.ItemName = model.ItemName;
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
            try
            {
                var item = await _context.Items.FindAsync(request.Id);
                if (item == null)
                    return Json(new { success = false, message = "Item not found" });

                _context.Items.Remove(item);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Item deleted successfully" });
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
