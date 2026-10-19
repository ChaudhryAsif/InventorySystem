namespace InventorySystem.Core.Options;

// Keep branch IDs in the data model so multiple branches can be enabled later.
public sealed class BranchOptions
{
    public const int DefaultBranchId = 1;
    public bool MultiBranchEnabled { get; set; } = false;
}
