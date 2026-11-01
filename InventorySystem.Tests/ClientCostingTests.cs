using InventorySystem.Core.Services;
using System.Text.Json;
using InventorySystem.Services.Services;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventorySystem.Tests;

public class ClientCostingTests
{
    private static ApplicationDbContext Database()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.ItemCategory.Add(new ItemCategory { CategoryId = 1, CategoryName = "Test", IsActive = true });
        for (var i = 1; i <= 5; i++) db.Items.Add(new Items { ItemID = i, CategoryID = 1, ItemName = "Raw " + i, ItemType = "RawMaterial", StockUnit = "kg", IsActive = true });
        db.Items.Add(new Items { ItemID = 6, CategoryID = 1, ItemName = "Box", ItemType = "FinishedGoods", StockUnit = "pcs", IsActive = true });
        db.SaveChanges(); return db;
    }
    public static CostSheetViewModel Sample(bool laminated = false) => new()
    {
        ItemName = "Client sample", FinishedItemId = 6, Status = "Final", BranchID = 1,
        BoxStyle = "Regular Slotted Carton (RSC)", SizeUnit = "mm", SizeType = "External",
        Length = 416, Width = 312, Height = 111, Flap = 45, AutoFlapGap = 0,
        ClientCosting = new() { GlueLaminationItemId = 4, LaminationChemicalItemId = 5, FlutedLayers = [false, true, false, true, false] },
        HasLamination = laminated, LaminationCost = 999, PrintingType = "Flexo", PrintingColors = 3,
        GlueItemId = 2, SilicateItemId = 3, GlueRate = 50, SilicateRate = 45,
        LabourRate = 10, EnergyRate = 5, BindingType = "Pins", BindingPins = 2,
        WastePct = 5.25m, AdminExpPct = 2.5m, SellingDistPct = 2.5m, RepairMaintPct = 2.25m, StoreSparesPct = 2.25m, MfgCostPct = 2.75m,
        ProfitPct = 5, GSTPercentage = 18, MOQ = 1,
        Plies = new[] { 140m, 110m, 110m, 110m, 140m }.Select(rate => new CostSheetPlyViewModel { ItemId = 1, GSM = 110, Rate = rate }).ToList()
    };

    [Theory]
    [InlineData(false, 80.53, 84.55, 99.77, 15.22)]
    [InlineData(true, 89.08, 93.54, 110.38, 16.84)]
    public async Task ClientSample_ReconcilesWithConfirmedRules(bool laminated, decimal basis, decimal exGst, decimal final, decimal gst)
    {
        await using var db = Database(); var m = Sample(laminated);
        await CostSheetCalculation.ValidateAndCalculateAsync(db, m);
        Assert.Equal(439.002m, decimal.Round(m.AdjWidthMm!.Value,6)); Assert.Equal(1514.97m,decimal.Round(m.AdjLengthMm!.Value,6));
        Assert.InRange(m.TotalSheetSqIn!.Value, 1030.86809464318m, 1030.86809464320m);
        Assert.Equal(.446266m, m.ClientCosting!.Totals!.PaperWeightKg);
        Assert.Equal(.073158m, m.Plies[0].KGs); Assert.Equal(.113395m, m.Plies[1].KGs);
        Assert.Equal(53.48m, m.PaperCost); Assert.Equal(3.09m, m.PrintingCost); Assert.Equal(2.50m, m.BindingCost);
        Assert.Equal(laminated ? 25.35m : 23.96m, m.ClientCosting.Totals.OperatingCost);
        Assert.Equal(basis, m.ManufacturingTotal); Assert.Equal(exGst, m.FinalCostWOGST); Assert.Equal(final, m.FinalRateWithGST);
        Assert.Equal(gst, m.ClientCosting.Totals.GstAmount); Assert.Equal(laminated ? 7.16m : 0m, m.LaminationCost);
    }

    [Fact]
    public async Task ToggleLamination_AndRecalculate_DoesNotDuplicateAdhesiveEstimates()
    {
        await using var db = Database(); var m = Sample(true);
        await CostSheetCalculation.ValidateAndCalculateAsync(db, m); Assert.Equal(110.38m, m.FinalRateWithGST);
        m.HasLamination = false;
        await CostSheetCalculation.ValidateAndCalculateAsync(db, m);
        Assert.Equal(0m, m.LaminationCost); Assert.Equal(99.77m, m.FinalRateWithGST);
        Assert.Empty(m.Materials);
        Assert.Equal(0m, m.ClientCosting!.Totals!.GlueLaminationCost);
        Assert.Equal(0m, m.ClientCosting.Totals.LaminationChemicalCost);
        Assert.Equal(0m, m.ClientCosting.Totals.GlueLaminationKg);
        Assert.Equal(0m, m.ClientCosting.Totals.LaminationChemicalKg);
        m.HasLamination = true;
        await CostSheetCalculation.ValidateAndCalculateAsync(db, m);
        await CostSheetCalculation.ValidateAndCalculateAsync(db, m);
        Assert.Equal(2, m.Materials.Count);
        Assert.Equal(.001663m, m.Materials.Single(x => x.ItemId == 4).Quantity);
        Assert.Equal(.001663m, m.Materials.Single(x => x.ItemId == 5).Quantity);
        Assert.Equal(.033254m, m.GlueKgPerBox); Assert.Equal(.033254m, m.SilicateKgPerBox);
    }

    [Fact]
    public async Task Controller_SaveReloadUpdate_PreservesClientInputsAndRecomputesTamperedOutputs()
    {
        await using var db = Database(); var controller = new CostSheetController(db, new PermissionService(db)); var m = Sample();
        m.FinalRateWithGST = 123456m; m.ClientCosting!.Totals = new() { GstAmount = -999 };
        Assert.IsType<OkObjectResult>(await controller.Save(m));
        var saved = await db.CostSheet.Include(x => x.Plies).Include(x => x.Materials).SingleAsync();
        Assert.Equal(99.77m, saved.FinalRateWithGST); Assert.Empty(saved.Materials!);
        var data = JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(await controller.GetById(saved.CostSheetId)).Value).GetProperty("data");
        Assert.Equal(1.55m, data.GetProperty("clientCosting").GetProperty("FluteFactor").GetDecimal());
        Assert.Equal(5, data.GetProperty("plies").GetArrayLength());
        m.CostSheetId = saved.CostSheetId; m.HasLamination = true; m.CommPersonAPct = 5; m.CommPersonBPct = 2; m.TaxPct = 10; m.ItemName = "Updated client sample";
        Assert.IsType<OkObjectResult>(await controller.Save(m));
        Assert.Single(await db.CostSheet.ToListAsync()); Assert.Equal(5, await db.CostSheetPly.CountAsync());
        Assert.Equal(2, await db.CostSheetMaterial.CountAsync()); Assert.Equal(125.52m, saved.FinalRateWithGST);
        Assert.Equal(4.45m, saved.CommPersonACost); Assert.Equal(1.78m, saved.CommPersonBCost);
        Assert.Equal(8.91m, JsonSerializer.Deserialize<ClientCostingOptions>(saved.ClientCostingJson!)!.Totals!.AdditionalTaxAmount);
        Assert.Equal("Updated client sample", saved.ItemName);
        Assert.Equal(16.84m, JsonSerializer.Deserialize<ClientCostingOptions>(saved.ClientCostingJson!)!.Totals!.GstAmount);
        Assert.Empty(await db.Stock.ToListAsync());
    }

    [Fact]
    public async Task IncreasedColoursAndPinRate_RecalculateTheClientPrice()
    {
        await using var db = Database(); var m = Sample(); m.PrintingColors = 4; m.ClientCosting!.PinRate = 2;
        await CostSheetCalculation.ValidateAndCalculateAsync(db, m);
        Assert.Equal(4.12m, m.PrintingCost); Assert.Equal(4m, m.BindingCost); Assert.Equal(102.91m, m.FinalRateWithGST);
    }

    [Fact]
    public async Task MoqAndWithholding_FollowWorkbookBases()
    {
        await using var db = Database(); var m = Sample(); m.MOQ = 2; m.WHTaxPct = 2;
        await CostSheetCalculation.ValidateAndCalculateAsync(db, m);
        Assert.Equal(42.28m, m.ClientCosting!.Totals!.PricePerUnit); Assert.Equal(.48m, m.ClientCosting.Totals.WithholdingAmount);
        Assert.Equal(57.98m, m.FinalRateWithGST);
    }

    [Fact]
    public async Task EquivalentInchDimensions_GiveSameWeightAndRate()
    {
        await using var db = Database(); var m = Sample();
        m.SizeUnit = "in"; m.Length /= 25.4m; m.Width /= 25.4m; m.Height /= 25.4m; m.Flap /= 25.4m;
        await CostSheetCalculation.ValidateAndCalculateAsync(db, m); Assert.Equal(99.77m, m.FinalRateWithGST);
        Assert.Equal(.446266m, m.ClientCosting!.Totals!.PaperWeightKg);
    }

    [Theory]
    [InlineData("commission")] [InlineData("tax")] [InlineData("moq")] [InlineData("factor")]
    [InlineData("style")] [InlineData("unit")] [InlineData("version")]
    public async Task UnsupportedOrInvalidInputs_AreRejectedBeforeSaving(string field)
    {
        await using var db = Database(); var m = Sample();
        switch(field) {
            case "commission": m.CommPersonBPct = 101; break;
            case "tax": m.TaxPct = 101; break;
            case "moq": m.MOQ = 0; break;
            case "factor": m.ClientCosting!.FluteFactor = -1; break;
            case "style": m.BoxStyle = "Other"; break;
            case "unit": m.HasLamination = true; (await db.Items.FindAsync(4L))!.StockUnit = "pcs"; await db.SaveChangesAsync(); break;
            case "version": m.ClientCosting!.Version = 999; break;
        }
        Assert.IsType<BadRequestObjectResult>(await new CostSheetController(db,new PermissionService(db)).Save(m));
        Assert.Empty(await db.CostSheet.ToListAsync());
    }

    [Fact]
    public async Task LegacyRecipe_KeepsExistingMethod_WithNoLaminationChargeWhenNo()
    {
        await using var db = Database(); var m = Sample(); m.ClientCosting = null;
        foreach (var p in m.Plies) p.KGs = .1m;
        m.GlueKgPerBox = m.SilicateKgPerBox = 0; m.HasLamination = false; m.LaminationCost = 100;
        await CostSheetCalculation.ValidateAndCalculateAsync(db,m);
        Assert.Equal(0m,m.LaminationCost); Assert.Equal(.5m,m.Plies.Sum(p=>p.KGs)); Assert.Null(m.ClientCosting);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FinalClientRecipe_ProducesFinishedGoods_AndConsumesEveryLinkedEstimate(bool laminated)
    {
        await using var db = Database();
        db.Parties.Add(new Party { PartyId = 7, PartyName = "Client QA", PartyType = "customer", Status = "active" });
        for (var i = 1; i <= 5; i++) db.Stock.Add(new Stock { ItemId = i, BranchId = 1, Quantity = 100 });
        await db.SaveChangesAsync();
        var m = Sample(laminated);
        Assert.IsType<OkObjectResult>(await new CostSheetController(db,new PermissionService(db)).Save(m));
        var sheet = await db.CostSheet.SingleAsync();
        var production = new ProductionController(db,new StockService(db));
        Assert.IsType<OkObjectResult>(await production.Save(new ProductionOrderViewModel {
            CustomerId = "7", CostSheetId = sheet.CostSheetId, OrderQty = 10, OrderDate = DateTime.Today,
            DeliveryDate = DateTime.Today, AgreedUnitPrice = 90, AgreedGstPercent = 0 }));
        var order = await db.ProductionOrder.Include(o=>o.Materials).SingleAsync();
        Assert.Equal(laminated ? 5 : 3,order.Materials!.Count);
        var estimates = order.Materials.ToDictionary(x=>x.ItemId,x=>x.KgPerUnit);
        await new EstimatedProductionService(db,new StockService(db)).RecordAsync(order.ProductionOrderId,10);
        foreach (var (id,perBox) in estimates)
            Assert.Equal(100m - decimal.Ceiling(perBox*10*1000m)/1000m,(await db.Stock.SingleAsync(s=>s.ItemId==id)).Quantity);
        Assert.Equal(10,(await db.Stock.SingleAsync(s=>s.ItemId==6)).Quantity);
        if (!laminated) foreach (var id in new[] { 4, 5 })
            Assert.Equal(100,(await db.Stock.SingleAsync(s=>s.ItemId==id)).Quantity);
    }
    [Fact]
    public async Task CommissionProfitAndAdditionalTax_UseBaseCost_GstIncludesProfit_MoqDividesPrice()
    {
        await using var db = Database();
        var m = new CostSheetViewModel {
            ItemName = "Exact basis", FinishedItemId = 6, Status = "Final", BoxStyle = "Regular Slotted Carton (RSC)",
            SizeUnit = "in", Length = 40, Width = 10, Height = 5.5m, Flap = 0,
            ClientCosting = new() { WidthAllowanceIn = 0, LengthAllowanceIn = 0, WoodRate = 0, StarchRate = 0,
                GlueApplications = 0, SilicateApplications = 0, FlutedLayers = [false] },
            Plies = [new() { ItemId = 1, GSM = 1000, Rate = 100 }], HasLamination = false,
            CommPersonAPct = 5, CommPersonBPct = 2, ProfitPct = 10, GSTPercentage = 18, TaxPct = 10, MOQ = 2
        };
        await CostSheetCalculation.ValidateAndCalculateAsync(db,m);
        Assert.Equal(100m,m.ManufacturingTotal);
        Assert.Equal(5m,m.CommPersonACost); Assert.Equal(2m,m.CommPersonBCost); Assert.Equal(10m,m.ProfitAmount);
        Assert.Equal(19.80m,m.ClientCosting!.Totals!.GstAmount); Assert.Equal(10m,m.ClientCosting.Totals.AdditionalTaxAmount);
        Assert.Equal(117m,m.FinalCostWOGST); Assert.Equal(58.50m,m.ClientCosting.Totals.PricePerUnit);
        Assert.Equal(88.30m,m.FinalRateWithGST);
        Assert.Empty(m.Materials); // No lamination stock links are needed when No.
    }

    [Fact]
    public async Task LabourRateTen_OnHalfKgPaper_CostsFiveRupees()
    {
        await using var db = Database(); var m = Sample();
        m.SizeUnit = "in"; m.Length = 40; m.Width = 10; m.Height = 5.5m; m.Flap = 0;
        m.ClientCosting!.WidthAllowanceIn = m.ClientCosting.LengthAllowanceIn = 0;
        m.ClientCosting.FlutedLayers = [false];
        m.Plies = [new() { ItemId = 1, GSM = 500, Rate = 100 }];
        await CostSheetCalculation.ValidateAndCalculateAsync(db,m);
        Assert.Equal(.5m,m.ClientCosting.Totals!.PaperWeightKg); Assert.Equal(5m,m.LabourCost);
    }

}
