namespace InventorySystem.Core.Services;

// "Edit saved records": until now only a SuperAdmin could change a record after it was saved. This permission lets the
// SuperAdmin give that power to a role, module by module. Nobody holds it by default, so the old rule stays as it was.
public static class EditSavedPermissions
{
    public const string Action = "EditSaved";

    // The modules whose saved records are locked (see OperationFilter.InventoryEditBlocked). Cost sheets were never locked.
    public static readonly string[] Modules = ["Item", "ItemCategory", "Sale", "SaleReturn", "Purchase", "PurchaseReturn", "Production", "GatePass"];

    public static string Code(string module) => module + "." + Action;
}
