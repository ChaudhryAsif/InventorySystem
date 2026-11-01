using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Models;

public static class CostSheetCalculation
{
    private static decimal R(decimal x) => decimal.Round(x, 2, MidpointRounding.AwayFromZero);
    public static async Task ValidateAndCalculateAsync(ApplicationDbContext db, CostSheetViewModel m)
    {
        if (string.IsNullOrWhiteSpace(m.ItemName) || m.Status is not ("Draft" or "Final"))
            throw new InvalidOperationException("Enter a product name and save as Draft or Final.");
        var rows = (m.Plies ?? []).Select((p, i) => (p, i)).Where(x => x.p.ItemId.HasValue || x.p.KGs > 0 || m.ClientCosting != null && x.p.GSM > 0).ToList();
        if (m.ClientCosting?.FlutedLayers != null)
            m.ClientCosting.FlutedLayers = rows.Select(x => x.i < m.ClientCosting.FlutedLayers.Count ? m.ClientCosting.FlutedLayers[x.i] : x.i % 2 == 1).ToList();
        m.Plies = rows.Select(x => x.p).ToList();
        if (m.Plies.Count > 20) throw new InvalidOperationException("A cost sheet supports up to 20 paper layers.");
        foreach (var property in typeof(CostSheetViewModel).GetProperties())
            if (property.PropertyType == typeof(decimal?) && property.GetValue(m) is decimal value && (value < 0 || value > 100_000_000))
                throw new InvalidOperationException("Costs, weights and dimensions must be nonnegative and within valid limits.");
        var percentages = new[] { m.ClientCosting == null ? m.WastePct : 0, m.ClientCosting == null ? m.AdminExpPct : 0, m.ClientCosting == null ? m.SellingDistPct : 0, m.ClientCosting == null ? m.RepairMaintPct : 0, m.ClientCosting == null ? m.StoreSparesPct : 0,
            m.ClientCosting == null ? m.MfgCostPct : 0, m.CommPersonAPct, m.CommPersonBPct, m.ProfitPct, m.WHTaxPct, m.GSTPercentage, m.TaxPct };
        if (percentages.Any(p => p > 100)) throw new InvalidOperationException("Percentages must be between 0 and 100.");
        m.Materials ??= [];
        if (m.ClientCosting != null) ClientCartonCalculation.Prepare(m);
        if (m.HasLamination != true) m.LaminationCost = 0;
        if(m.ClientCosting?.AutomaticApplications==true){
            foreach(var link in new[]{(m.GlueItemId,"Glue"),(m.SilicateItemId,"Silicate"),(m.ClientCosting.GlueLaminationItemId,"LaminationGlue"),(m.ClientCosting.LaminationChemicalItemId,"LaminationChemical")}){
                if(!link.Item1.HasValue)continue;
                var material=await db.Items.AsNoTracking().SingleOrDefaultAsync(i=>i.ItemID==link.Item1&&i.IsActive==true&&i.Category.IsActive==true);
                if(material==null||material.ItemType!="RawMaterial"||material.StockUnit!="kg"||MaterialRoles.Resolve(material.MaterialRole,material.ItemName)!=link.Item2)throw new InvalidOperationException("Select the relevant "+link.Item2+" item in KG. Set its costing material use in Items if necessary.");
            }
        }
        if (m.GlueSilicateKg > 0 && m.GlueKgPerBox == null && m.SilicateKgPerBox == null)
            throw new InvalidOperationException("Separate the old combined adhesive weight into glue and silicate kg per box, and link the stock items.");
        if (m.Status == "Final")
        {
            if (m.Plies.Count == 0 || m.Plies.Any(p => p.KGs is null or <= 0)) throw new InvalidOperationException("Enter paper kg per box for each selected layer.");
        }
        foreach (var ply in m.Plies)
        {
            if (ply.KGs is < 0 or > 1000000 || ply.Rate is < 0 or > 100000000 || ply.GSM < 0 || ply.ItemId.HasValue &&
                !await db.Items.AnyAsync(i => i.ItemID == ply.ItemId && i.IsActive == true && i.ItemType == "RawMaterial" && i.StockUnit == "kg"))
                throw new InvalidOperationException("Select active raw-material items stocked in kg for the paper layers.");
            ply.Cost = R((ply.KGs ?? 0) * (ply.Rate ?? 0));
        }
        // Other materials: any active raw material in kg or pcs; cost only, not box weight.
        m.Materials = (m.Materials ?? []).Where(x => x.ItemId.HasValue || x.Quantity > 0).ToList();
        if (m.Materials.Count > 20) throw new InvalidOperationException("A cost sheet supports up to 20 other materials.");
        if (m.Materials.Select(x => x.ItemId).Distinct().Count() != m.Materials.Count)
            throw new InvalidOperationException("List each other material once.");
        foreach (var other in m.Materials)
        {
            var unit = other.ItemId == null ? null : await db.Items
                .Where(i => i.ItemID == other.ItemId && i.IsActive == true && i.ItemType == "RawMaterial" && i.ItemID != m.FinishedItemId)
                .Select(i => i.StockUnit).FirstOrDefaultAsync();
            if (unit is not ("kg" or "pcs" or "in") || other.Quantity is null or <= 0 or > 1000000 || other.Rate is < 0 or > 100000000)
                throw new InvalidOperationException("Select an active raw material for each other-material line and enter a quantity per box above zero.");
            if (m.ClientCosting != null && (other.ItemId == m.ClientCosting.GlueLaminationItemId || other.ItemId == m.ClientCosting.LaminationChemicalItemId) && unit != "kg")
                throw new InvalidOperationException("Client adhesive estimates require raw-material stock items in kg.");
            other.Unit = unit;
            other.Cost = R(other.Quantity.Value * (other.Rate ?? 0));
        }
        async Task Adhesive(long? id, decimal? kg)
        {
            if (kg > 0 && id.HasValue && !await db.Items.AnyAsync(i => i.ItemID == id && i.IsActive == true && i.ItemType == "RawMaterial" && i.StockUnit == "kg"))
                throw new InvalidOperationException("Link each adhesive quantity to an active raw-material item stocked in kg.");
        }
        await Adhesive(m.GlueItemId, m.GlueKgPerBox); await Adhesive(m.SilicateItemId, m.SilicateKgPerBox);
        if (m.ClientCosting != null) { ClientCartonCalculation.Calculate(m); return; }
        m.GlueSilicateKg = (m.GlueKgPerBox ?? 0) + (m.SilicateKgPerBox ?? 0);
        var adhesiveCost = (m.GlueKgPerBox ?? 0) * (m.GlueRate ?? 0) + (m.SilicateKgPerBox ?? 0) * (m.SilicateRate ?? 0);
        m.GlueSilicateCost = R(adhesiveCost);
        var weight = m.Plies.Sum(p => p.KGs ?? 0) + m.GlueSilicateKg.Value;
        var paper = m.Plies.Sum(p => p.Cost ?? 0) + m.Materials.Sum(x => x.Cost ?? 0) + adhesiveCost + (m.HasLamination == true ? m.LaminationCost ?? 0 : 0) + (m.DickelCost ?? 0) + (m.PrintingCost ?? 0);
        var labour = (m.LabourRate ?? 0) * weight; var energy = (m.EnergyRate ?? 0) * weight; var freight = (m.FreightRate ?? 0) * weight;
        var binding = m.BindingType == "None" ? 0 : m.BindingType == "Pins" ? (m.BindingPins ?? 0) * 0.05m : m.BindingCost ?? 0;
        var sub = paper + labour + energy + freight + binding;
        decimal P(decimal basis, decimal? pct) => basis * (pct ?? 0) / 100;
        m.PaperCost = R(paper); m.LabourCost = R(labour); m.EnergyCost = R(energy); m.FreightCost = R(freight); m.BindingCost = R(binding); m.SubTotal = R(sub);
        m.WasteCost = R(P(sub,m.WastePct)); m.AdminExpCost = R(P(sub,m.AdminExpPct)); m.SellingDistCost = R(P(sub,m.SellingDistPct));
        m.RepairMaintCost = R(P(sub,m.RepairMaintPct)); m.StoreSparesCost = R(P(sub,m.StoreSparesPct)); m.MfgCostValue = R(P(sub,m.MfgCostPct));
        var manufacturing = sub + P(sub,m.WastePct)+P(sub,m.AdminExpPct)+P(sub,m.SellingDistPct)+P(sub,m.RepairMaintPct)+P(sub,m.StoreSparesPct)+P(sub,m.MfgCostPct);
        m.ManufacturingTotal = R(manufacturing);
        var commA=P(manufacturing,m.CommPersonAPct); var commB=P(manufacturing,m.CommPersonBPct);
        m.CommPersonACost=R(commA); m.CommPersonBCost=R(commB);
        var after=manufacturing+commA+commB; var profit=P(after,m.ProfitPct);
        m.ProfitAmount=R(profit); var final=after+profit+P(after,m.WHTaxPct);
        m.FinalCostWOGST=R(final); m.FinalRateWithGST=R(final+P(final,m.GSTPercentage)+P(final,m.TaxPct));
    }
}
