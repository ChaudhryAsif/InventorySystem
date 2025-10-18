using InventorySystem.Models;
using Microsoft.AspNetCore.Mvc;

namespace InventorySystem.Controllers
{
    public class ItemController : Controller
    {
        // GET: Item Category List
        public IActionResult Index()
        {
            ViewData["Title"] = "Item Category";
            return View();
        }

        // POST: Save Category
        [HttpPost]
        public IActionResult SaveCategory([FromBody] ItemCategory model)
        {
            try
            {
                // Your save logic here
                // Example: _dbContext.Categories.Add(model);
                // _dbContext.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Category saved successfully",
                    categoryId = model.CategoryId
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // POST: Delete Category
        [HttpPost]
        public IActionResult DeleteCategory(int id)
        {
            try
            {
                // Your delete logic here

                return Json(new
                {
                    success = true,
                    message = "Category deleted successfully"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // GET: Item Management
        public IActionResult Management()
        {
            ViewData["Title"] = "Item Management";
            return View();
        }

        // POST: Save Item
        [HttpPost]
        public IActionResult SaveItem([FromBody] Item model)
        {
            try
            {
                // Your save logic here

                return Json(new
                {
                    success = true,
                    message = "Item saved successfully"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // GET: Item Rate
        public IActionResult Rate()
        {
            ViewData["Title"] = "Item Rate";
            return View();
        }
    }
}
