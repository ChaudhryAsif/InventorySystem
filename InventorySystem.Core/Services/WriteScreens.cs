namespace InventorySystem.Core.Services;

// Pages that exist to enter or change data. A user who may only view must not reach them (or see them in the menu),
// even though opening the page is a GET. Everything else that is a GET stays a plain "View".
public static class WriteScreens
{
    // Same names as the permission codes (Module.Verb); controllers share a module where the code says so.
    public static string ModuleFor(string controller) => controller switch { "Home" => "Dashboard", "Category" => "ItemCategory", "ItemRateList" => "Item", _ => controller };

    private static readonly Dictionary<string, string> Screens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CostSheet/Create"] = "Create", ["CostSheet/Edit"] = "Edit", ["CostSheet/Settings"] = "Settings",
        ["Production/Create"] = "Create", ["Production/Edit"] = "Edit", ["Production/Consume"] = "Edit",
        ["StockAdjustment/Create"] = "Create",
        ["GatePass/Create"] = "Create",
        ["Accounts/PaymentVoucher"] = "Create", ["Accounts/ReceiptVoucher"] = "Create", ["Accounts/JournalVoucher"] = "Create", ["Accounts/ContraVoucher"] = "Create",
        ["Accounts/DebitNote"] = "Create", ["Accounts/CreditNote"] = "Create", ["Accounts/OpeningBalance"] = "Create",
    };

    // Sale, purchase and their returns: Index is the entry form, List is the read-only list.
    public static readonly HashSet<string> EntryIndexControllers = new(StringComparer.OrdinalIgnoreCase) { "Sale", "Purchase", "SaleReturn", "PurchaseReturn" };

    // The permission verb a GET screen needs, or null when it is an ordinary read page.
    public static string? VerbFor(string controller, string action, bool editingExisting = false)
    {
        if (!Screens.TryGetValue(controller + "/" + action, out var verb)) return null;
        return verb == "Create" && editingExisting ? "Edit" : verb;
    }

    public static bool IsEntryIndex(string controller, string action) => action.Equals("Index", StringComparison.OrdinalIgnoreCase) && EntryIndexControllers.Contains(controller);
}
