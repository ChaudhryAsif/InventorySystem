using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    public class CategoryController : Controller
    {
        private readonly ApplicationDbContext _context;
        public CategoryController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var itemCategories = await _context.ItemCategory.ToListAsync();
                return View(itemCategories);
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        [HttpPost]
        public async Task<JsonResult> Save(ItemCategoryViewModel categoryViewModel)
        {
            if (categoryViewModel == null)
                return Json(new { success = false, message = "Invalid data." });

            var category = await MapItemCategoryViewModelToEntityAsync(categoryViewModel);

            if (category == null)
                return Json(new { success = false, message = "Mapping failed." });

            try
            {
                if (category.CategoryId == 0)
                {
                    category.CategoryId = GetMaxCategoryId();
                    await _context.ItemCategory.AddAsync(category);
                }
                else
                {
                    _context.ItemCategory.Update(category);
                }

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Category saved successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error saving category.", error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetById(int id)
        {
            var category = await _context.ItemCategory
                .FirstOrDefaultAsync(c => c.CategoryId == id);

            if (category == null)
                return NotFound();

            return Json(category);
        }

        public IActionResult GetList()
        {
            var categories = _context.ItemCategory.ToList();
            return Json(new { success = true, data = categories });
        }

        [HttpPost]
        public async Task<JsonResult> Delete(int id)
        {
            try
            {
                var category = await _context.ItemCategory.FindAsync(id);

                if (category == null)
                    return Json(new { success = false, message = "Category not found." });

                _context.ItemCategory.Remove(category);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Category deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error deleting category.", error = ex.Message });
            }
        }

        public int GetMaxCategoryId()
        {
            var maxId = _context.ItemCategory.Max(c => (int?)c.CategoryId) ?? 0;
            return maxId + 1;
        }

        [HttpGet]
        public JsonResult GetCategoriesDropdown()
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

        private async Task<ItemCategory> MapItemCategoryViewModelToEntityAsync(ItemCategoryViewModel viewModel)
        {
            if (viewModel == null) return null;

            var category = await _context.ItemCategory
                .FirstOrDefaultAsync(c => c.CategoryId == viewModel.CategoryId);

            if (category == null)
            {
                // New record (CategoryId == 0)
                category = new ItemCategory
                {
                    CategoryName = viewModel.CategoryName,
                    Description = viewModel.Description,
                    IsActive = viewModel.IsActive,
                    CreatedDate = DateTime.Now
                };
            }
            else
            {
                // Existing record – update fields
                category.CategoryName = viewModel.CategoryName;
                category.Description = viewModel.Description;
                category.IsActive = viewModel.IsActive;
            }

            return category;
        }

    }
}
