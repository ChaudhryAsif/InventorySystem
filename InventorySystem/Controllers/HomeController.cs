using InventorySystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        public HomeController(ApplicationDbContext context) => _context = context;

        public IActionResult Index()
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";

            if (role == "Admin")
            {
                ViewData["Title"] = "Dashboard";
                return View(); // Views/Home/Index.cshtml (admin dashboard)
            }

            // All non-admin roles (including CostSheet) see the generic POS dashboard
            ViewData["Title"] = "Dashboard";
            return View("GenericDashboard");
        }
    }
}
