using InventorySystem.Core.Models;

namespace InventorySystem.Models;

public static class ClientCartonCalculation
{
    private static decimal R(decimal n) => decimal.Round(n, 2, MidpointRounding.AwayFromZero);
    private static decimal K(decimal n) => decimal.Round(n, 6, MidpointRounding.AwayFromZero);

    public static void Prepare(CostSheetViewModel m)
    {
        var c = m.ClientCosting!;
        if(c.AutomaticApplications){
            c.GlueApplications=c.SilicateApplications=Math.Max(0,(m.Plies.Count-1)/2);
            c.GlueLaminationApplications=c.LaminationChemicalApplications=m.HasLamination==true?1:0;
        }
        if (m.BoxStyle != null && m.BoxStyle != "Regular Slotted Carton (RSC)")
            throw new InvalidOperationException("The supplied client formula is for RSC cartons. Select RSC or use the manual method for other styles.");
        if (c.Version is not (1 or 2) || c.FlutedLayers == null || c.FlutedLayers.Count > 20 || c.DeckelUp is < 1 or > 100 || c.LayerFluteFactors == null || c.LayerFluteFactors.Count > 20 || c.LayerFluteFactors.Any(f => f < 1 || f > 10))
            throw new InvalidOperationException("Review the client costing settings and layer types.");
        foreach (var p in typeof(ClientCostingOptions).GetProperties().Where(p => p.PropertyType == typeof(decimal)))
            if (p.GetValue(c) is decimal n && (n < 0 || n > 1000000))
                throw new InvalidOperationException("Client costing rates and allowances must be nonnegative and within valid limits.");
        if (c.FluteFactor is < 1 or > 10 || m.Plies.Any(p => p.GSM > 10000) || new[] { m.Length, m.Width, m.Height, m.Flap }.Any(x => x > 1000000) || m.MOQ is null or < 1 or > 1000000 || m.SizeUnit is not ("mm" or "in") ||
            m.PrintingColors is < 0 or > 100 || m.BindingPins is < 0 or > 1000000)
            throw new InvalidOperationException("Enter valid units, flute factor, colours, pins and a pricing divisor (MOQ) of at least 1.");
        if (m.Status == "Final" && (m.Length is null or <= 0 || m.Width is null or <= 0 || m.Height is null or <= 0))
            throw new InvalidOperationException("Enter positive carton length, width and height before finalizing.");
        var factor = m.SizeUnit == "in" ? 25.4m : 1m;
        var width = ((m.Width ?? 0) + (m.Height ?? 0) - (m.AutoFlapGap ?? 0)) * factor / 25.4m;
        if (width < 0) throw new InvalidOperationException("Both flap gap cannot exceed width plus height.");
        var length = 2 * ((m.Length ?? 0) + (m.Width ?? 0)) * factor / 25.4m;
        var flap = (m.Flap ?? 0) * factor / 25.4m;
        m.SheetWidthIn = width; m.SheetLengthIn = length; m.WithFlap = flap;
        m.AdjWidthIn = width + c.WidthAllowanceIn;
        m.AdjLength1In = length + flap + c.LengthAllowanceIn;
        m.AdjLength2In = m.AdjLength1In / 2;
        m.AdjWidthMm = m.AdjWidthIn * 25.4m; m.AdjLengthMm = m.AdjLength1In * 25.4m;
        m.TotalSheetSqIn = m.AdjWidthIn * m.AdjLength1In;
        // 1550 is the area conversion used by the client's workbook.
        var area = (m.Length ?? 0) > 0 && (m.Width ?? 0) > 0 && (m.Height ?? 0) > 0 ? m.TotalSheetSqIn.Value : 0;
        m.TotalSheetSqIn = area;
        c.FlutedLayers = m.Plies.Select((_, i) => i < c.FlutedLayers.Count ? c.FlutedLayers[i] : i % 2 == 1).ToList();
        foreach (var (ply, i) in m.Plies.Select((p, i) => (p, i)))
            ply.KGs = K(area / 1550m * (ply.GSM ?? 0) / 1000m * LayerFactor(c, i));
        m.GlueKgPerBox = K(area / 1550m * .025m * c.GlueApplications);
        m.SilicateKgPerBox = K(area / 1550m * .025m * c.SilicateApplications);
        m.GlueSilicateKg = m.GlueKgPerBox + m.SilicateKgPerBox;
        m.PrintingCost = R(area / 1000m * (m.PrintingColors ?? 0) * c.PrintingRate);
        m.LaminationCost = m.HasLamination == true ? R(area / 144m * c.LaminationRate) : 0;
        var glueLamKg = m.HasLamination == true ? area / 1550m * .0025m * c.GlueLaminationApplications : 0;
        var chemicalKg = m.HasLamination == true ? area / 1550m * .0025m * c.LaminationChemicalApplications : 0;
        var managedIds = new[] { c.GlueLaminationItemId, c.LaminationChemicalItemId }.Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
        m.Materials = m.Materials.Where(x => !managedIds.Contains(x.ItemId ?? -1)).ToList();
        AddEstimate(c.GlueLaminationItemId, glueLamKg, c.GlueLaminationRate, "Glue Lamination");
        AddEstimate(c.LaminationChemicalItemId, chemicalKg, c.LaminationChemicalRate, "Lamination Chemicals");

        void AddEstimate(long? id, decimal kg, decimal rate, string name)
        {
            if (kg <= 0) return;
            if (id == null) return;
            var existing = m.Materials.SingleOrDefault(x => x.ItemId == id);
            if (existing != null) {
                var amount = existing.Quantity!.Value * (existing.Rate ?? 0) + K(kg) * rate;
                existing.Quantity += K(kg); existing.Rate = amount / existing.Quantity.Value; return;
            }
            m.Materials.Add(new CostSheetMaterialViewModel { ItemId = id, Unit = "kg", Quantity = K(kg), Rate = rate });
        }
    }

    public static void Calculate(CostSheetViewModel m)
    {
        var c = m.ClientCosting!; var area = m.TotalSheetSqIn ?? 0;
        var weights = m.Plies.Select((p, i) => area / 1550m * (p.GSM ?? 0) / 1000m * LayerFactor(c, i)).ToList();
        var paper = m.Plies.Select((p, i) => weights[i] * (p.Rate ?? 0)).Sum();
        foreach (var (p, i) in m.Plies.Select((p, i) => (p, i))) p.Cost = R(weights[i] * (p.Rate ?? 0));
        var weight = weights.Sum();
        var glue = area / 1550m * .025m * c.GlueApplications * (m.GlueRate ?? 0);
        var silicate = area / 1550m * .025m * c.SilicateApplications * (m.SilicateRate ?? 0);
        var glueLamKg = m.HasLamination == true ? area / 1550m * .0025m * c.GlueLaminationApplications : 0;
        var chemicalKg = m.HasLamination == true ? area / 1550m * .0025m * c.LaminationChemicalApplications : 0;
        var glueLam = glueLamKg * c.GlueLaminationRate; var chemical = chemicalKg * c.LaminationChemicalRate;
        var other = m.Materials.Where(x => x.ItemId != c.GlueLaminationItemId && x.ItemId != c.LaminationChemicalItemId)
            .Sum(x => (x.Quantity ?? 0) * (x.Rate ?? 0));
        var printing = area / 1000m * (m.PrintingColors ?? 0) * c.PrintingRate;
        var labour = weight * (m.LabourRate ?? 0); var energy = weight * (m.EnergyRate ?? 0); var freight = c.Version == 2 ? m.FreightRate ?? 0 : weight * (m.FreightRate ?? 0);
        var binding = m.BindingType == "None" ? 0 : m.BindingType == "Pins" ? (m.BindingPins ?? 0) * c.PinRate : m.BindingCost ?? 0;
        var wood = weight * c.WoodRate; var starch = weight * c.StarchRate;
        var waste = weight * (m.WastePct ?? 0); var admin = weight * (m.AdminExpPct ?? 0); var selling = weight * (m.SellingDistPct ?? 0);
        var repair = weight * (m.RepairMaintPct ?? 0); var spares = weight * (m.StoreSparesPct ?? 0); var manufacturing = weight * (m.MfgCostPct ?? 0);
        var operating = glue + silicate + glueLam + chemical + labour + energy + (c.Version == 2 ? 0 : freight) + binding + wood + starch + waste + admin + selling + repair + spares + manufacturing;
        var basis = paper + printing + (m.LaminationCost ?? 0) + (m.DickelCost ?? 0) + other + operating + (c.Version == 2 ? freight : 0);
        if (basis > 1000000000000m) throw new InvalidOperationException("Calculated cost exceeds the supported limit. Review dimensions, GSM and rates.");
        // Each percentage uses its stated basis without compounding commission into profit or GST.
        // Version 2 reproduces the new workbook: K39=AJ63*G39; K41=AJ64*G41 (AJ64 is empty).
        var commissionA = c.Version == 2 ? operating * (m.CommPersonAPct ?? 0) : basis * (m.CommPersonAPct ?? 0) / 100;
        var commissionB = c.Version == 2 ? 0 : basis * (m.CommPersonBPct ?? 0) / 100;
        var additionalTax = basis * (m.TaxPct ?? 0) / 100;
        var profit = basis * (m.ProfitPct ?? 0) / 100;
        var gst = (c.Version == 2 ? basis : basis + profit) * (m.GSTPercentage ?? 0) / 100;
        // New P54 includes Z45 again and K52; neither commission is included in P54.
        var finalBeforeDivisor = c.Version == 2 ? basis + profit + freight + additionalTax : basis + commissionA + commissionB + profit;
        var withholding = (c.Version == 2 ? finalBeforeDivisor : operating) * (m.WHTaxPct ?? 0) / 100;
        m.PaperCost = R(paper); m.PrintingCost = R(printing); m.GlueSilicateCost = R(glue + silicate);
        m.LabourCost = R(labour); m.EnergyCost = R(energy); m.FreightCost = R(freight); m.BindingCost = R(binding);
        m.WasteCost = R(waste); m.AdminExpCost = R(admin); m.SellingDistCost = R(selling); m.RepairMaintCost = R(repair); m.StoreSparesCost = R(spares); m.MfgCostValue = R(manufacturing);
        m.SubTotal = R(basis - waste - admin - selling - repair - spares - manufacturing); m.ManufacturingTotal = R(basis);
        m.CommPersonACost = R(commissionA); m.CommPersonBCost = R(commissionB);
        m.ProfitAmount = R(profit); m.FinalCostWOGST = R(finalBeforeDivisor);
        var unit = finalBeforeDivisor / m.MOQ!.Value;
        m.FinalRateWithGST = R(unit + withholding + (c.Version == 2 ? 0 : gst + additionalTax));
        c.Totals = new ClientCostingTotals { PaperWeightKg = K(weight), DeckelIn = R((m.SheetWidthIn ?? 0) * c.DeckelUp + 1),
            WoodCost = R(wood), StarchCost = R(starch), GlueLaminationKg = K(glueLamKg), LaminationChemicalKg = K(chemicalKg),
            GlueLaminationCost = R(glueLam), LaminationChemicalCost = R(chemical), OperatingCost = R(operating),
            GstAmount = R(gst), AdditionalTaxAmount = R(additionalTax), WithholdingAmount = R(withholding), PricePerUnit = R(unit) };
    }
    private static decimal LayerFactor(ClientCostingOptions c, int i) => !c.FlutedLayers[i] ? 1 : c.Version == 2 && i < c.LayerFluteFactors.Count ? c.LayerFluteFactors[i] : c.FluteFactor;
}
