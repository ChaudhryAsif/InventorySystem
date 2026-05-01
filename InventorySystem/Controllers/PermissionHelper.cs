using InventorySystem.Core.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace InventorySystem.Controllers
{
    /// <summary>
    /// Helper class for permission checking in controllers.
    /// Use this to maintain consistent permission handling across all controllers.
    /// </summary>
    public static class PermissionHelper
    {
        /// <summary>
        /// Checks if user has permission. If not, redirects to Access Denied page.
        /// </summary>
        public static async Task<IActionResult?> CheckPermissionAsync(
            this ControllerBase controller,
            IPermissionService permissionService,
            string permissionCode)
        {
            if (controller?.User == null)
                return controller!.RedirectToAction("Login", "Auth");

            var userId = int.Parse(controller.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            if (userId == 0)
                return controller.RedirectToAction("Login", "Auth");

            var hasPermission = await permissionService.HasPermissionAsync(userId, permissionCode);

            if (!hasPermission)
            {
                return controller.RedirectToAction("AccessDenied", "Auth");
            }

            return null;
        }
    }
}