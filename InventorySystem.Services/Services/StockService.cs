using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Core.Services
{
    public class StockService : IStockService
    {
        private readonly ApplicationDbContext _context;

        public StockService(ApplicationDbContext context)
        {
            _context = context;
        }

        // Finds the Stock row for (itemId, branchId); creates it if missing, else adjusts Quantity.
        public async Task AdjustAsync(long itemId, int branchId, decimal delta)
        {
            var stock = await _context.Stock.FirstOrDefaultAsync(s => s.ItemId == itemId && s.BranchId == branchId);
            if (stock == null)
            {
                _context.Stock.Add(new Stock { ItemId = itemId, BranchId = branchId, Quantity = delta, LastUpdated = DateTime.Now });
            }
            else
            {
                stock.Quantity += delta;
                stock.LastUpdated = DateTime.Now;
            }
        }
    }
}
