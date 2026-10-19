using System.Data;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using Microsoft.Extensions.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Authorization;

// Authorizes the endpoint itself and coordinates all database writes in one transaction.
public sealed class OperationFilter(ApplicationDbContext db, IPermissionService permissions, ILogger<OperationFilter> logger, IOptions<BranchOptions> branches) : IAsyncActionFilter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
        var action = context.RouteData.Values["action"]?.ToString() ?? "";
        if (action.Length > 0) action = char.ToUpperInvariant(action[0]) + action[1..];
        var request = context.HttpContext.Request;
        if (controller == "Auth" || context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        { await next(); return; }
        if (!int.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        { context.Result = await ExpiredSessionAsync(context.HttpContext); return; }
        var user = await db.AppUsers.Include(u => u.UserRoles!).ThenInclude(r => r.Role)
            .Include(u => u.PrimaryRole).FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user == null) { context.Result = await ExpiredSessionAsync(context.HttpContext); return; }
        var isSuperAdmin = user.UserRoles?.Any(r => r.Role?.IsActive == true && r.Role.Name == "SuperAdmin") == true;
        if (controller == "Admin" && !isSuperAdmin) { context.Result = new ForbidResult(); return; }
        var multiBranch = branches.Value.MultiBranchEnabled;
        db.RestrictBranches = !multiBranch || !isSuperAdmin;
        db.AllowedBranchIds = multiBranch
            ? user.BranchIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x.Trim(), out var id) ? id : 0).Where(x => x > 0).Distinct().ToArray()
            : [BranchOptions.DefaultBranchId];
        var write = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method);
        var module = controller switch { "Home" => "Dashboard", "Category" => "ItemCategory", "ItemRateList" => "Item", _ => controller };
        var verb = !write ? "View" : action.StartsWith("Delete") || action.StartsWith("Void") ? "Delete"
            : action.StartsWith("Update") || action.StartsWith("Edit") || action == "SaveConsumption" || action == "Record" || action == "Reverse" || action == "Complete" || action == "Cancel" ? "Edit"
            : action.StartsWith("Post") || action.StartsWith("Approve") ? "Post" : "Create";
        if (controller == "WhatsApp") verb = action is "Configure" or "RegisterWebhook" or "TestMessage" or "Playground" or "PlaygroundReply" ? "Config" : write ? "Reply" : "View";
        if (controller == "CostSheet" && action.Contains("Settings") && action != "GetSettings") verb = "Settings";
        if (write && action != "SavePayment" && action.StartsWith("Save") && context.ActionArguments.Values.Any(HasExistingId)) verb = "Edit";
        if (controller != "Admin" && !isSuperAdmin)
        {
            var allowed = await permissions.HasPermissionAsync(userId, $"{module}.{verb}");
            // Shared read-only lookup endpoints remain available to authorized transaction screens.
            if (!allowed && !write && ((controller == "Item" && action.StartsWith("Get")) || (controller == "Parties" && action.StartsWith("Get")) || (controller == "Category" && action.StartsWith("Get"))))
                foreach (var lookup in new[] { "Sale.View", "Sale.Create", "Purchase.View", "Purchase.Create", "CostSheet.View", "Production.View", "StockAdjustment.Create", "Accounts.View" })
                    if (await permissions.HasPermissionAsync(userId, lookup)) { allowed = true; break; }
            if (!allowed && controller == "CostSheet" && action is "GetSettings" or "GetMaterialOptions")
                foreach (var code in new[] { "CostSheet.View", "CostSheet.Create", "CostSheet.Edit" })
                    if (await permissions.HasPermissionAsync(userId, code)) { allowed = true; break; }
            if (!allowed) { context.Result = new ForbidResult(); return; }
        }
        if (!context.ModelState.IsValid) { context.Result = new BadRequestObjectResult(new { success = false, message = "Invalid input.", errors = context.ModelState.Where(x => x.Value!.Errors.Count > 0).Select(x => x.Key) }); return; }
        foreach (var value in context.ActionArguments.Values)
        {
            var property = value?.GetType().GetProperty("BranchID") ?? value?.GetType().GetProperty("BranchId");
            if (property == null) continue;
            var branch = property.GetValue(value) is int id ? id : BranchOptions.DefaultBranchId;
            if (!multiBranch && branch == 0) branch = BranchOptions.DefaultBranchId;
            if (db.RestrictBranches && !db.AllowedBranchIds.Contains(branch))
            { context.Result = new ForbidResult(); return; }
            if (!multiBranch && property.CanWrite) property.SetValue(value, BranchOptions.DefaultBranchId);
        }
        if (int.TryParse(request.Query["branchId"], out var queryBranch) && queryBranch > 0 && db.RestrictBranches && !db.AllowedBranchIds.Contains(queryBranch))
        { context.Result = new ForbidResult(); return; }
        // Bound scalar arguments must also use the default (including "all branches" reports).
        if (!multiBranch)
            foreach (var key in context.ActionArguments.Keys.Where(k => k.Equals("branchId", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                if (context.ActionArguments[key] is int branch && branch != 0 && branch != BranchOptions.DefaultBranchId)
                { context.Result = new ForbidResult(); return; }
                context.ActionArguments[key] = BranchOptions.DefaultBranchId;
            }
        if (!write || controller == "WhatsApp") { await next(); return; }
        var actionKey = controller + "/" + action;
        var requestId = request.Headers["X-Request-ID"].ToString();
        if (requestId.Length > 64) { context.Result = new BadRequestObjectResult(new { success = false, message = "Invalid request identifier." }); return; }
        var userKey = userId.ToString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(context.ActionArguments, JsonOptions))));
        ActionExecutedContext? executed = null;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (requestId.Length > 0)
            {
                var previous = await db.OperationRecords.FirstOrDefaultAsync(o => o.UserId == userKey && o.Action == actionKey && o.RequestId == requestId);
                if (previous != null)
                {
                    if (previous.RequestHash != hash) context.Result = new ConflictObjectResult(new { success = false, message = "This request identifier was already used for different data." });
                    else context.Result = new ContentResult { Content = previous.ResponseJson, ContentType = "application/json", StatusCode = 200 };
                    return;
                }
            }
            executed = await next();
            if (executed.Exception != null || !Succeeded(executed.Result)) { await tx.RollbackAsync(); return; }
            var resultValue = executed.Result switch { ObjectResult o => o.Value, JsonResult j => j.Value, _ => null };
            db.OperationRecords.Add(new OperationRecord
            {
                UserId = userKey, Action = actionKey, RequestId = requestId.Length > 0 ? requestId : null,
                RequestHash = requestId.Length > 0 ? hash : null, CreatedUtc = DateTime.UtcNow,
                Reference = context.ActionArguments.TryGetValue("id", out var id) ? id?.ToString() : null,
                ResponseJson = resultValue == null ? null : JsonSerializer.Serialize(resultValue, JsonOptions)
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            logger.LogError(ex, "Operation {Action} failed for user {UserId}", actionKey, userId);
            var failure = new ConflictObjectResult(new { success = false, message = "The operation could not be completed. Refresh and retry; no changes were saved." });
            if (executed != null) { executed.ExceptionHandled = true; executed.Result = failure; }
            else context.Result = failure;
        }
    }
    private static async Task<IActionResult> ExpiredSessionAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        http.User = new ClaimsPrincipal(new ClaimsIdentity());
        var request = http.Request;
        var accept = request.Headers.Accept.ToString();
        var browserPage = HttpMethods.IsGet(request.Method)
            && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase)
            && !accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        if (browserPage)
            return new RedirectToActionResult("Login", "Auth", new { returnUrl = request.PathBase + request.Path + request.QueryString });
        return new UnauthorizedResult();
    }

    private static bool HasExistingId(object? value) => value != null && new[] { "ItemID", "CategoryId", "PartyId", "CostSheetId", "VoucherId", "AccountHeadId" }
        .Any(name => value.GetType().GetProperty(name)?.GetValue(value) is object id && long.TryParse(id.ToString(), out var n) && n > 0);
    private static bool Succeeded(IActionResult? result)
    {
        if (result is StatusCodeResult s && s.StatusCode >= 400 || result is ObjectResult { StatusCode: >= 400 }) return false;
        var value = result switch { ObjectResult o => o.Value, JsonResult j => j.Value, _ => null };
        return value?.GetType().GetProperty("success")?.GetValue(value) is not false;
    }
}
