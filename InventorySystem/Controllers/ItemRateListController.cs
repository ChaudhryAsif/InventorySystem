using InventorySystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    public class ItemRateListController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ItemRateListController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Load main page
        public IActionResult Index()
        {
            return View();
        }

        // Get categories for dropdown
        [HttpGet]
        public JsonResult GetCategories()
        {
            var categories = _context.ItemCategory
                .Where(c => c.IsActive == true)
                .Select(c => new
                {
                    CategoryId = c.CategoryId,
                    CategoryName = c.CategoryName
                })
                .ToList();

            return Json(categories);
        }

        // Get items for selected category or all
        [HttpGet]
        public JsonResult GetItems(int? categoryId)
        {
            var items = _context.Items
                .Include(i => i.Category)
                .Where(i => !categoryId.HasValue || i.CategoryID == categoryId)
                .Select(i => new
                {
                    i.ItemID,
                    CategoryName = i.Category.CategoryName,
                    ItemName = i.ItemName,
                    i.Design,
                    i.Size,
                    i.SalePrice
                })
                .ToList();

            return Json(items);
        }
    }
}
