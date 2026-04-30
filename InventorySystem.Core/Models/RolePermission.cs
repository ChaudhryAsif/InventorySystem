using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Core.Models
{
    /// <summary>
    /// Represents a role in the system
    /// </summary>
    public class Role
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }

        // Navigation
        public ICollection<RolePermission>? RolePermissions { get; set; }
        public ICollection<AppUser>? Users { get; set; }
    }

    /// <summary>
    /// Represents a permission in the system
    /// </summary>
    public class Permission
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Code { get; set; } = string.Empty; // e.g., "CostSheet.View", "Invoice.Create"

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty; // e.g., "View Cost Sheet"

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Module { get; set; } = string.Empty; // e.g., "CostSheet", "Invoice"

        [MaxLength(50)]
        public string Action { get; set; } = string.Empty; // e.g., "View", "Create", "Edit", "Delete"

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }

        // Navigation
        public ICollection<RolePermission>? RolePermissions { get; set; }
    }

    /// <summary>
    /// Junction table for Role-Permission relationship
    /// </summary>
    public class RolePermission
    {
        public int Id { get; set; }

        [Required]
        public int RoleId { get; set; }

        [Required]
        public int PermissionId { get; set; }

        public DateTime CreatedAt { get; set; }

        // Navigation
        public Role? Role { get; set; }
        public Permission? Permission { get; set; }
    }

    /// <summary>
    /// Represents a menu item in the system
    /// </summary>
    public class MenuItem
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty; // e.g., "Cost Sheet"

        [MaxLength(500)]
        public string Icon { get; set; } = string.Empty; // e.g., "📦"

        public string? Controller { get; set; }

        public string? Action { get; set; }

        public int? ParentId { get; set; } // For sub-menus

        public int SortOrder { get; set; }

        [Required]
        public string RequiredPermission { get; set; } = string.Empty; // e.g., "CostSheet.View"

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }

        // Navigation
        public MenuItem? Parent { get; set; }
        public ICollection<MenuItem>? Children { get; set; }
    }
}