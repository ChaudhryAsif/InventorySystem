namespace InventorySystem.Core.Options
{
    /// <summary>Config-driven toggle for whether Sale/Purchase/Returns also post to the double-entry ledger.</summary>
    public class AccountingOptions
    {
        public bool PostToGeneralLedger { get; set; } = false;
    }
}
