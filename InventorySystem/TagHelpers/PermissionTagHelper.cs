using Microsoft.AspNetCore.Razor.TagHelpers;
using InventorySystem.Core.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InventorySystem.TagHelpers
{
    /// <summary>
    /// TagHelper to hide UI elements based on permissions
    /// Usage: <div permission="CostSheet.View">Content</div>
    /// </summary>
    [HtmlTargetElement("*", Attributes = "permission")]
    public class PermissionTagHelper : TagHelper
    {
        private readonly IPermissionService _permissionService;

        [ViewContext]
        public ViewContext? ViewContext { get; set; }

        public string? Permission { get; set; }

        public PermissionTagHelper(IPermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            var user = ViewContext?.HttpContext.User;

            if (user?.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(Permission))
            {
                output.SuppressOutput();
                return;
            }

            var hasPermission = await _permissionService.HasPermissionAsync(user, Permission);

            if (!hasPermission)
            {
                output.SuppressOutput();
            }

            output.Attributes.RemoveAll("permission");
        }
    }
}