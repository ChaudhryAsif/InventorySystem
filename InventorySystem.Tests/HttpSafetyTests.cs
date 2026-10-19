using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Tests;

public class HttpSafetyTests : IAsyncLifetime
{
    private readonly string database = "InventorySystem_Test_" + Guid.NewGuid().ToString("N");
    private Process? process;
    private string address = "";
    private long itemId;
    private int partyId;
    private readonly List<string> serverErrors = [];
    private ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;TrustServerCertificate=True;").Options);
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("POS_TEST_SQL_SERVER") != "localdb") return;
        await using var db = Db(); await db.Database.EnsureCreatedAsync();
        var category = new ItemCategory { CategoryName = "HTTP test", IsActive = true }; db.ItemCategory.Add(category); await db.SaveChangesAsync();
        var party = new Party { PartyName = "HTTP customer", PartyType = "customer", Status = "active" };
        var item = new Items { ItemName = "HTTP item", CategoryID = category.CategoryId, SalePrice = 180, IsActive = true };
        db.Parties.Add(party); db.Items.Add(item);
        var user = new AppUser { Username = "smoke-admin", FullName = "HTTP Test Admin", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test-Only-Pass-123", 4), PrimaryRoleId = 1, BranchIds = "1" };
        db.AppUsers.Add(user); await db.SaveChangesAsync();
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = 1 });
        db.Stock.Add(new Stock { ItemId = item.ItemID, BranchId = 1, Quantity = 10, LastUpdated = DateTime.UtcNow }); await db.SaveChangesAsync();
        itemId = item.ItemID; partyId = party.PartyId;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        address = $"http://127.0.0.1:{port}";
        var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../InventorySystem"));
        var dll = Path.Combine(webRoot, "bin/Debug/net8.0/InventorySystem.dll");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = webRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(dll); start.ArgumentList.Add("--urls"); start.ArgumentList.Add(address);
        start.ArgumentList.Add("--ConnectionStrings:DefaultConnection"); start.ArgumentList.Add($"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;TrustServerCertificate=True;");
        start.ArgumentList.Add("--Accounting:PostToGeneralLedger"); start.ArgumentList.Add("true");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        process = Process.Start(start)!;
        process.OutputDataReceived += (_, _) => { }; process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock(serverErrors) serverErrors.Add(e.Data); };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(2) };
        for (var i = 0; i < 100; i++)
        {
            if (process.HasExited) throw new InvalidOperationException("Test server stopped: " + string.Join("\n", serverErrors));
            try { if ((await client.GetAsync("/Auth/Login")).IsSuccessStatusCode) return; } catch (HttpRequestException) {} catch (TaskCanceledException) {}
            await Task.Delay(100);
        }
        throw new InvalidOperationException("Test server did not become ready.");
    }
    public async Task DisposeAsync()
    {
        if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        process?.Dispose();
        if (Environment.GetEnvironmentVariable("POS_TEST_SQL_SERVER") != "localdb") return;
        if (!database.StartsWith("InventorySystem_Test_", StringComparison.Ordinal) || database.Length != 53) throw new InvalidOperationException("Unsafe test database name.");
        await using var db = Db(); await db.Database.EnsureDeletedAsync();
    }
    private async Task<(HttpClient client, string csrf)> Login()
    {
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
        var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
        var html = await client.GetStringAsync("/Auth/Login");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        var response = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string,string> { ["Username"]="smoke-admin", ["Password"]="Test-Only-Pass-123", ["__RequestVerificationToken"]=token }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        html = await client.GetStringAsync("/Sale/Index");
        Assert.Contains("id=\"scanInput\"", html);
        token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"csrf-token\" content=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token); return (client, token);
    }
    [SqlServerFact] public async Task SaleHttpFlow_EnforcesCsrfBranchAndIdempotency_AndPreservesCancellationHistory()
    {
        var (client, csrf) = await Login(); using var disposable = client;
        var payload = new { CustomerID = partyId.ToString(), BranchID = 1, PaymentMode = 1, SaleDate = DateTime.Today, NetAmount = 1,
            Items = new[] { new { ItemId = itemId, Quantity = 2, SalePrice = 180, DiscPer = 10, Total = 1 } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/Sale/Save", payload)).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf); client.DefaultRequestHeaders.Add("X-Request-ID", Guid.NewGuid().ToString());
        var first = await client.PostAsJsonAsync("/Sale/Save", payload);
        var text = await first.Content.ReadAsStringAsync(); Assert.True(first.IsSuccessStatusCode, text);
        var document = JsonDocument.Parse(text); Assert.True(document.RootElement.GetProperty("success").GetBoolean(), text);
        var id = document.RootElement.GetProperty("id").GetInt32();
        var retry = await client.PostAsJsonAsync("/Sale/Save", payload); Assert.Equal(text, await retry.Content.ReadAsStringAsync());
        await using (var check = Db())
        {
            Assert.Equal(1, await check.SaleInvoice.CountAsync()); Assert.Equal(324, (await check.SaleInvoice.SingleAsync()).NetAmount);
            Assert.Equal(8, (await check.Stock.SingleAsync()).Quantity); Assert.Equal(2, await check.GeneralLedger.CountAsync());
            Assert.Equal(1, await check.OperationRecords.CountAsync());
        }
        var unauthorizedBranch = new { CustomerID = partyId.ToString(), BranchID = 2, PaymentMode = 1, SaleDate = DateTime.Today, Items = payload.Items };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/Sale/Save", unauthorizedBranch)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Request-ID"); client.DefaultRequestHeaders.Add("X-Operation-Reason", "Test cancellation");
        var cancel = await client.PostAsync($"/Sale/Delete?id={id}", null); Assert.True(cancel.IsSuccessStatusCode, await cancel.Content.ReadAsStringAsync());
        await using (var check = Db())
        {
            Assert.Empty(await check.SaleInvoice.ToListAsync());
            var history = await check.SaleInvoice.IgnoreQueryFilters().SingleAsync(); Assert.True(history.IsCancelled); Assert.Equal("Test cancellation", history.CancellationReason);
            Assert.Equal(1, await check.SaleInvoiceBody.CountAsync()); Assert.Equal(10, (await check.Stock.SingleAsync()).Quantity);
            Assert.All(await check.AccountLedger.IgnoreQueryFilters().ToListAsync(), l => Assert.True(l.IsVoid));
            Assert.All(await check.GeneralLedger.ToListAsync(), l => Assert.True(l.IsVoid));
        }
    }
    [SqlServerFact] public async Task WhatsAppManagement_IsNotPublic_AndWebhookRejectsUnsignedPayload()
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
        var management = await client.GetAsync("/WhatsApp/Configure"); Assert.Equal(HttpStatusCode.Redirect, management.StatusCode);
        var webhook = await client.PostAsJsonAsync("/WhatsApp/Webhook", new { @object = "whatsapp_business_account" });
        Assert.Equal(HttpStatusCode.Unauthorized, webhook.StatusCode);
    }
}
