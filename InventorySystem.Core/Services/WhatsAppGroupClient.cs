using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InventorySystem.Core.Options;

namespace InventorySystem.Core.Services;

public sealed record WaGroup(string Id, string Name, int Members);
public sealed record WaStatus(bool Reachable, string Status, string? Number, string? Qr, string? PairingCode, string? Error);
public sealed record WaGroups(bool Ok, List<WaGroup> Groups, string? Error);
public sealed record WaSendResult(bool Ok, bool Duplicate, string? MessageId, string? Error);

// Talks to the WhatsApp service (the small Node app that holds the linked number). Every call returns a result; none throws.
public interface IWhatsAppGroupClient
{
    bool IsConfigured { get; }
    Task<WaStatus> StatusAsync(CancellationToken ct = default);
    Task<WaStatus> ConnectAsync(CancellationToken ct = default);
    Task<WaStatus> DisconnectAsync(CancellationToken ct = default);
    Task<WaGroups> GroupsAsync(CancellationToken ct = default);
    Task<WaSendResult> SendPdfAsync(string groupId, string fileName, string caption, byte[] pdf, string requestId, CancellationToken ct = default);
    Task<WaSendResult> SendTextAsync(string groupId, string text, CancellationToken ct = default);
}

public sealed class WhatsAppGroupClient(HttpClient http, WhatsAppGroupOptions options) : IWhatsAppGroupClient
{
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(20), Slow = TimeSpan.FromSeconds(60);
    private WhatsAppGroupOptions Options => options;
    public bool IsConfigured => Uri.TryCreate(Options.BaseUrl, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(Options.ApiKey);
    private const string NotConfigured = "The WhatsApp service is not set up on this server (WhatsAppGroup settings are missing).";

    private async Task<(bool Ok, JsonElement Json, string? Error)> CallAsync(HttpMethod method, string path, object? body, TimeSpan timeout, CancellationToken ct)
    {
        if (!IsConfigured) return (false, default, NotConfigured);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(new Uri(Options.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/')));
            request.Headers.Add("x-api-key", Options.ApiKey);
            if (body != null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request, cts.Token);
            var text = await response.Content.ReadAsStringAsync(cts.Token);
            JsonElement json = default;
            try { if (!string.IsNullOrWhiteSpace(text)) json = JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { }
            var message = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            if (response.StatusCode == HttpStatusCode.Unauthorized) return (false, json, "The WhatsApp service key is wrong. Check the WhatsAppGroup:ApiKey setting.");
            if (!response.IsSuccessStatusCode) return (false, json, message ?? $"The WhatsApp service answered {(int)response.StatusCode}.");
            return (true, json, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (false, default, "The WhatsApp service did not answer in time. It may be starting up; try again in a minute."); }
        catch (HttpRequestException) { return (false, default, "The WhatsApp service could not be reached."); }
    }

    private static string? Str(JsonElement json, string name) => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static WaStatus ToStatus((bool Ok, JsonElement Json, string? Error) r) => r.Ok
        ? new(true, Str(r.Json, "status") ?? "unknown", Str(r.Json, "number"), Str(r.Json, "qr"), Str(r.Json, "pairingCode"), Str(r.Json, "lastError"))
        : new(false, "unreachable", null, null, null, r.Error);

    public async Task<WaStatus> StatusAsync(CancellationToken ct = default) => ToStatus(await CallAsync(HttpMethod.Get, "status", null, Quick, ct));
    public async Task<WaStatus> ConnectAsync(CancellationToken ct = default) => ToStatus(await CallAsync(HttpMethod.Post, "connect", new { }, Quick, ct));
    public async Task<WaStatus> DisconnectAsync(CancellationToken ct = default) => ToStatus(await CallAsync(HttpMethod.Post, "disconnect", new { }, Slow, ct));

    public async Task<WaGroups> GroupsAsync(CancellationToken ct = default)
    {
        var r = await CallAsync(HttpMethod.Get, "groups", null, Slow, ct);
        if (!r.Ok) return new(false, [], r.Error);
        var groups = new List<WaGroup>();
        if (r.Json.TryGetProperty("groups", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var g in list.EnumerateArray())
                groups.Add(new(Str(g, "id") ?? "", Str(g, "name") ?? "", g.TryGetProperty("members", out var m) && m.TryGetInt32(out var n) ? n : 0));
        return new(true, groups.Where(g => g.Id.Length > 0).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList(), null);
    }

    public async Task<WaSendResult> SendPdfAsync(string groupId, string fileName, string caption, byte[] pdf, string requestId, CancellationToken ct = default)
    {
        var r = await CallAsync(HttpMethod.Post, "send-pdf", new { requestId, groupId, fileName, caption, base64 = Convert.ToBase64String(pdf) }, Slow, ct);
        return r.Ok ? new(true, r.Json.TryGetProperty("duplicate", out var d) && d.ValueKind == JsonValueKind.True, Str(r.Json, "messageId"), null) : new(false, false, null, r.Error);
    }

    public async Task<WaSendResult> SendTextAsync(string groupId, string text, CancellationToken ct = default)
    {
        var r = await CallAsync(HttpMethod.Post, "send-text", new { groupId, text }, Slow, ct);
        return r.Ok ? new(true, false, Str(r.Json, "messageId"), null) : new(false, false, null, r.Error);
    }
}
