namespace InventorySystem.Core.Models;

// Stored with the recipe, so later default changes cannot alter a saved quotation.
public class ClientCostingOptions
{
    public int Version { get; set; } = 1;
    public decimal WidthAllowanceIn { get; set; } = .63m;
    public decimal LengthAllowanceIn { get; set; } = .55m;
    public decimal FluteFactor { get; set; } = 1.55m;
    public int DeckelUp { get; set; } = 2;
    public decimal PrintingRate { get; set; } = 1m;
    public decimal LaminationRate { get; set; } = 1m;
    public decimal PinRate { get; set; } = 1.25m;
    public decimal WoodRate { get; set; } = 4m;
    public decimal StarchRate { get; set; } = 4.5m;
    public decimal GlueApplications { get; set; } = 2m;
    public decimal SilicateApplications { get; set; } = 2m;
    public decimal GlueLaminationApplications { get; set; } = 1m;
    public decimal LaminationChemicalApplications { get; set; } = 1m;
    public decimal GlueLaminationRate { get; set; } = 640m;
    public decimal LaminationChemicalRate { get; set; } = 200m;
    public long? GlueLaminationItemId { get; set; }
    public long? LaminationChemicalItemId { get; set; }
    public List<bool> FlutedLayers { get; set; } = [];
    public ClientCostingTotals? Totals { get; set; }
}

public class ClientCostingTotals
{
    public decimal PaperWeightKg { get; set; }
    public decimal DeckelIn { get; set; }
    public decimal WoodCost { get; set; }
    public decimal StarchCost { get; set; }
    public decimal GlueLaminationKg { get; set; }
    public decimal LaminationChemicalKg { get; set; }
    public decimal GlueLaminationCost { get; set; }
    public decimal LaminationChemicalCost { get; set; }
    public decimal OperatingCost { get; set; }
    public decimal GstAmount { get; set; }
    public decimal AdditionalTaxAmount { get; set; }
    public decimal WithholdingAmount { get; set; }
    public decimal PricePerUnit { get; set; }
}
