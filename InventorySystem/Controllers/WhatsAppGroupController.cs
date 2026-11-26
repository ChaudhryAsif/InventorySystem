using System.Text.RegularExpressions;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers;

// The WhatsApp page: link the number, pick which group receives which document, and see what was sent.
// Every action needs the WhatsAppGroup.Manage permission (see OperationFilter).
[Authorize]   // the WhatsAppGroup.Manage permission decides who may use it (see OperationFilter)
public class WhatsAppGroupController(ApplicationDbContext db,IWhatsAppGroupClient wa) : Controller
{
    private static readonly Regex GroupIdPattern=new(@"^\d{5,}(-\d+)?@g\.us$",RegexOptions.Compiled);
    private static readonly Dictionary<string,string> Labels=new(){[WhatsAppDocumentTypes.GatePass]="Gate Pass (Inward and Outward)"};

    public IActionResult Index()=>View();

    private IActionResult StatusResult(WaStatus s)=>Ok(new{configured=wa.IsConfigured,reachable=s.Reachable,status=s.Status,number=s.Number,qr=s.Qr,pairingCode=s.PairingCode,error=s.Error});
    [HttpGet] public async Task<IActionResult> Status(CancellationToken ct)=>StatusResult(await wa.StatusAsync(ct));
    [HttpPost] public async Task<IActionResult> Connect(CancellationToken ct)=>StatusResult(await wa.ConnectAsync(ct));
    [HttpPost] public async Task<IActionResult> Disconnect(CancellationToken ct)=>StatusResult(await wa.DisconnectAsync(ct));

    [HttpGet]
    public async Task<IActionResult> Groups(CancellationToken ct)
    {
        var result=await wa.GroupsAsync(ct);
        return result.Ok?Ok(new{success=true,groups=result.Groups.Select(g=>new{id=g.Id,name=g.Name,members=g.Members})}):BadRequest(new{success=false,message=result.Error});
    }

    [HttpGet]
    public async Task<IActionResult> Destinations()
    {
        var saved=await db.WhatsAppDestinations.AsNoTracking().ToListAsync();
        return Ok(WhatsAppDocumentTypes.All.Select(type=>{var row=saved.FirstOrDefault(d=>d.DocumentType==type);return new{documentType=type,label=Labels.GetValueOrDefault(type,type),groupId=row?.GroupId??"",groupName=row?.GroupName??"",enabled=row?.Enabled??false};}));
    }

    // Saving a group is what turns sending on for that document; saving no group turns it off (the Enabled column just follows it).
    public sealed class DestinationRequest{public string? DocumentType{get;set;} public string? GroupId{get;set;} public string? GroupName{get;set;}}
    [HttpPost]
    public async Task<IActionResult> SaveDestination([FromBody] DestinationRequest? request)
    {
        if(request==null||request.DocumentType==null||!WhatsAppDocumentTypes.All.Contains(request.DocumentType))return BadRequest(new{success=false,message="Choose what kind of document this group is for."});
        var groupId=(request.GroupId??"").Trim();
        if(groupId.Length>0&&!GroupIdPattern.IsMatch(groupId))return BadRequest(new{success=false,message="That is not a WhatsApp group."});
        var name=(request.GroupName??"").Trim();
        var row=await db.WhatsAppDestinations.SingleOrDefaultAsync(d=>d.DocumentType==request.DocumentType);
        if(row==null){row=new(){DocumentType=request.DocumentType};db.WhatsAppDestinations.Add(row);}
        row.GroupId=groupId;row.GroupName=name[..Math.Min(name.Length,200)];row.Enabled=groupId.Length>0;row.UpdatedUtc=DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new{success=true,message="Saved."});
    }

    public sealed class TestRequest{public string? DocumentType{get;set;}}
    [HttpPost]
    public async Task<IActionResult> SendTest([FromBody] TestRequest? request,CancellationToken ct)
    {
        var type=request?.DocumentType;
        var row=type==null?null:await db.WhatsAppDestinations.AsNoTracking().SingleOrDefaultAsync(d=>d.DocumentType==type);
        if(row==null||row.GroupId.Length==0)return BadRequest(new{success=false,message="Choose and save a group first."});
        var result=await wa.SendTextAsync(row.GroupId,$"{GatePassMessage.CompanyName}: test message. WhatsApp sending is working.",ct);
        var error=result.Ok?null:(result.Error??"The message was not accepted.");
        db.WhatsAppSendLogs.Add(new(){DocumentType=row.DocumentType,DocumentNumber="Test message",GroupId=row.GroupId,Status=result.Ok?"Sent":"Failed",Error=error==null?null:error[..Math.Min(error.Length,500)],SentBy=User.Identity?.Name,CreatedUtc=DateTime.UtcNow});
        await db.SaveChangesAsync(CancellationToken.None);
        return result.Ok?Ok(new{success=true,message="Test message sent to "+(row.GroupName.Length>0?row.GroupName:"the group")+"."}):BadRequest(new{success=false,message=error});
    }

    [HttpGet]
    public async Task<IActionResult> Log()
    {
        var rows=await db.WhatsAppSendLogs.AsNoTracking().OrderByDescending(l=>l.Id).Take(20)
            .Select(l=>new{l.Id,l.DocumentType,l.DocumentNumber,l.Status,l.Error,l.SentBy,l.CreatedUtc}).ToListAsync();
        return Ok(rows);
    }
}
