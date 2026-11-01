using System.ComponentModel.DataAnnotations;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers;

[Authorize(Roles="Admin,SuperAdmin")]
public class GatePassController(ApplicationDbContext db,ILogger<GatePassController>? logger=null) : Controller
{
    private static bool DirectionValid(string? direction)=>direction is "Inward" or "Outward";
    public IActionResult Inward(){ViewBag.Direction="Inward";return View("Index");}
    public IActionResult Outward(){ViewBag.Direction="Outward";return View("Index");}
    [HttpGet]
    public IActionResult Create(string direction="Outward",int id=0)
    {
        if(!DirectionValid(direction)||id<0)return BadRequest();
        ViewBag.Direction=direction;ViewBag.PassId=id;return View();
    }
    [HttpGet]
    public async Task<IActionResult> GetList(string direction="Outward",string? search=null,DateTime? from=null,DateTime? to=null,bool cancelledOnly=false,int page=1,int pageSize=50,string? party=null,string? status=null)
    {
        if(!DirectionValid(direction))return BadRequest(new{success=false,message="Select Inward or Outward."});
        if(status is not (null or "active" or "cancelled" or "all")||from>to||to?.Year==9999)
            return BadRequest(new{success=false,message="Review the status and date range."});
        var query=db.GatePasses.AsNoTracking().Where(g=>g.Direction==direction);
        if(status!="all")query=query.Where(g=>g.IsCancelled==(status==null?cancelledOnly:status=="cancelled"));
        if(!string.IsNullOrWhiteSpace(party))query=query.Where(g=>g.PartyName==party.Trim());
        if(!string.IsNullOrWhiteSpace(search))
        {
            var term=search.Trim();
            var prefix=direction=="Inward"?"IGP-":"OGP-";
            var serial=term.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)?term[prefix.Length..]:term;
            var passId=int.TryParse(serial,out var parsed)?parsed:0;
            query=query.Where(g=>g.PartyName.Contains(term)||(g.VehicleNumber??"").Contains(term)||g.Id==passId);
        }
        if(from.HasValue)query=query.Where(g=>g.PassDate>=from.Value.Date);
        if(to.HasValue)query=query.Where(g=>g.PassDate<to.Value.Date.AddDays(1));
        pageSize=Math.Clamp(pageSize,1,200);page=Math.Clamp(page,1,int.MaxValue/pageSize);
        try
        {
            var totalCount=await query.CountAsync();
            var rows=await query.OrderByDescending(g=>g.Id).Skip((page-1)*pageSize).Take(pageSize).Select(g=>new{g.Id,g.Direction,g.PassDate,g.PartyName,g.VehicleNumber,g.IsCancelled,g.Revision,ItemCount=g.Lines.Count}).ToListAsync();
            return Json(new{success=true,data=rows,totalCount,page,pageSize});
        }
        catch(System.Data.Common.DbException ex)
        {
            logger?.LogError(ex,"Gate Pass list failed. Reference {Reference}",HttpContext.TraceIdentifier);
            var message=ex is Microsoft.Data.SqlClient.SqlException {Number:208}
                ? "Gate Pass database tables are unavailable. Ask your administrator to apply the Gate Pass database migration."
                : "Gate passes could not be loaded. Please retry. If this continues, ask your administrator to check the server logs.";
            return StatusCode(503,new{success=false,message,reference=HttpContext.TraceIdentifier});
        }
    }
    public sealed class LookupOption
    {
        public string Key{get;set;}="";
        public string Name{get;set;}="";
        public string Type{get;set;}="";
        public string? Phone{get;set;}
        public string? Address{get;set;}
        public string? Unit{get;set;}
        public string? Detail{get;set;}
        public int? GroupId{get;set;}
        public bool IsActive{get;set;}=true;
        public long? ItemId{get;set;}
        public decimal? PurchasePrice{get;set;}
        public decimal? SalePrice{get;set;}
        public string? PackingType{get;set;}
        public decimal? PackingQuantity{get;set;}
    }
    // Lightweight, read-only lookups use GatePass permissions and never create master records.
    [HttpGet]
    public async Task<IActionResult> GetPartyOptions(string? search=null,string type="all",int page=1)
    {
        if(type is not ("all" or "supplier" or "customer" or "RawMaterial" or "FinishedGoods"))return BadRequest();
        page=Math.Clamp(page,1,int.MaxValue/20);var skip=(page-1)*20;var term=search?.Trim()??"";
        var result=new List<LookupOption>();
        if(type is "all" or "supplier" or "customer")
        {
            var parties=db.Parties.AsNoTracking().Where(p=>p.Status=="active" && !(p.PartyCode??"").StartsWith("RAW-GROUP:") && !(p.PartyCode??"").StartsWith("FG-GROUP:"));
            if(type!="all")parties=parties.Where(p=>p.PartyType==type||p.PartyType=="both");
            if(term!="")parties=parties.Where(p=>p.PartyName.Contains(term)||(p.Phone??"").Contains(term)||(p.Mobile??"").Contains(term));
            var count=await parties.CountAsync();
            result.AddRange(await parties.OrderBy(p=>p.PartyName).ThenBy(p=>p.PartyId).Skip(skip).Take(21).Select(p=>new LookupOption{Key="party-"+p.PartyId,Name=p.PartyName,Type=p.PartyType,Phone=p.Mobile!=null&&p.Mobile!=""?p.Mobile:p.Phone,Address=p.Address,Detail="Party"}).ToListAsync());
            skip=Math.Max(0,skip-count);
        }
        if(type is "all" or "RawMaterial" or "FinishedGoods")
        {
            var groups=db.ItemCategory.AsNoTracking().Where(g=>g.IsActive!=false&&(g.GroupType=="RawMaterial"||g.GroupType=="FinishedGoods"));
            if(type!="all")groups=groups.Where(g=>g.GroupType==type);
            if(term!="")groups=groups.Where(g=>g.CategoryName.Contains(term)||(g.ContactNumber??"").Contains(term));
            if(result.Count<21)result.AddRange(await groups.OrderBy(g=>g.CategoryName).ThenBy(g=>g.CategoryId).Skip(skip).Take(21-result.Count).Select(g=>new LookupOption{Key="group-"+g.CategoryId,GroupId=g.CategoryId,Name=g.CategoryName,Type=g.GroupType!,Phone=g.ContactNumber,Address=g.Address,Detail="Group"}).ToListAsync());
        }
        return Json(new{success=true,data=result.Take(20),hasMore=result.Count>20,page});
    }
    [HttpGet]
    public async Task<IActionResult> GetGroupOptions(int? passId=null)
    {
        var savedGroupId=passId.HasValue?await db.GatePasses.Where(g=>g.Id==passId).Select(g=>g.GroupId).SingleOrDefaultAsync():null;
        var rows=await db.ItemCategory.AsNoTracking().Where(g=>(g.IsActive!=false||g.CategoryId==savedGroupId)&&(g.GroupType=="RawMaterial"||g.GroupType=="FinishedGoods"))
            .OrderBy(g=>g.CategoryName).ThenBy(g=>g.CategoryId).Select(g=>new LookupOption{Key="group-"+g.CategoryId,GroupId=g.CategoryId,Name=g.CategoryName,Type=g.GroupType!,Phone=g.ContactNumber,Address=g.Address,IsActive=g.IsActive!=false}).ToListAsync();
        return Json(new{success=true,data=rows});
    }
    [HttpGet]
    public async Task<IActionResult> GetItemOptions(string? search=null,string type="all",int page=1,int? groupId=null)
    {
        if(type is not ("all" or "RawMaterial" or "FinishedGoods"))return BadRequest();
        page=Math.Clamp(page,1,int.MaxValue/20);var term=search?.Trim()??"";
        var items=db.Items.AsNoTracking().Where(i=>i.IsActive==true&&i.Category.IsActive!=false&&(i.StockUnit=="kg"||i.StockUnit=="pcs"||i.StockUnit=="in"));
        if(groupId.HasValue)
        {
            if(groupId<=0||!await db.ItemCategory.AnyAsync(g=>g.CategoryId==groupId&&g.IsActive!=false&&(g.GroupType=="RawMaterial"||g.GroupType=="FinishedGoods")))return BadRequest(new{success=false,message="Select an active item group."});
            items=items.Where(i=>i.CategoryID==groupId);
        }
        if(type!="all")items=items.Where(i=>i.ItemType==type);
        if(term!="")items=items.Where(i=>(i.ItemName??"").Contains(term)||(i.Barcode??"").Contains(term)||i.Category.CategoryName.Contains(term));
        var rows=await items.OrderBy(i=>i.ItemName).ThenBy(i=>i.ItemID).Skip((page-1)*20).Take(21).Select(i=>new LookupOption{Key="item-"+i.ItemID,GroupId=i.CategoryID,ItemId=i.ItemID,Name=i.ItemName??"",Type=i.ItemType,Unit=i.StockUnit,Detail=i.Category.CategoryName,PackingType=i.Packing,SalePrice=i.SalePrice,PurchasePrice=db.PurchaseInvoiceBody.Where(b=>b.Itemid==i.ItemID&&db.PurchaseInvoice.Any(p=>p.PurchaseId==b.PurchaseId)).OrderByDescending(b=>b.Srno).Select(b=>b.PurPrice).FirstOrDefault()}).ToListAsync();
        foreach(var row in rows)
        {
            var savedPacking=row.PackingType;
            var ply=System.Text.RegularExpressions.Regex.Match(savedPacking??row.Name,@"\b(3|5)\s*-?\s*ply\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if(!ply.Success)ply=System.Text.RegularExpressions.Regex.Match(row.Name,@"\b(3|5)\s*-?\s*ply\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            row.PackingType=ply.Success?ply.Groups[1].Value+"-Ply":null;
            if(decimal.TryParse(savedPacking,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var qty)&&qty is 10 or 20 or 30 or 50 or 100)row.PackingQuantity=qty;
        }
        return Json(new{success=true,data=rows.Take(20),hasMore=rows.Count>20,page});
    }
    [HttpGet]
    public async Task<IActionResult> GetPassPartyOptions(string direction="Outward",string? search=null,int page=1)
    {
        if(!DirectionValid(direction))return BadRequest();
        page=Math.Clamp(page,1,int.MaxValue/20);var term=search?.Trim()??"";
        var names=db.GatePasses.AsNoTracking().Where(g=>g.Direction==direction).Select(g=>g.PartyName).Distinct();
        if(term!="")names=names.Where(n=>n.Contains(term));
        var rows=await names.OrderBy(n=>n).Skip((page-1)*20).Take(21).ToListAsync();
        return Json(new{success=true,data=rows.Take(20).Select(n=>new LookupOption{Key=n,Name=n,Type="Saved party"}),hasMore=rows.Count>20,page});
    }
    [HttpGet]
    public async Task<IActionResult> GetSuggestions()
    {
        var recent=await db.GatePasses.AsNoTracking().Where(g=>!g.IsCancelled).OrderByDescending(g=>g.Id).Take(200)
            .Select(g=>new{g.VehicleName,g.VehicleNumber,g.TransportCompany,g.DriverName,g.DriverPhone,g.GateKeeper,g.ReceivedBy}).ToListAsync();
        string[] Values(IEnumerable<string?> values)=>values.Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>s!).Distinct().Take(30).ToArray();
        return Json(new{success=true,vehicleName=Values(recent.Select(g=>g.VehicleName)),vehicleNumber=Values(recent.Select(g=>g.VehicleNumber)),transportCompany=Values(recent.Select(g=>g.TransportCompany)),driverName=Values(recent.Select(g=>g.DriverName)),driverPhone=Values(recent.Select(g=>g.DriverPhone)),gateKeeper=Values(recent.Select(g=>g.GateKeeper)),receivedBy=Values(recent.Select(g=>g.ReceivedBy))});
    }
    [HttpGet]
    public async Task<IActionResult> GetById(int id)
    {
        var pass=await db.GatePasses.AsNoTracking().Include(g=>g.Lines.OrderBy(l=>l.SerialNo)).SingleOrDefaultAsync(g=>g.Id==id);
        if(pass==null)return NotFound();
        var ids=pass.Lines.Where(l=>l.ItemId.HasValue).Select(l=>l.ItemId!.Value).Distinct().ToList();
        var groups=await db.Items.AsNoTracking().Where(i=>ids.Contains(i.ItemID)).ToDictionaryAsync(i=>i.ItemID,i=>i.CategoryID);
        foreach(var line in pass.Lines)line.ItemGroupId=line.ItemId.HasValue&&groups.TryGetValue(line.ItemId.Value,out var group)?group:null;
        return Json(pass);
    }
    [HttpGet]
    public async Task<IActionResult> Print(int id)
    {
        var pass=await db.GatePasses.AsNoTracking().Include(g=>g.Lines.OrderBy(l=>l.SerialNo)).SingleOrDefaultAsync(g=>g.Id==id);
        return pass==null?NotFound():View(pass);
    }
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] GatePass? model)
    {
        if(model==null||!ModelState.IsValid||!Validator.TryValidateObject(model,new ValidationContext(model),new List<ValidationResult>(),true)||!DirectionValid(model.Direction)||model.Id<0||string.IsNullOrWhiteSpace(model.PartyName)||model.PassDate.Year<2000||model.BranchId<1||db.RestrictBranches&&!db.AllowedBranchIds.Contains(model.BranchId))
            return BadRequest(new{success=false,message="Enter a party, date, direction and authorized branch."});
        if(model.Id==0)
        {
            model.DepartureAt??=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time"));
            if(string.IsNullOrWhiteSpace(model.IssuedBy))model.IssuedBy="M. Nadeem";
            if(string.IsNullOrWhiteSpace(model.GateKeeper))model.GateKeeper="M. Yameen";
            if(string.IsNullOrWhiteSpace(model.ReceivedBy))model.ReceivedBy="M. Sami Ullah";
        }
        if(string.IsNullOrWhiteSpace(model.DriverSignature))model.DriverSignature=model.DriverName;
        if(model.Lines!=null)
        {
            var linked=model.Lines.Where(l=>l.ItemId.HasValue).ToList();
            var ids=linked.Select(l=>l.ItemId!.Value).Distinct().ToList();
            var items=await db.Items.AsNoTracking().Where(i=>ids.Contains(i.ItemID)).Select(i=>new{i.ItemID,i.ItemName,i.StockUnit,i.IsActive,GroupActive=i.Category.IsActive,i.CategoryID}).ToDictionaryAsync(i=>i.ItemID);
            var original=model.Id>0?await db.GatePassLines.AsNoTracking().Where(l=>l.GatePassId==model.Id&&db.GatePasses.Any(g=>g.Id==l.GatePassId)).ToListAsync():new List<GatePassLine>();
            var savedGroupId=model.Id>0?await db.GatePasses.AsNoTracking().Where(g=>g.Id==model.Id).Select(g=>g.GroupId).SingleOrDefaultAsync():null;
            if(savedGroupId.HasValue&&!model.GroupId.HasValue)return BadRequest(new{success=false,message="Select an item group. A saved group cannot be cleared."});
            if(model.GroupId.HasValue)
            {
                var group=await db.ItemCategory.AsNoTracking().SingleOrDefaultAsync(g=>g.CategoryId==model.GroupId&&(g.GroupType=="RawMaterial"||g.GroupType=="FinishedGoods"));
                if(model.GroupId<=0||group==null)return BadRequest(new{success=false,message="Select a valid item group."});
                if(model.Lines.Any(l=>!l.ItemId.HasValue)||linked.Any(l=>!items.TryGetValue(l.ItemId!.Value,out var item)||item.CategoryID!=model.GroupId))return BadRequest(new{success=false,message="Select every item from the selected group. Items from other groups or manual descriptions cannot be saved in a grouped gate pass."});
                if(group.IsActive==false&&(savedGroupId!=model.GroupId||linked.Any(l=>!original.Any(p=>p.Id==l.Id&&p.ItemId==l.ItemId))))return BadRequest(new{success=false,message="This group is inactive. Choose an active group before adding or changing items."});
            }
            foreach(var line in linked)
            {
                var prior=original.FirstOrDefault(l=>l.Id==line.Id&&l.ItemId==line.ItemId);
                if(prior!=null)
                {
                    if(line.Unit!=prior.Unit)return BadRequest(new{success=false,message="A saved item's unit cannot be changed. Select the correct inventory item."});
                }
                else
                {
                    if(!items.TryGetValue(line.ItemId!.Value,out var item)||item.IsActive!=true||item.GroupActive==false||item.StockUnit is not ("kg" or "pcs" or "in"))
                        return BadRequest(new{success=false,message="Select an active inventory item with a KG or PCS unit."});
                    if(line.Unit!=item.StockUnit)return BadRequest(new{success=false,message="The item's unit changed. Select it again to import its saved unit."});
                    line.Description=item.ItemName??line.Description;
                }
            }
            if(model.Lines.Any(l=>l.UnitPrice<0||l.UnitPrice>100000000))return BadRequest(new{success=false,message="Enter a unit price between 0 and 100,000,000, or leave it blank when unavailable."});
        }
        if(model.Lines!=null)foreach(var line in model.Lines.Where(l=>l.Unit=="pcs"&&(l.CalculateFromBundles||model.Direction=="Outward"&&l.OpenCartonQuantity>0)))
        {
            if(line.PackingQuantity<0||line.PackingQuantity>100000000||line.BundleQuantity<0||line.BundleQuantity>100000000||line.BundleQuantity>0&&line.PackingQuantity==0||line.OpenCartonQuantity<0||line.OpenCartonQuantity>100000000||decimal.Truncate(line.PackingQuantity)!=line.PackingQuantity||decimal.Truncate(line.BundleQuantity)!=line.BundleQuantity||decimal.Truncate(line.OpenCartonQuantity)!=line.OpenCartonQuantity)
                return BadRequest(new{success=false,message="Enter whole, non-negative carton and bundle quantities."});
            line.CalculateFromBundles=true;line.TotalQuantity=line.PackingQuantity*line.BundleQuantity+line.OpenCartonQuantity;
        }
        if(model.Lines==null||model.Lines.Count is <1 or >100||model.Lines.Any(l=>!Validator.TryValidateObject(l,new ValidationContext(l),new List<ValidationResult>(),true)||string.IsNullOrWhiteSpace(l.Description)||l.Description.Length>300||l.TotalQuantity<=0||l.TotalQuantity>100000000||l.PackingQuantity<0||l.PackingQuantity>100000000||l.BundleQuantity<0||l.BundleQuantity>100000000||l.OpenCartonQuantity<0||l.OpenCartonQuantity>100000000||l.Unit!="pcs"&&(l.CalculateFromBundles||l.OpenCartonQuantity!=0)||l.Unit is not ("kg" or "pcs" or "in")||l.PackingType is not (null or "" or "3-Ply" or "5-Ply")))
            return BadRequest(new{success=false,message="Enter an item description and a positive quantity for each line; use KG or PCS and 3-Ply/5-Ply packing."});
        if(model.ArrivalAt.HasValue&&model.DepartureAt<model.ArrivalAt||model.GrossWeightKg<0||model.GrossWeightKg>100000000||model.TareWeightKg<0||model.TareWeightKg>100000000||model.GrossWeightKg.HasValue&&model.TareWeightKg>model.GrossWeightKg)
            return BadRequest(new{success=false,message="Review arrival/departure times and gross/tare weights."});
        await using var tx=await db.Database.BeginAtomicTransactionAsync();
        try
        {
            var pass=model.Id==0?new GatePass{CreatedUtc=DateTime.UtcNow,CreatedBy=User.Identity?.Name}:await db.GatePasses.Include(g=>g.Lines).SingleOrDefaultAsync(g=>g.Id==model.Id);
            if(pass==null)return NotFound();
            if(pass.IsCancelled)return BadRequest(new{success=false,message="Cancelled gate passes cannot be edited."});
            if(model.ArrivalAt.HasValue&&(model.DepartureAt??pass.DepartureAt)<model.ArrivalAt)return BadRequest(new{success=false,message="Departure must be on or after arrival."});
            if(model.Id>0&&(pass.Direction!=model.Direction||pass.Revision!=model.Revision))return Conflict(new{success=false,message="This gate pass changed. Reload it before saving."});
            if(model.Id>0){db.GatePassLines.RemoveRange(pass.Lines);pass.Lines.Clear();pass.Revision++;pass.UpdatedUtc=DateTime.UtcNow;}
            pass.GroupId=model.GroupId;pass.Direction=model.Direction;pass.PassDate=model.PassDate.Date;pass.BranchId=model.BranchId;pass.PartyName=model.PartyName.Trim();pass.PartyAddress=model.PartyAddress;pass.PartyPhone=model.PartyPhone;
            pass.VehicleName=model.VehicleName;pass.VehicleNumber=model.VehicleNumber;pass.TransportCompany=model.TransportCompany;pass.BiltyNumber=model.BiltyNumber;pass.DriverName=model.DriverName;pass.DriverPhone=model.DriverPhone;
            pass.ArrivalAt=model.ArrivalAt;pass.DepartureAt=model.DepartureAt??pass.DepartureAt;pass.GrossWeightKg=model.GrossWeightKg;pass.TareWeightKg=model.TareWeightKg;
            pass.NetWeightKg=model.GrossWeightKg.HasValue&&model.TareWeightKg.HasValue?model.GrossWeightKg-model.TareWeightKg:null;
            pass.IssuedBy=model.IssuedBy;pass.DriverSignature=model.DriverSignature;pass.GateKeeper=model.GateKeeper;pass.ReceivedBy=model.ReceivedBy;pass.ReceiverReference=model.ReceiverReference;pass.Remarks=model.Remarks;
            pass.Lines=model.Lines.Select((l,i)=>new GatePassLine{SerialNo=i+1,ItemId=l.ItemId,UnitPrice=l.UnitPrice,Description=l.Description.Trim(),PackingType=l.PackingType,PackingQuantity=l.PackingQuantity,BundleQuantity=l.BundleQuantity,OpenCartonQuantity=l.OpenCartonQuantity,CalculateFromBundles=l.CalculateFromBundles,TotalQuantity=l.TotalQuantity,Unit=l.Unit}).ToList();
            if(model.Id==0)db.GatePasses.Add(pass);
            await db.SaveChangesAsync();await tx.CommitAsync();
            return Ok(new{success=true,id=pass.Id,revision=pass.Revision,passNumber=pass.PassNumber,message="Gate pass saved successfully."});
        }
        catch(DbUpdateConcurrencyException){await tx.RollbackAsync();return Conflict(new{success=false,message="This gate pass changed. Reload it before saving."});}
    }
    [HttpPost]
    public Task<IActionResult> Delete([FromBody] CancelRequest? request) => Cancel(request);

    public sealed class CancelRequest {public int Id{get;set;}public int Revision{get;set;}public string? Reason{get;set;}}
    [HttpPost]
    public async Task<IActionResult> Cancel([FromBody] CancelRequest? request)
    {
        if(request==null||request.Id<1||request.Reason?.Trim().Length is not (>=3 and <=500))return BadRequest(new{success=false,message="Enter a cancellation reason (3–500 characters)."});
        var pass=await db.GatePasses.SingleOrDefaultAsync(g=>g.Id==request.Id);if(pass==null)return NotFound();
        if(pass.IsCancelled||pass.Revision!=request.Revision)return Conflict(new{success=false,message="This gate pass changed. Reload the list."});
        pass.IsCancelled=true;pass.CancellationReason=request.Reason.Trim();pass.CancelledUtc=DateTime.UtcNow;pass.Revision++;
        try{await db.SaveChangesAsync();}catch(DbUpdateConcurrencyException){return Conflict(new{success=false,message="This gate pass changed. Reload the list."});}
        return Ok(new{success=true,message="Gate pass cancelled."});
    }
}
