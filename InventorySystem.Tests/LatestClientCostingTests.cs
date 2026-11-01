using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Services.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace InventorySystem.Tests;
public class LatestClientCostingTests
{
    private static CostSheetViewModel Read() {
        using var file=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"TestData/LatestClientCosting.xlsx"));
        return CostSheetExcelImport.Read(file).Data;
    }
    [Fact]
    public void ActualNewClientFile_ReconcilesWithWorkbookAndIgnoresUnusedLayers()
    {
        var m=Read();Assert.Equal(2,m.ClientCosting!.Version);Assert.Equal(3,m.Plies.Count);
        Assert.Equal(new[]{1m,1.45m,1m},m.ClientCosting.LayerFluteFactors);
        Assert.Equal(300,m.Length);Assert.Equal(300,m.Width);Assert.Equal(380,m.Height);
        Assert.Equal(.332536m,m.ClientCosting.Totals!.PaperWeightKg);Assert.Equal(42.36m,m.PaperCost);
        Assert.Equal(16.13m,m.ClientCosting.Totals.OperatingCost);Assert.Equal(58.49m,m.ManufacturingTotal);
        Assert.Equal(61.27m,m.FinalCostWOGST);Assert.Equal(61.27m,m.FinalRateWithGST);
        Assert.Equal("None",m.BindingType);Assert.False(m.HasLamination);
        Assert.All(m.Plies,p=>Assert.Null(p.ItemId));
    }
    [Fact]
    public void NewWorkbookFormula_ReproducesFreightCommissionTaxAndMoqExactly()
    {
        var m=Read();m.FreightRate=10;m.CommPersonAPct=5;m.CommPersonBPct=9;m.TaxPct=10;m.GSTPercentage=18;m.WHTaxPct=2;m.MOQ=2;
        ClientCartonCalculation.Prepare(m);ClientCartonCalculation.Calculate(m);
        Assert.Equal(68.49m,m.ManufacturingTotal);Assert.Equal(80.65m,m.CommPersonACost);Assert.Equal(0,m.CommPersonBCost);
        Assert.Equal(12.33m,m.ClientCosting!.Totals!.GstAmount);
        Assert.Equal(88.60m,m.FinalCostWOGST);Assert.Equal(46.07m,m.FinalRateWithGST);
    }
    [Fact]
    public void DifferentActiveFluteFactors_AreCalculatedPerLayer()
    {
        var m=Read();m.Plies.Add(new(){GSM=110,Rate=110});m.Plies.Add(new(){GSM=110,Rate=140});
        m.ClientCosting!.FlutedLayers=[false,true,false,true,false];m.ClientCosting.LayerFluteFactors=[1,1.45m,1,1.35m,1];
        ClientCartonCalculation.Prepare(m);ClientCartonCalculation.Calculate(m);
        Assert.Equal(.139761m,m.Plies[1].KGs);Assert.Equal(.130123m,m.Plies[3].KGs);
        Assert.True(m.FinalRateWithGST>61.27m);
    }
    [Fact]
    public async Task NewWorkbook_SaveReloadUpdate_PreservesFormulaAndFactors()
    {
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(x=>x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.ItemCategory.Add(new(){CategoryId=1,CategoryName="QA",IsActive=true});
        for(int i=1;i<=3;i++)db.Items.Add(new(){ItemID=i,CategoryID=1,ItemName="Material "+i,ItemType="RawMaterial",StockUnit="kg",IsActive=true});
        db.Items.Add(new(){ItemID=4,CategoryID=1,ItemName="Carton",ItemType="FinishedGoods",StockUnit="pcs",IsActive=true});await db.SaveChangesAsync();
        var m=Read();m.FinishedItemId=4;m.GlueItemId=2;m.SilicateItemId=3;m.BranchID=1;m.Status="Final";
        foreach(var p in m.Plies){p.ItemId=1;p.KGs=999;}
        var controller=new CostSheetController(db,new PermissionService(db));
        Assert.IsType<OkObjectResult>(await controller.Save(m));var saved=await db.CostSheet.Include(x=>x.Plies).SingleAsync();
        Assert.Equal(61.27m,saved.FinalRateWithGST);Assert.Equal(3,saved.Plies!.Count);
        var options=JsonSerializer.Deserialize<ClientCostingOptions>(saved.ClientCostingJson!)!;
        Assert.Equal(2,options.Version);Assert.Equal(1.45m,options.LayerFluteFactors[1]);
        Assert.IsType<JsonResult>(await controller.GetById(saved.CostSheetId));
        m.CostSheetId=saved.CostSheetId;m.ClientCosting!.LayerFluteFactors[1]=1.5m;
        Assert.IsType<OkObjectResult>(await controller.Save(m));Assert.Single(await db.CostSheet.ToListAsync());
        Assert.True(saved.FinalRateWithGST>61.27m);Assert.Empty(await db.Stock.ToListAsync());
    }
}
