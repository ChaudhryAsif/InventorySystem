namespace InventorySystem.Core.Services
{
    public interface IStockService
    {
        Task AdjustAsync(long itemId, int branchId, decimal delta);
    }
}
