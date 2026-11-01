using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventorySystem.Tests;

public class CostSheetExcelImportTests
{
    [Fact]
    public void DownloadTemplate_PreservesClientWorkbookStructureStylesAndAllFormulas()
    {
        using var source=new ZipArchive(File.OpenRead(Path.Combine(AppContext.BaseDirectory,"TestData/LatestClientCosting.xlsx")),ZipArchiveMode.Read);
        using var template=new ZipArchive(File.OpenRead(Path.Combine(AppContext.BaseDirectory,"TestData/CostSheetTemplate.xlsx")),ZipArchiveMode.Read);
        Assert.Equal(source.Entries.Select(e=>e.FullName),template.Entries.Select(e=>e.FullName));
        foreach(var entry in source.Entries.Where(e=>e.FullName!="xl/worksheets/sheet1.xml")) {
            using var expected=entry.Open();using var actual=template.GetEntry(entry.FullName)!.Open();
            using var a=new MemoryStream();using var b=new MemoryStream();expected.CopyTo(a);actual.CopyTo(b);
            Assert.Equal(a.ToArray(),b.ToArray());
        }
        XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var src=source.GetEntry("xl/worksheets/sheet1.xml")!.Open();using var dest=template.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var before=XDocument.Load(src);var after=XDocument.Load(dest);
        var cells=after.Descendants(ns+"c").ToDictionary(c=>(string)c.Attribute("r")!);
        foreach(var cell in before.Descendants(ns+"c")) {
            var copy=cells[(string)cell.Attribute("r")!];
            Assert.Equal((string?)cell.Attribute("s"),(string?)copy.Attribute("s"));
            Assert.Equal(cell.Element(ns+"f")?.ToString(),copy.Element(ns+"f")?.ToString());
        }
        Assert.Equal(before.Root!.Element(ns+"mergeCells")!.ToString(),after.Root!.Element(ns+"mergeCells")!.ToString());
    }
    private static MemoryStream Template(params (string Address, string Value)[] values)
    {
        var result = new MemoryStream();
        result.Write(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"TestData/CostSheetTemplate.xlsx")));
        result.Position=0;
        using(var zip=new ZipArchive(result,ZipArchiveMode.Update,true)) {
            var entry=zip.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument xml; using(var stream=entry.Open()) xml=XDocument.Load(stream);
            XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach(var (address,value) in values) {
                var cell=xml.Descendants(ns+"c").SingleOrDefault(c=>(string?)c.Attribute("r")==address);
                if(cell==null){cell=new XElement(ns+"c",new XAttribute("r",address));xml.Descendants(ns+"row").First().Add(cell);}
                cell.RemoveNodes();cell.SetAttributeValue("t","inlineStr");cell.Add(new XElement(ns+"is",new XElement(ns+"t",value)));
            }
            entry.Delete();using var output=zip.CreateEntry("xl/worksheets/sheet1.xml").Open();xml.Save(output);
        }
        result.Position=0;return result;
    }
    [Fact]
    public void BlankDownloadTemplate_RequiresUserInputs()
    {
        using var file=Template();
        var error=Assert.Throws<InvalidOperationException>(()=>CostSheetExcelImport.Read(file));
        Assert.Contains("R18",error.Message);
    }
    [Theory]
    [InlineData(3)] [InlineData(5)]
    public void CompletedDownloadTemplate_ImportsLayersAndBlankOptionalCharges(int layers)
    {
        var values=new List<(string,string)> {("R18",layers.ToString()),("G9","QA Template Box"),("I3","46307"),
            ("E20","416"),("G20","312"),("I20","111"),("K20","45")};
        for(int i=0;i<layers;i++) {var r=20+i*2;values.Add(("T"+r,"KLB"));values.Add(("AB"+r,"110"));values.Add(("AD"+r,"140"));}
        using var file=Template(values.ToArray());var m=CostSheetExcelImport.Read(file).Data;
        Assert.Equal(layers,m.Plies.Count);Assert.Equal("QA Template Box",m.ItemName);
        Assert.Equal(0,m.LabourRate);Assert.Equal(1,m.ClientCosting!.PrintingRate);Assert.Equal(1.45m,m.ClientCosting.FluteFactor);
        Assert.Equal(.63m,m.ClientCosting.WidthAllowanceIn);Assert.Equal(1,m.MOQ);Assert.True(m.FinalRateWithGST>0);
        Assert.All(m.Plies,p=>Assert.Null(p.ItemId));
    }
    [Fact]
    public void CompletedDownloadTemplate_MatchesLatestClientCost()
    {
        var values=new List<(string,string)> {("R18","5"),("G9","QA Template Box"),("I3","46307"),
            ("E20","300"),("G20","300"),("I20","380"),("K20","45"),("AB5","Offset"),("AB7","0"),
            ("R13","1"),("AF14","1"),("T31","0"),("T32","1"),("T33","1"),("T34","0"),
            ("AF39","10"),("AF41","5"),
            ("AF43","0"),("AF45","4.5"),("AF47","5.25"),("AF49","5.25"),("AF51","2.5"),
            ("AF54","2.5"),("AF57","2.25"),("AF59","2.25"),("AF61","2.75"),("G49","4.75"),("G47","0")};
        for(int i=0;i<3;i++){var r=20+i*2;values.Add(("T"+r,"KLB"));values.Add(("AB"+r,"110"));values.Add(("AD"+r,i==1?"110":"140"));}
        using var file=Template(values.ToArray());Assert.Equal(61.27m,CostSheetExcelImport.Read(file).Data.FinalRateWithGST);
    }
    private static byte[] Sample() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"TestData/ClientCostingSample.xlsx"));
    private static MemoryStream Modified(string address, string? value = null, string? formula = null, string? type = null)
    {
        var result = new MemoryStream(); result.Write(Sample()); result.Position = 0;
        using (var zip = new ZipArchive(result, ZipArchiveMode.Update, true)) {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument xml; using (var stream = entry.Open()) xml = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var cell = xml.Descendants(ns+"c").SingleOrDefault(c => (string?)c.Attribute("r") == address);
            if (cell == null) { cell = new XElement(ns+"c",new XAttribute("r",address)); xml.Descendants(ns+"row").First().Add(cell); }
            if (value != null) { cell.Element(ns+"v")?.Remove(); cell.Element(ns+"is")?.Remove(); cell.Add(new XElement(ns+"v",value)); }
            if (formula != null) { cell.Element(ns+"f")?.Remove(); cell.Add(new XElement(ns+"f",formula)); }
            if (type != null) cell.SetAttributeValue("t",type);
            entry.Delete(); using var output = zip.CreateEntry("xl/worksheets/sheet1.xml").Open(); xml.Save(output);
        }
        result.Position=0; return result;
    }
    [Fact]
    public void ActualSample_ImportsFiveLayersAndRecalculatesConfirmedRules()
    {
        using var input = new MemoryStream(Sample()); var preview=CostSheetExcelImport.Read(input); var m=preview.Data;
        Assert.Equal("Wakol Loba(12 x 0.5) Ltr",m.ItemName); Assert.Equal(new DateTime(2026,10,12),m.SheetDate);
        Assert.Equal(416,m.Length); Assert.Equal(312,m.Width); Assert.Equal(111,m.Height); Assert.Equal(45,m.Flap);
        Assert.Equal(5,m.Plies.Count); Assert.Equal(new[]{false,true,false,true,false},m.ClientCosting!.FlutedLayers);
        Assert.Equal(new[]{140m,110m,110m,110m,140m},m.Plies.Select(p=>p.Rate!.Value));
        Assert.All(m.Plies,p=>{Assert.Equal(110,p.GSM);Assert.Null(p.ItemId);});
        Assert.Equal(50,m.GlueRate); Assert.Equal(640,m.ClientCosting.GlueLaminationRate); Assert.Equal(1.25m,m.ClientCosting.PinRate);
        Assert.False(m.HasLamination); Assert.Equal(0,m.LaminationCost); Assert.Equal(99.77m,m.FinalRateWithGST);
        Assert.Equal("Draft",m.Status); Assert.Null(m.FinishedItemId); Assert.Null(m.CustomerId); Assert.Equal(0,m.CostSheetId);
        Assert.Contains("Shahzad Designer",m.Remarks); Assert.NotEmpty(preview.Warnings);
    }
    [Fact]
    public void LaminationYes_ImportsAndRecalculatesWithoutStockIds()
    {
        using var input=Modified("AF16","Yes",type:"str"); var m=CostSheetExcelImport.Read(input).Data;
        Assert.True(m.HasLamination); Assert.Equal(110.38m,m.FinalRateWithGST); Assert.Equal(7.16m,m.LaminationCost);
        Assert.Null(m.ClientCosting!.GlueLaminationItemId);
    }
    [Theory]
    [InlineData("E20","-1")] [InlineData("AB20","0")] [InlineData("AB20","invalid")]
    [InlineData("R18","6")] [InlineData("R18","2.5")] [InlineData("K61","0")]
    [InlineData("I3","not a date")] [InlineData("AB7","1000")]
    public void InvalidRequiredInputs_AreRejected(string address,string value)
    { using var input=Modified(address,value,type:"n"); Assert.Throws<InvalidOperationException>(()=>CostSheetExcelImport.Read(input)); }
    [Fact]
    public void DifferentLayout_IsRejected()
    { using var input=Modified("C3","Other",type:"str"); Assert.Contains("doesn't match the required cost sheet format",Assert.Throws<InvalidOperationException>(()=>CostSheetExcelImport.Read(input)).Message); }
    [Fact]
    public void ChangedRateFormula_IsNotSilentlyIgnored()
    { using var input=Modified("Z32",formula:"K34*999"); Assert.Contains("Z32",Assert.Throws<InvalidOperationException>(()=>CostSheetExcelImport.Read(input)).Message); }
    [Fact]
    public void FormulaError_IsRejected()
    { using var input=Modified("E20","#REF!",type:"e"); Assert.Contains("E20",Assert.Throws<InvalidOperationException>(()=>CostSheetExcelImport.Read(input)).Message); }
    [Fact]
    public void CorruptFile_ReturnsReadableError()
    { using var input=new MemoryStream([1,2,3,4]); Assert.Contains(".xlsx",Assert.Throws<InvalidOperationException>(()=>CostSheetExcelImport.Read(input)).Message); }

    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w=>w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static CostSheetController Controller(ApplicationDbContext db, Permissions permissions) => new(db,permissions) { ControllerContext=new() { HttpContext=new DefaultHttpContext { User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"1")],"Test")) } } };
    private static IFormFile FileUpload(byte[]? bytes=null,string name="costing.xlsx") { var stream=new MemoryStream(bytes??Sample()); return new FormFile(stream,0,stream.Length,"file",name); }
    [Fact]
    public async Task UploadPreview_DoesNotSaveAndRequiresCreatePermission()
    {
        await using var db=Database(); var permissions=new Permissions(); var controller=Controller(db,permissions);
        Assert.IsType<OkObjectResult>(await controller.ImportExcel(FileUpload())); Assert.Equal("CostSheet.Create",permissions.LastCode);
        Assert.Empty(await db.CostSheet.ToListAsync()); Assert.Empty(await db.Stock.ToListAsync());
        permissions.Allowed=false; Assert.IsType<RedirectToActionResult>(await controller.ImportExcel(FileUpload()));
    }
    [Fact]
    public async Task ExistingSheetImport_RequiresEditPermissionAndKeepsStoredSheetUntouched()
    {
        await using var db=Database(); db.CostSheet.Add(new(){CostSheetId=9,ItemName="Original",Status="Final"}); await db.SaveChangesAsync();
        var permissions=new Permissions(); var controller=Controller(db,permissions);
        Assert.IsType<OkObjectResult>(await controller.ImportExcel(FileUpload(),9)); Assert.Equal("CostSheet.Edit",permissions.LastCode);
        Assert.Equal("Original",(await db.CostSheet.SingleAsync()).ItemName); Assert.Equal("Final",(await db.CostSheet.SingleAsync()).Status);
        Assert.IsType<NotFoundResult>(await controller.ImportExcel(FileUpload(),99));
    }
    [Theory]
    [InlineData("costing.xls")] [InlineData("costing.xlsm")] [InlineData("file.txt")]
    public async Task UnsupportedExtensions_AreRejected(string name)
    { await using var db=Database(); Assert.IsType<BadRequestObjectResult>(await Controller(db,new()).ImportExcel(FileUpload(name:name))); }
    [Fact]
    public async Task UploadLimitAndEmptyFile_AreRejected()
    {
        await using var db=Database(); var controller=Controller(db,new());
        Assert.IsType<BadRequestObjectResult>(await controller.ImportExcel(FileUpload([])));
        Assert.IsType<BadRequestObjectResult>(await controller.ImportExcel(FileUpload(new byte[CostSheetExcelImport.MaxFileBytes+1])));
    }
    [Fact]
    public async Task InventoryMatching_IsExactActiveUniqueAndNeverUsesExcelIds()
    {
        await using var db=Database(); db.ItemCategory.Add(new(){CategoryId=10,CategoryName="Paper",IsActive=true});
        db.Items.AddRange(new Items {ItemID=100,CategoryID=10,ItemName="KLB",ItemType="RawMaterial",StockUnit="kg",IsActive=true},
            new Items {ItemID=101,CategoryID=10,ItemName="SF",ItemType="RawMaterial",StockUnit="kg",IsActive=true},
            new Items {ItemID=102,CategoryID=10,ItemName="SF",ItemType="RawMaterial",StockUnit="kg",IsActive=true}); await db.SaveChangesAsync();
        var response=Assert.IsType<OkObjectResult>(await Controller(db,new()).ImportExcel(FileUpload()));
        var json=JsonSerializer.SerializeToElement(response.Value); var preview=json.GetProperty("preview").Deserialize<CostSheetImportPreview>()!;
        Assert.Equal(100,preview.Data.Plies[0].ItemId); Assert.Null(preview.Data.Plies[1].ItemId); Assert.Null(preview.Data.FinishedItemId);
        Assert.Empty(await db.CostSheet.ToListAsync());
    }
    [Fact]
    public async Task ImportedSheet_CanBeSavedReopenedEditedAndFinalizedAfterStockSelection()
    {
        await using var db=Database(); db.ItemCategory.Add(new(){CategoryId=10,CategoryName="Import QA",IsActive=true});
        for(int i=1;i<=3;i++) db.Items.Add(new(){ItemID=i,CategoryID=10,ItemName="Raw "+i,ItemType="RawMaterial",StockUnit="kg",IsActive=true});
        db.Items.Add(new(){ItemID=4,CategoryID=10,ItemName="Box",ItemType="FinishedGoods",StockUnit="pcs",IsActive=true}); await db.SaveChangesAsync();
        using var stream=new MemoryStream(Sample()); var m=CostSheetExcelImport.Read(stream).Data; var controller=Controller(db,new());
        Assert.IsType<OkObjectResult>(await controller.Save(m));
        m.CostSheetId=(await db.CostSheet.SingleAsync()).CostSheetId;
        Assert.Empty(await db.Stock.ToListAsync());
        foreach(var ply in m.Plies) { ply.ItemId=1; ply.PlyName="Raw 1"; }
        m.GlueItemId=2; m.SilicateItemId=3; m.FinishedItemId=4; m.BranchID=1;
        Assert.IsType<OkObjectResult>(await controller.Save(m)); var saved=await db.CostSheet.Include(x=>x.Plies).SingleAsync();
        Assert.Equal(99.77m,saved.FinalRateWithGST); Assert.Equal(5,saved.Plies!.Count); Assert.Equal("Draft",saved.Status);
        Assert.IsType<JsonResult>(await controller.GetById(saved.CostSheetId));
        m.CostSheetId=saved.CostSheetId; m.ProfitPct=10; m.Status="Final";
        Assert.IsType<OkObjectResult>(await controller.Save(m)); Assert.Single(await db.CostSheet.ToListAsync());
        Assert.Equal("Final",saved.Status); Assert.Equal(10,saved.ProfitPct); Assert.True(saved.FinalRateWithGST>99.77m);
        Assert.Empty(await db.Stock.ToListAsync());
    }

    private sealed class Permissions : IPermissionService
    {
        public bool Allowed=true; public string? LastCode;
        public Task<bool> HasPermissionAsync(int id,string code) { LastCode=code; return Task.FromResult(Allowed); }
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user,string code)=>HasPermissionAsync(1,code);
        public Task<List<string>> GetUserPermissionsAsync(int id)=>Task.FromResult(new List<string>());
        public Task<List<string>> GetAllPermissionCodesAsync()=>Task.FromResult(new List<string>());
        public Task<List<string>> GetRolePermissionsAsync(int id)=>Task.FromResult(new List<string>());
        public Task<Permission?> GetPermissionByCodeAsync(string code)=>Task.FromResult<Permission?>(null);
        public Task AssignPermissionToRoleAsync(int id,int permission)=>Task.CompletedTask;
        public Task RemovePermissionFromRoleAsync(int id,int permission)=>Task.CompletedTask;
    }
}
