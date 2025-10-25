using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    public class SaleController : Controller
    {
        private readonly ApplicationDbContext _context;
        public SaleController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            return View();
        }


    }
}
