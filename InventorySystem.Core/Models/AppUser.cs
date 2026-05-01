using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventorySystem.Core.Models
{
    public class AppUser
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        //[Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(50)]
        [Obsolete("Use Roles collection instead", false)]
        public string Role { get; set; } = "Admin"; // Keep for backward compatibility

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }

        // New: Foreign key for primary role
        public int? PrimaryRoleId { get; set; }

        [ForeignKey(nameof(PrimaryRoleId))]
        public Role? PrimaryRole { get; set; }

        // New: Collection of roles
        public ICollection<UserRole>? UserRoles { get; set; }
    }

    /// <summary>
    /// Maps user to multiple roles
    /// </summary>
    public class UserRole
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int RoleId { get; set; }

        public DateTime AssignedAt { get; set; }

        // Navigation
        [ForeignKey(nameof(UserId))]
        public AppUser? User { get; set; }

        [ForeignKey(nameof(RoleId))]
        public Role? Role { get; set; }
    }
}
