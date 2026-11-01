namespace InventorySystem.Models;

// Read-only rows for the item and group print pages.
public record ItemPrintRow(long ItemID, string ItemName, string CategoryName, string ItemType, string StockUnit, string? MaterialRole,
    decimal SalePrice, string? Size, string? Description, string? Barcode, decimal ReorderLevel, bool IsActive, string? ImagePath, DateTime? CreatedDate);

public record GroupPrintRow(int CategoryId, string CategoryName, string? GroupType, string? Description, string? Address, string? ContactNumber,
    bool IsActive, DateTime? CreatedDate, int ItemCount);

public record PrintPage<T>(List<T> Rows, bool Detail, bool Popup);
