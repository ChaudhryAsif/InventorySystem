using InventorySystem.Core.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace InventorySystem.Authorization
{
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string PermissionCode { get; }
        public PermissionRequirement(string permissionCode)
        {
            PermissionCode = permissionCode;
        }
    }

    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IPermissionService _permissionService;

        public PermissionAuthorizationHandler(IPermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            // ── SuperAdmin bypass: skip all permission checks ──
            var roleClaim = context.User.FindFirst(ClaimTypes.Role)?.Value;
            if (roleClaim == "SuperAdmin")
            {
                context.Succeed(requirement);
                return;
            }

            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);

            if (!int.TryParse(userIdClaim?.Value, out int userId))
            {
                context.Fail();
                return;
            }

            var hasPermission = await _permissionService.HasPermissionAsync(userId, requirement.PermissionCode);

            if (hasPermission)
                context.Succeed(requirement);
            else
                context.Fail();
        }
    }
}