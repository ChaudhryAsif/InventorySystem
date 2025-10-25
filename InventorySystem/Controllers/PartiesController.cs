using InventorySystem.Data;
using Microsoft.AspNetCore.Mvc;

namespace InventorySystem.Controllers
{
    public class PartiesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PartiesController(ApplicationDbContext context)
        {
            _context = context;
        }

        public ActionResult Index()
        {
            return View();
        }
    }
}
