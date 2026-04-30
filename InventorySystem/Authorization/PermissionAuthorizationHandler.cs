using InventorySystem.Core.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace InventorySystem.Authorization
{
    /// <summary>
    /// Custom authorization handler for permission-based policies
    /// </summary>
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string PermissionCode { get; set; }

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