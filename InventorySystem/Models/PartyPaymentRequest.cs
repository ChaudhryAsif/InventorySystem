namespace InventorySystem.Models;
public sealed class PartyPaymentRequest
{
    public int PartyId { get; set; }
    public string Direction { get; set; } = "Receipt";
    public string Mode { get; set; } = "Cash";
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Reference { get; set; }
    public int BranchId { get; set; } = 1;
}
