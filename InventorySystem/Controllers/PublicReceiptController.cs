using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers;

// The customer's copy of a gate pass receipt. Opens without a sign-in; the secret token in the link is the only key.
[AllowAnonymous]
public class PublicReceiptController(ApplicationDbContext db) : Controller
{
    [HttpGet("/r/{token}")]
    public async Task<IActionResult> Show(string token)
    {
        // Keep the token out of referrers and caches, and out of search engines.
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        var pass = GatePassShare.LooksLikeToken(token)
            ? await db.GatePasses.AsNoTracking().IgnoreQueryFilters().Include(g => g.Lines).SingleOrDefaultAsync(g => g.ShareToken == token)
            : null;
        if (pass == null || pass.ShareExpiresUtc == null || pass.ShareExpiresUtc <= DateTime.UtcNow)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return View("Expired");
        }
        return View(pass);
    }
}
