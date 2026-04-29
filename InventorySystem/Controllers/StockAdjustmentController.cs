using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class StockAdjustmentController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StockAdjustmentController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetAdjustments()
        {
            var adjustments = await _context.StockAdjustment
                .OrderByDescending(x => x.AdjustmentDate)
                .Select(x => new
                {
                    x.AdjustmentId,
                    x.AdjustmentNo,
                    AdjustmentDate = x.AdjustmentDate.ToString("yyyy-MM-dd"),
                    x.AdjustmentType,
                    x.Status,
                    x.Remarks,
                    ItemCount = x.Details!.Count(),
                    TotalQuantity = x.Details!.Sum(d => d.Quantity)
                })
                .ToListAsync();

            return Json(adjustments);
        }

        [HttpGet]
        public async Task<IActionResult> GetNextAdjustmentNo()
        {
            var lastAdjustment = await _context.StockAdjustment
                .OrderByDescending(x => x.AdjustmentId)
                .FirstOrDefaultAsync();

            string nextNo;
            if (lastAdjustment == null)
            {
                nextNo = "ADJ-0001";
            }
            else
            {
                var lastNo = lastAdjustment.AdjustmentNo ?? "ADJ-0000";
                var numPart = lastNo.Split('-').LastOrDefault() ?? "0000";
                if (int.TryParse(numPart, out int num))
                {
                    nextNo = $"ADJ-{(num + 1):D4}";
                }
                else
                {
                    nextNo = "ADJ-0001";
                }
            }

            return Json(new { adjustmentNo = nextNo });
        }

        [HttpGet]
        public async Task<IActionResult> GetItems(string? search, int? categoryId)
        {
            var query = _context.Items
                .Include(x => x.Category)
                .Where(x => x.IsActive == true);

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(x => x.ItemName!.Contains(search) || 
                                        x.ProductName!.Contains(search) ||
                                        x.CompanyName!.Contains(search));
            }

            if (categoryId.HasValue && categoryId > 0)
            {
                query = query.Where(x => x.CategoryID == categoryId);
            }

            var items = await query
                .Select(x => new
                {
                    x.ItemID,
                    x.ItemName,
                    x.CompanyName,
                    x.ProductName,
                    CategoryName = x.Category.CategoryName,
                    x.SalePrice,
                    CurrentStock = _context.Stock
                        .Where(s => s.ItemId == x.ItemID)
                        .Sum(s => (decimal?)s.Quantity) ?? 0
                })
                .Take(50)
                .ToListAsync();

            return Json(items);
        }

        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.ItemCategory
                .Where(x => x.IsActive == true)
                .OrderBy(x => x.CategoryName)
                .Select(x => new { x.CategoryId, x.CategoryName })
                .ToListAsync();

            return Json(categories);
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] StockAdjustmentViewModel model)
        {
            if (model == null || model.Details == null || !model.Details.Any())
                return BadRequest("Invalid adjustment data.");

            try
            {
                var adjustment = new StockAdjustment
                {
                    AdjustmentNo = model.AdjustmentNo,
                    AdjustmentDate = model.AdjustmentDate,
                    BranchId = model.BranchId,
                    AdjustmentType = model.AdjustmentType,
                    Remarks = model.Remarks,
                    Status = model.Status,
                    CreatedDate = DateTime.Now
                };

                _context.StockAdjustment.Add(adjustment);
                await _context.SaveChangesAsync();

                foreach (var detail in model.Details)
                {
                    if (detail.ItemId <= 0 || detail.Quantity <= 0)
                        continue;

                    // Add detail record
                    _context.StockAdjustmentDetail.Add(new StockAdjustmentDetail
                    {
                        AdjustmentId = adjustment.AdjustmentId,
                        ItemId = detail.ItemId,
                        Description = detail.Description,
                        Quantity = detail.Quantity,
                        UnitCost = detail.UnitCost,
                        TotalCost = detail.Quantity * (detail.UnitCost ?? 0),
                        Reason = detail.Reason
                    });

                    // Update stock only if status is "Posted"
                    if (model.Status == "Posted")
                    {
                        var stock = await _context.Stock
                            .FirstOrDefaultAsync(s => s.ItemId == detail.ItemId);

                        if (stock == null)
                        {
                            _context.Stock.Add(new Stock
                            {
                                ItemId = detail.ItemId,
                                BranchId = model.BranchId,
                                Quantity = detail.Quantity,
                                LastUpdated = DateTime.Now
                            });
                        }
                        else
                        {
                            stock.Quantity += detail.Quantity;
                            stock.LastUpdated = DateTime.Now;
                        }
                    }
                }

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = "Stock adjustment saved successfully.",
                    adjustmentId = adjustment.AdjustmentId
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAdjustmentDetails(long id)
        {
            var adjustment = await _context.StockAdjustment
                .Include(x => x.Details)
                .ThenInclude(d => d.Item)
                .FirstOrDefaultAsync(x => x.AdjustmentId == id);

            if (adjustment == null)
                return NotFound();

            return Json(new
            {
                adjustment.AdjustmentId,
                adjustment.AdjustmentNo,
                AdjustmentDate = adjustment.AdjustmentDate.ToString("yyyy-MM-dd"),
                adjustment.BranchId,
                adjustment.AdjustmentType,
                adjustment.Remarks,
                adjustment.Status,
                Details = adjustment.Details!.Select(d => new
                {
                    d.DetailId,
                    d.ItemId,
                    ItemName = d.Item!.ItemName,
                    d.Description,
                    d.Quantity,
                    d.UnitCost,
                    d.TotalCost,
                    d.Reason
                })
            });
        }

        [HttpPost]
        public async Task<IActionResult> Post(long id)
        {
            var adjustment = await _context.StockAdjustment
                .Include(x => x.Details)
                .FirstOrDefaultAsync(x => x.AdjustmentId == id);

            if (adjustment == null)
                return NotFound();

            if (adjustment.Status == "Posted")
                return BadRequest("Adjustment already posted.");

            // Update stock for each detail
            foreach (var detail in adjustment.Details!)
            {
                var stock = await _context.Stock
                    .FirstOrDefaultAsync(s => s.ItemId == detail.ItemId);

                if (stock == null)
                {
                    _context.Stock.Add(new Stock
                    {
                        ItemId = detail.ItemId,
                        BranchId = adjustment.BranchId,
                        Quantity = detail.Quantity,
                        LastUpdated = DateTime.Now
                    });
                }
                else
                {
                    stock.Quantity += detail.Quantity;
                    stock.LastUpdated = DateTime.Now;
                }
            }

            adjustment.Status = "Posted";
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Adjustment posted successfully." });
        }

        [HttpDelete]
        public async Task<IActionResult> Delete(long id)
        {
            var adjustment = await _context.StockAdjustment
                .FirstOrDefaultAsync(x => x.AdjustmentId == id);

            if (adjustment == null)
                return NotFound();

            if (adjustment.Status == "Posted")
                return BadRequest("Cannot delete posted adjustment.");

            _context.StockAdjustment.Remove(adjustment);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Adjustment deleted successfully." });
        }
    }

    public class StockAdjustmentViewModel
    {
        public string? AdjustmentNo { get; set; }
        public DateTime AdjustmentDate { get; set; }
        public int BranchId { get; set; }
        public string? AdjustmentType { get; set; }
        public string? Remarks { get; set; }
        public string? Status { get; set; }
        public List<StockAdjustmentDetailViewModel>? Details { get; set; }
    }

    public class StockAdjustmentDetailViewModel
    {
        public long ItemId { get; set; }
        public string? Description { get; set; }
        public decimal Quantity { get; set; }
        public decimal? UnitCost { get; set; }
        public string? Reason { get; set; }
    }
}