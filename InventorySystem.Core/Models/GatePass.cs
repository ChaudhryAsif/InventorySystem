using System.ComponentModel.DataAnnotations;
namespace InventorySystem.Core.Models;

public class GatePass
{
    public int Id { get; set; }
    [Required, MaxLength(8)] public string Direction { get; set; } = "Outward";
    public DateTime PassDate { get; set; } = DateTime.Today;
    public int BranchId { get; set; } = 1;
    public int? GroupId { get; set; }
    [Required, MaxLength(200)] public string PartyName { get; set; } = "";
    [MaxLength(500)] public string? PartyAddress { get; set; }
    [MaxLength(32)] public string? PartyPhone { get; set; }
    [MaxLength(100)] public string? VehicleName { get; set; }
    [MaxLength(50)] public string? VehicleNumber { get; set; }
    [MaxLength(200)] public string? TransportCompany { get; set; }
    [MaxLength(80)] public string? BiltyNumber { get; set; }
    [MaxLength(150)] public string? DriverName { get; set; }
    [MaxLength(32)] public string? DriverPhone { get; set; }
    public DateTime? ArrivalAt { get; set; }
    public DateTime? DepartureAt { get; set; }
    public decimal? GrossWeightKg { get; set; }
    public decimal? TareWeightKg { get; set; }
    public decimal? NetWeightKg { get; set; }
    [MaxLength(150)] public string? IssuedBy { get; set; }
    [MaxLength(150)] public string? DriverSignature { get; set; }
    [MaxLength(150)] public string? GateKeeper { get; set; }
    [MaxLength(150)] public string? ReceivedBy { get; set; }
    [MaxLength(150)] public string? ReceiverReference { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
    public bool IsCancelled { get; set; }
    [MaxLength(500)] public string? CancellationReason { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public DateTime? CancelledUtc { get; set; }
    [MaxLength(100)] public string? CreatedBy { get; set; }
    public int Revision { get; set; } = 1;
    public List<GatePassLine> Lines { get; set; } = [];
    public string PassNumber => (Direction == "Inward" ? "IGP-" : "OGP-") + Id.ToString("D6");
}

public class GatePassLine
{
    public int Id { get; set; }
    public int GatePassId { get; set; }
    public long? ItemId { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int? ItemGroupId { get; set; }
    // Document snapshots preserve the unit and price on reopening.
    public decimal? UnitPrice { get; set; }
    public int SerialNo { get; set; }
    [Required, MaxLength(300)] public string Description { get; set; } = "";
    [MaxLength(16)] public string? PackingType { get; set; }
    public decimal PackingQuantity { get; set; }
    public decimal BundleQuantity { get; set; }
    public decimal OpenCartonQuantity { get; set; }
    // False on existing documents so introducing carton arithmetic cannot rewrite historical totals.
    public bool CalculateFromBundles { get; set; }
    public decimal TotalQuantity { get; set; }
    [Required, MaxLength(8)] public string Unit { get; set; } = "pcs";
}
