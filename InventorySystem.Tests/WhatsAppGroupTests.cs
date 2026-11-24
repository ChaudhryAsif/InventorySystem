using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using InventorySystem.Authorization;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
namespace InventorySystem.Tests;

public class WhatsAppGroupTests
{
    private const string Group = "120363000000000001@g.us";
    private static ApplicationDbContext Db()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.Database.EnsureCreated();
        return db;
    }
    private static HttpContext Http() => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "QA") }, "Test")) };
    private static GatePassController Gate(ApplicationDbContext db) => new(db) { ControllerContext = new() { HttpContext = Http() } };
    private static WhatsAppGroupController Page(ApplicationDbContext db, FakeWa wa) => new(db, wa) { ControllerContext = new() { HttpContext = Http() } };
    private static JsonElement Data(IActionResult result) => JsonSerializer.SerializeToElement(result switch { ObjectResult o => o.Value, JsonResult j => j.Value, _ => null });
    private static GatePassController.SendToGroupRequest Request(int id, bool resend = false, string? pdf = null) => new() { Id = id, Resend = resend, PdfBase64 = pdf ?? Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4 " + new string('x', 200))) };
    private static async Task<GatePass> Pass(ApplicationDbContext db, int revision = 1, bool cancelled = false)
    {
        var pass = new GatePass { Direction = "Outward", PassDate = new DateTime(2026, 11, 24), BranchId = 1, PartyName = "Star Packaging", VehicleNumber = "KHI-1234", DriverName = "Imran", Revision = revision, IsCancelled = cancelled, CreatedUtc = DateTime.UtcNow };
        pass.Lines.Add(new GatePassLine { SerialNo = 1, Description = "3-Ply Carton", TotalQuantity = 100, Unit = "pcs" });
        db.GatePasses.Add(pass); await db.SaveChangesAsync(); return pass;
    }
    private static async Task Destination(ApplicationDbContext db, bool enabled = true, string groupId = Group)
    {
        db.WhatsAppDestinations.Add(new() { DocumentType = WhatsAppDocumentTypes.GatePass, GroupId = groupId, GroupName = "Gate Pass Group", Enabled = enabled }); await db.SaveChangesAsync();
    }

    private sealed class FakeWa : IWhatsAppGroupClient
    {
        public bool IsConfigured { get; set; } = true;
        public WaStatus Status = new(true, "connected", "923001234567", null, null, null);
        public WaGroups Groups = new(true, [new("120363000000000001@g.us", "Gate Pass Group", 5)], null);
        public Queue<WaSendResult> Results = new();
        public List<(string Group, string File, string Caption, byte[] Pdf, string RequestId)> Pdfs = [];
        public List<(string Group, string Text)> Texts = [];
        public int Connects, Disconnects;
        public Task<WaStatus> StatusAsync(CancellationToken ct = default) => Task.FromResult(Status);
        public Task<WaStatus> ConnectAsync(CancellationToken ct = default) { Connects++; return Task.FromResult(Status); }
        public Task<WaStatus> DisconnectAsync(CancellationToken ct = default) { Disconnects++; return Task.FromResult(Status); }
        public Task<WaGroups> GroupsAsync(CancellationToken ct = default) => Task.FromResult(Groups);
        public Task<WaSendResult> SendPdfAsync(string groupId, string fileName, string caption, byte[] pdf, string requestId, CancellationToken ct = default)
        { Pdfs.Add((groupId, fileName, caption, pdf, requestId)); return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : new WaSendResult(true, false, "m1", null)); }
        public Task<WaSendResult> SendTextAsync(string groupId, string text, CancellationToken ct = default)
        { Texts.Add((groupId, text)); return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : new WaSendResult(true, false, "t1", null)); }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests = []; public List<string> Bodies = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Requests.Add(request); Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct)); return respond(request); }
    }
    private static HttpResponseMessage Json(HttpStatusCode code, string json) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static WhatsAppGroupClient Client(StubHandler handler, string url = "http://wa.test", string key = "secret") => new(new HttpClient(handler), new WhatsAppGroupOptions { BaseUrl = url, ApiKey = key });

    // ---- the client that talks to the WhatsApp service ----
    [Fact] public async Task Client_WithoutSettings_FailsEveryCallWithoutTouchingTheNetwork()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}")); var client = Client(handler, "", "");
        Assert.False(client.IsConfigured);
        var status = await client.StatusAsync(); Assert.False(status.Reachable); Assert.Contains("not set up", status.Error);
        Assert.False((await client.SendPdfAsync(Group, "a.pdf", "c", [1], "r")).Ok); Assert.False((await client.GroupsAsync()).Ok);
        Assert.Empty(handler.Requests);
    }
    [Fact] public async Task Client_SendsTheKey_AndReadsTheStatus()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"ok":true,"status":"qr","qr":"data:image/png;base64,AAA","number":null}""")); var client = Client(handler, "http://wa.test/");
        var status = await client.StatusAsync();
        Assert.True(status.Reachable); Assert.Equal("qr", status.Status); Assert.Equal("data:image/png;base64,AAA", status.Qr);
        Assert.Equal("http://wa.test/status", handler.Requests[0].RequestUri!.ToString()); Assert.Equal("secret", handler.Requests[0].Headers.GetValues("x-api-key").Single());
    }
    [Fact] public async Task Client_ExplainsAWrongKey_AndAnUnreachableService()
    {
        var wrong = await Client(new StubHandler(_ => Json(HttpStatusCode.Unauthorized, """{"error":"Unauthorized"}"""))).StatusAsync();
        Assert.False(wrong.Reachable); Assert.Contains("key is wrong", wrong.Error);
        var down = await Client(new StubHandler(_ => throw new HttpRequestException("boom"))).SendPdfAsync(Group, "a.pdf", "c", [1], "r");
        Assert.False(down.Ok); Assert.Contains("could not be reached", down.Error);
        var refused = await Client(new StubHandler(_ => Json(HttpStatusCode.BadRequest, """{"ok":false,"error":"WhatsApp number is not connected"}"""))).SendPdfAsync(Group, "a.pdf", "c", [1], "r");
        Assert.Equal("WhatsApp number is not connected", refused.Error);
    }
    [Fact] public async Task Client_SendsThePdfWithItsRequestId_AndReportsADuplicate()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"ok":true,"duplicate":true}""")); var client = Client(handler);
        var result = await client.SendPdfAsync(Group, "OGP-000001.pdf", "caption text", Encoding.ASCII.GetBytes("%PDF-abc"), "gatepass-1-r1");
        Assert.True(result.Ok); Assert.True(result.Duplicate);
        var body = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        Assert.Equal("gatepass-1-r1", body.GetProperty("requestId").GetString()); Assert.Equal(Group, body.GetProperty("groupId").GetString()); Assert.Equal("OGP-000001.pdf", body.GetProperty("fileName").GetString());
        Assert.Equal("caption text", body.GetProperty("caption").GetString()); Assert.Equal("%PDF-abc", Encoding.ASCII.GetString(Convert.FromBase64String(body.GetProperty("base64").GetString()!)));
    }
    [Fact] public async Task Client_ListsGroupsByName()
    {
        var client = Client(new StubHandler(_ => Json(HttpStatusCode.OK, """{"ok":true,"groups":[{"id":"2@g.us","name":"Zeta","members":3},{"id":"1@g.us","name":"alpha","members":9}]}""")));
        var groups = await client.GroupsAsync(); Assert.True(groups.Ok); Assert.Equal(["alpha", "Zeta"], groups.Groups.Select(g => g.Name)); Assert.Equal(9, groups.Groups[0].Members);
    }

    // ---- sending a gate pass to the group ----
    [Fact] public async Task SendToGroup_WithNoGroupSaved_SendsNothing_AndLogsNothing()
    {
        using var db = Db(); var pass = await Pass(db); await Destination(db, groupId: ""); var wa = new FakeWa();
        var data = Data(await Gate(db).SendToGroup(Request(pass.Id), wa));
        Assert.True(data.GetProperty("skipped").GetBoolean()); Assert.Contains("No WhatsApp group", data.GetProperty("message").GetString()); Assert.Empty(wa.Pdfs);
        Assert.Equal(0, await db.WhatsAppSendLogs.CountAsync());
    }
    [Fact] public async Task SendToGroup_OnlyNeedsAGroup_NotTheOldOnSwitch()
    {
        using var db = Db(); var pass = await Pass(db); await Destination(db, enabled: false); var wa = new FakeWa();
        Assert.True(Data(await Gate(db).SendToGroup(Request(pass.Id), wa)).GetProperty("success").GetBoolean()); Assert.Single(wa.Pdfs);
    }
    [Fact] public async Task SendToGroup_WithoutAnyDestination_IsSkipped()
    {
        using var db = Db(); var pass = await Pass(db); var wa = new FakeWa();
        Assert.True(Data(await Gate(db).SendToGroup(Request(pass.Id), wa)).GetProperty("skipped").GetBoolean()); Assert.Empty(wa.Pdfs);
    }
    [Fact] public async Task SendToGroup_SendsThePdfWithTheCaption_AndLogsIt()
    {
        using var db = Db(); var pass = await Pass(db); await Destination(db); var wa = new FakeWa();
        var data = Data(await Gate(db).SendToGroup(Request(pass.Id), wa));
        Assert.True(data.GetProperty("success").GetBoolean());
        var sent = Assert.Single(wa.Pdfs);
        Assert.Equal(Group, sent.Group); Assert.Equal(pass.PassNumber + ".pdf", sent.File); Assert.Equal($"gatepass-{pass.Id}-r1", sent.RequestId);
        Assert.Contains(pass.PassNumber, sent.Caption); Assert.Contains("Star Packaging", sent.Caption); Assert.Contains("KHI-1234", sent.Caption); Assert.Contains("3-Ply Carton: 100 PCS", sent.Caption);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(sent.Pdf, 0, 4));
        var log = await db.WhatsAppSendLogs.SingleAsync();
        Assert.Equal("Sent", log.Status); Assert.Equal(pass.Id, log.DocumentId); Assert.Equal(pass.PassNumber, log.DocumentNumber); Assert.Equal("QA", log.SentBy); Assert.Null(log.Error);
    }
    [Fact] public async Task SendToGroup_NeverSendsTheSamePassTwice_UnlessAskedToSendAgain()
    {
        using var db = Db(); var pass = await Pass(db); await Destination(db); var wa = new FakeWa(); var c = Gate(db);
        await c.SendToGroup(Request(pass.Id), wa);
        var again = Assert.IsType<ConflictObjectResult>(await c.SendToGroup(Request(pass.Id), wa));
        Assert.True(Data(again).GetProperty("alreadySent").GetBoolean()); Assert.Single(wa.Pdfs);
        Assert.IsType<OkObjectResult>(await c.SendToGroup(Request(pass.Id, resend: true), wa));
        Assert.Equal(2, wa.Pdfs.Count); Assert.Equal($"gatepass-{pass.Id}-r1-2", wa.Pdfs[1].RequestId); Assert.NotEqual(wa.Pdfs[0].RequestId, wa.Pdfs[1].RequestId);
    }
    [Fact] public async Task SendToGroup_AFailedSend_IsLogged_AndTheRetryKeepsTheSameRequestId()
    {
        using var db = Db(); var pass = await Pass(db); await Destination(db); var wa = new FakeWa(); var c = Gate(db);
        wa.Results.Enqueue(new WaSendResult(false, false, null, "WhatsApp number is not connected"));
        var failed = Assert.IsType<ObjectResult>(await c.SendToGroup(Request(pass.Id), wa));
        Assert.Equal(502, failed.StatusCode); Assert.Equal("WhatsApp number is not connected", Data(failed).GetProperty("message").GetString());
        Assert.Equal("Failed", (await db.WhatsAppSendLogs.SingleAsync()).Status);
        Assert.IsType<OkObjectResult>(await c.SendToGroup(Request(pass.Id), wa));
        Assert.Equal(2, wa.Pdfs.Count); Assert.Equal(wa.Pdfs[0].RequestId, wa.Pdfs[1].RequestId);
        Assert.Equal(["Failed", "Sent"], await db.WhatsAppSendLogs.OrderBy(l => l.Id).Select(l => l.Status).ToArrayAsync());
    }
    [Fact] public async Task SendToGroup_ANewRevision_IsANewDocument_AndSimilarIdsDoNotClash()
    {
        using var db = Db(); var pass = await Pass(db); await Destination(db); var wa = new FakeWa(); var c = Gate(db);
        await c.SendToGroup(Request(pass.Id), wa);
        var tracked = await db.GatePasses.SingleAsync(); tracked.Revision = 2; await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await c.SendToGroup(Request(pass.Id), wa)); Assert.Equal($"gatepass-{pass.Id}-r2", wa.Pdfs[1].RequestId);
        tracked.Revision = 10; await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await c.SendToGroup(Request(pass.Id), wa)); Assert.Equal($"gatepass-{pass.Id}-r10", wa.Pdfs[2].RequestId);
    }
    [Fact] public async Task SendToGroup_RefusesCancelledUnknownAndInvalidRequests()
    {
        using var db = Db(); var cancelled = await Pass(db, cancelled: true); var ok = await Pass(db); await Destination(db); var wa = new FakeWa(); var c = Gate(db);
        Assert.IsType<BadRequestObjectResult>(await c.SendToGroup(Request(cancelled.Id), wa));
        Assert.IsType<NotFoundObjectResult>(await c.SendToGroup(Request(999), wa));
        Assert.IsType<BadRequestObjectResult>(await c.SendToGroup(null, wa));
        Assert.IsType<BadRequestObjectResult>(await c.SendToGroup(Request(0), wa));
        Assert.IsType<BadRequestObjectResult>(await c.SendToGroup(Request(ok.Id, pdf: "not base64!!"), wa));
        Assert.IsType<BadRequestObjectResult>(await c.SendToGroup(Request(ok.Id, pdf: Convert.ToBase64String(Encoding.ASCII.GetBytes("<html>" + new string('x', 300)))), wa));
        Assert.IsType<BadRequestObjectResult>(await c.SendToGroup(Request(ok.Id, pdf: Convert.ToBase64String([1, 2, 3])), wa));
        Assert.Empty(wa.Pdfs);
    }
    [Fact] public async Task GroupSendInfo_ReportsWhetherSendingIsOn_AndTheLastFailure()
    {
        using var db = Db(); var c = Gate(db);
        Assert.False(Data(await c.GroupSendInfo()).GetProperty("enabled").GetBoolean());
        await Destination(db);
        var on = Data(await c.GroupSendInfo()); Assert.True(on.GetProperty("enabled").GetBoolean()); Assert.Equal("Gate Pass Group", on.GetProperty("groupName").GetString()); Assert.False(on.GetProperty("lastFailed").GetBoolean());
        db.WhatsAppSendLogs.Add(new() { DocumentType = WhatsAppDocumentTypes.GatePass, GroupId = Group, Status = "Failed", Error = "not connected" }); await db.SaveChangesAsync();
        var failed = Data(await c.GroupSendInfo()); Assert.True(failed.GetProperty("lastFailed").GetBoolean()); Assert.Equal("not connected", failed.GetProperty("lastError").GetString());
        db.WhatsAppSendLogs.Add(new() { DocumentType = WhatsAppDocumentTypes.GatePass, GroupId = Group, Status = "Sent" }); await db.SaveChangesAsync();
        Assert.False(Data(await c.GroupSendInfo()).GetProperty("lastFailed").GetBoolean());
        var destination = await db.WhatsAppDestinations.SingleAsync(); destination.Enabled = false; await db.SaveChangesAsync();
        Assert.True(Data(await c.GroupSendInfo()).GetProperty("enabled").GetBoolean());   // the old switch no longer matters
        destination.GroupId = ""; await db.SaveChangesAsync();
        Assert.False(Data(await c.GroupSendInfo()).GetProperty("enabled").GetBoolean());
    }
    [Fact] public async Task TheCaption_ListsEightItemsThenCountsTheRest()
    {
        using var db = Db(); var pass = await Pass(db);
        for (var i = 2; i <= 10; i++) pass.Lines.Add(new GatePassLine { SerialNo = i, Description = "Item " + i, TotalQuantity = 10 * i, Unit = "pcs" });
        var caption = GatePassMessage.Caption(pass);
        Assert.Contains("- Item 8: 80 PCS", caption); Assert.DoesNotContain("Item 9:", caption); Assert.Contains("- and 2 more", caption);
    }

    // ---- the WhatsApp column on the gate pass list ----
    private static JsonElement Row(IActionResult result, int id) => Data(result).GetProperty("data").EnumerateArray().Single(r => r.GetProperty("Id").GetInt32() == id);
    private static void Log(ApplicationDbContext db, int passId, string status, string requestId, string? error = null) { db.WhatsAppSendLogs.Add(new() { DocumentType = WhatsAppDocumentTypes.GatePass, DocumentId = passId, DocumentNumber = "OGP", GroupId = Group, Status = status, RequestId = requestId, Error = error, CreatedUtc = DateTime.UtcNow }); db.SaveChanges(); }
    [Fact] public async Task List_ShowsNoFlag_ForAPassThatWasNeverSent()
    {
        using var db = Db(); var pass = await Pass(db);
        Assert.Equal(JsonValueKind.Null, Row(await Gate(db).GetList("Outward"), pass.Id).GetProperty("waStatus").ValueKind);
    }
    [Fact] public async Task List_ShowsSent_WithTheTime_ForTheCurrentVersion_AndResetsAfterAnEdit()
    {
        using var db = Db(); var pass = await Pass(db); Log(db, pass.Id, "Sent", $"gatepass-{pass.Id}-r1");
        var sent = Row(await Gate(db).GetList("Outward"), pass.Id); Assert.Equal("sent", sent.GetProperty("waStatus").GetString()); Assert.NotEqual(JsonValueKind.Null, sent.GetProperty("waSentUtc").ValueKind);
        var tracked = await db.GatePasses.SingleAsync(); tracked.Revision = 2; await db.SaveChangesAsync();
        Assert.Equal(JsonValueKind.Null, Row(await Gate(db).GetList("Outward"), pass.Id).GetProperty("waStatus").ValueKind);   // the new version was not sent
        Log(db, pass.Id, "Sent", $"gatepass-{pass.Id}-r2-2");                                                                   // a send again of version 2 counts too
        Assert.Equal("sent", Row(await Gate(db).GetList("Outward"), pass.Id).GetProperty("waStatus").GetString());
    }
    [Fact] public async Task List_ShowsFailed_WithTheReason_UntilItIsSentAgain()
    {
        using var db = Db(); var pass = await Pass(db); Log(db, pass.Id, "Failed", $"gatepass-{pass.Id}-r1", "WhatsApp number is not connected");
        var failed = Row(await Gate(db).GetList("Outward"), pass.Id); Assert.Equal("failed", failed.GetProperty("waStatus").GetString()); Assert.Equal("WhatsApp number is not connected", failed.GetProperty("waError").GetString());
        Log(db, pass.Id, "Sent", $"gatepass-{pass.Id}-r1");
        Assert.Equal("sent", Row(await Gate(db).GetList("Outward"), pass.Id).GetProperty("waStatus").GetString());
    }
    [Fact] public async Task List_FlagsOnePassOnly_AndIgnoresTestMessages()
    {
        using var db = Db(); var a = await Pass(db); var b = await Pass(db); Log(db, a.Id, "Sent", $"gatepass-{a.Id}-r1");
        db.WhatsAppSendLogs.Add(new() { DocumentType = WhatsAppDocumentTypes.GatePass, DocumentNumber = "Test message", GroupId = Group, Status = "Failed", Error = "x" }); await db.SaveChangesAsync();
        var list = await Gate(db).GetList("Outward");
        Assert.Equal("sent", Row(list, a.Id).GetProperty("waStatus").GetString()); Assert.Equal(JsonValueKind.Null, Row(list, b.Id).GetProperty("waStatus").ValueKind);
    }

    // ---- the WhatsApp page ----
    [Fact] public async Task Page_ShowsTheStatus_AndConnectsAndDisconnects()
    {
        using var db = Db(); var wa = new FakeWa(); var c = Page(db, wa);
        var status = Data(await c.Status(default)); Assert.True(status.GetProperty("configured").GetBoolean()); Assert.Equal("connected", status.GetProperty("status").GetString()); Assert.Equal("923001234567", status.GetProperty("number").GetString());
        await c.Connect(default); await c.Disconnect(default); Assert.Equal(1, wa.Connects); Assert.Equal(1, wa.Disconnects);
    }
    [Fact] public async Task Page_ListsGroups_OrExplainsWhyNot()
    {
        using var db = Db(); var wa = new FakeWa(); var c = Page(db, wa);
        Assert.Equal(Group, Data(await c.Groups(default)).GetProperty("groups")[0].GetProperty("id").GetString());
        wa.Groups = new(false, [], "not connected");
        Assert.Equal("not connected", Data(Assert.IsType<BadRequestObjectResult>(await c.Groups(default))).GetProperty("message").GetString());
    }
    [Fact] public async Task Page_SavesTheGroupForGatePasses_AndUpdatesItLater()
    {
        using var db = Db(); var c = Page(db, new FakeWa());
        var rows = Data(await c.Destinations()); Assert.Equal(1, rows.GetArrayLength()); Assert.False(rows[0].GetProperty("enabled").GetBoolean());
        Assert.IsType<OkObjectResult>(await c.SaveDestination(new() { DocumentType = "GatePass", GroupId = Group, GroupName = "Gate Pass Group" }));
        Assert.True((await db.WhatsAppDestinations.SingleAsync()).Enabled);                 // a saved group turns sending on
        Assert.IsType<OkObjectResult>(await c.SaveDestination(new() { DocumentType = "GatePass", GroupId = "120363000000000002@g.us", GroupName = "Other" }));
        var saved = await db.WhatsAppDestinations.SingleAsync(); Assert.Equal("120363000000000002@g.us", saved.GroupId); Assert.Equal("Other", saved.GroupName); Assert.True(saved.Enabled);
        Assert.IsType<OkObjectResult>(await c.SaveDestination(new() { DocumentType = "GatePass", GroupId = "", GroupName = "" }));   // "No group" turns it off
        Assert.False((await db.WhatsAppDestinations.SingleAsync()).Enabled);
    }
    [Fact] public async Task Page_RejectsAnUnknownDocument_OrABadGroup()
    {
        using var db = Db(); var c = Page(db, new FakeWa());
        Assert.IsType<BadRequestObjectResult>(await c.SaveDestination(null));
        Assert.IsType<BadRequestObjectResult>(await c.SaveDestination(new() { DocumentType = "Nope", GroupId = Group }));
        Assert.IsType<BadRequestObjectResult>(await c.SaveDestination(new() { DocumentType = "GatePass", GroupId = "someone@s.whatsapp.net" }));
        Assert.IsType<OkObjectResult>(await c.SaveDestination(new() { DocumentType = "GatePass", GroupId = "" }));
        Assert.Equal(1, await db.WhatsAppDestinations.CountAsync());
    }
    [Fact] public async Task Page_SendsATestMessage_AndLogsTheResult()
    {
        using var db = Db(); var wa = new FakeWa(); var c = Page(db, wa);
        Assert.IsType<BadRequestObjectResult>(await c.SendTest(new() { DocumentType = "GatePass" }, default));
        await Destination(db);
        Assert.IsType<OkObjectResult>(await c.SendTest(new() { DocumentType = "GatePass" }, default));
        Assert.Equal(Group, Assert.Single(wa.Texts).Group);
        wa.Results.Enqueue(new WaSendResult(false, false, null, "not connected"));
        Assert.IsType<BadRequestObjectResult>(await c.SendTest(new() { DocumentType = "GatePass" }, default));
        Assert.Equal(["Sent", "Failed"], await db.WhatsAppSendLogs.OrderBy(l => l.Id).Select(l => l.Status).ToArrayAsync());
    }
    [Fact] public async Task Page_ShowsTheLatestTwentySends_NewestFirst()
    {
        using var db = Db(); for (var i = 1; i <= 25; i++) db.WhatsAppSendLogs.Add(new() { DocumentType = "GatePass", DocumentNumber = "OGP-" + i, GroupId = Group, Status = "Sent" });
        await db.SaveChangesAsync();
        var rows = Data(await Page(db, new FakeWa()).Log()); Assert.Equal(20, rows.GetArrayLength()); Assert.Equal("OGP-25", rows[0].GetProperty("DocumentNumber").GetString()); Assert.Equal("OGP-6", rows[19].GetProperty("DocumentNumber").GetString());
    }

    // ---- who can use it ----
    [Fact] public void OnlyAdminsReachThePage()
    {
        var attribute = typeof(WhatsAppGroupController).GetCustomAttribute<AuthorizeAttribute>(); Assert.NotNull(attribute); Assert.Contains("Admin", attribute!.Roles);
        Assert.Null(typeof(WhatsAppGroupController).GetCustomAttribute<AllowAnonymousAttribute>());
    }
    [Fact] public async Task TheMenuAndPermission_AreCreatedOnce_AndGivenToAdmins()
    {
        using var db = Db(); await DefaultMenuSeeder.ApplyAsync(db); await ClientWorkflowMenuSeeder.ApplyAsync(db); await ClientWorkflowMenuSeeder.ApplyAsync(db);
        var item = await db.MenuItems.SingleAsync(m => m.Controller == "WhatsAppGroup"); Assert.Equal("Index", item.Action); Assert.Equal("WhatsApp", item.Name); Assert.Null(item.ParentId); Assert.Equal("WhatsAppGroup.Manage", item.RequiredPermission);
        var admin = await db.Roles.SingleAsync(r => r.Name == "Admin");
        Assert.Equal(1, await db.RolePermissions.CountAsync(p => p.RoleId == admin.Id && p.Permission!.Code == "WhatsAppGroup.Manage"));
        Assert.Equal(1, await db.Permissions.CountAsync(p => p.Code == "WhatsAppGroup.Manage"));
    }
    [Theory]
    [InlineData("WhatsAppGroup", "Index", "GET", "WhatsAppGroup.Manage")] [InlineData("WhatsAppGroup", "Status", "GET", "WhatsAppGroup.Manage")] [InlineData("WhatsAppGroup", "Connect", "POST", "WhatsAppGroup.Manage")]
    [InlineData("WhatsAppGroup", "Disconnect", "POST", "WhatsAppGroup.Manage")] [InlineData("WhatsAppGroup", "SaveDestination", "POST", "WhatsAppGroup.Manage")] [InlineData("GatePass", "SendToGroup", "POST", "GatePass.Create")]
    public async Task EachActionNeedsItsPermission_AndRunsOutsideTheLongTransaction(string controller, string action, string method, string permission)
    {
        using var db = Db(); await DefaultMenuSeeder.ApplyAsync(db); await ClientWorkflowMenuSeeder.ApplyAsync(db);
        var role = await db.Roles.SingleAsync(r => r.Name == "Admin");
        db.AppUsers.Add(new() { Id = 901, Username = "wa-qa", FullName = "WA QA", IsActive = true, BranchIds = "1" }); db.UserRoles.Add(new() { UserId = 901, RoleId = role.Id }); await db.SaveChangesAsync();
        var permissions = new PermissionService(db);
        async Task<(bool Ran, ActionExecutingContext Context)> Run()
        {
            var http = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "901")], "QA")) }; http.Request.Method = method;
            var route = new RouteData(); route.Values["controller"] = controller; route.Values["action"] = action;
            var context = new ActionExecutingContext(new ActionContext(http, route, new ActionDescriptor(), new ModelStateDictionary()), [], new Dictionary<string, object>(), new object());
            var filter = new OperationFilter(db, permissions, NullLogger<OperationFilter>.Instance, Microsoft.Extensions.Options.Options.Create(new BranchOptions())); var ran = false;
            await filter.OnActionExecutionAsync(context, () => { ran = true; return Task.FromResult(new ActionExecutedContext(context, [], context.Controller) { Result = new OkObjectResult(new { success = true }) }); });
            return (ran, context);
        }
        var allowed = await Run(); Assert.True(allowed.Ran); Assert.Null(allowed.Context.Result);
        db.RolePermissions.RemoveRange(await db.RolePermissions.Where(p => p.RoleId == role.Id && p.Permission!.Code == permission).ToListAsync()); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var denied = await Run(); Assert.False(denied.Ran); Assert.IsType<ForbidResult>(denied.Context.Result);
    }
}
