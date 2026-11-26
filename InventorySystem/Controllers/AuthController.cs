using InventorySystem.Data;
using InventorySystem.Core.Services;
using InventorySystem.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace InventorySystem.Controllers
{
    public class AuthController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IPermissionService _permissionService;

        public AuthController(ApplicationDbContext db, IPermissionService permissionService)
        {
            _db = db;
            _permissionService = permissionService;
        }

        // GET /Auth/Login
        [HttpGet]
        public async Task<IActionResult> Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                    && await _db.AppUsers.AnyAsync(u => u.Id == userId && u.IsActive))
                    return RedirectToAction("Index", "Home");

                // A cookie from an old database or disabled account must not loop back to Home.
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
                ModelState.AddModelError(string.Empty, "Your previous session is no longer valid. Please sign in again.");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // POST /Auth/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
                return View(model);

            var user = _db.AppUsers
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .Include(u => u.PrimaryRole)
                .FirstOrDefault(u => u.Username == model.Username && u.IsActive);

            if (user is null || !BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
            {
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View(model);
            }

            // Build claims
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new("FullName", user.FullName)
            };

            // Add primary role (backward compatibility)
            if (user.PrimaryRole != null)
            {
                claims.Add(new Claim(ClaimTypes.Role, user.PrimaryRole.Name));
            }
            else if (!string.IsNullOrEmpty(user.Role))
            {
                claims.Add(new Claim(ClaimTypes.Role, user.Role));
            }

            // Add all user roles
            if (user.UserRoles?.Count > 0)
            {
                foreach (var userRole in user.UserRoles.Where(ur => ur.Role?.IsActive == true))
                {
                    claims.Add(new Claim(ClaimTypes.Role, userRole.Role?.Name ?? ""));
                }
            }

            var principal = new ClaimsPrincipal(
                new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = model.RememberMe
                        ? DateTimeOffset.UtcNow.AddDays(30)
                        : DateTimeOffset.UtcNow.AddHours(8)
                });

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");
        }

        // POST /Auth/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Auth");
        }

        // GET /Auth/AccessDenied
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        public sealed class ChangePasswordInput
        {
            public string? CurrentPassword { get; set; }
            public string? NewPassword { get; set; }
            public string? ConfirmPassword { get; set; }
        }

        // GET /Auth/ChangePassword: any signed-in user can change their own password.
        [Authorize]
        [HttpGet]
        public IActionResult ChangePassword() => View(new ChangePasswordInput());

        // POST /Auth/ChangePassword
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordInput input)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Challenge();
            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
            if (user == null) return Challenge();
            if (string.IsNullOrEmpty(input.CurrentPassword) || !BCrypt.Net.BCrypt.Verify(input.CurrentPassword, user.PasswordHash))
                ModelState.AddModelError(nameof(input.CurrentPassword), "The current password is not correct.");
            else if (string.IsNullOrEmpty(input.NewPassword) || input.NewPassword.Length < 6)
                ModelState.AddModelError(nameof(input.NewPassword), "The new password must be at least 6 characters long.");
            else if (input.NewPassword != input.ConfirmPassword)
                ModelState.AddModelError(nameof(input.ConfirmPassword), "The two new passwords do not match.");
            else if (input.NewPassword == input.CurrentPassword)
                ModelState.AddModelError(nameof(input.NewPassword), "Choose a password different from the current one.");
            if (!ModelState.IsValid) return View(new ChangePasswordInput());
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(input.NewPassword);
            _db.AdminAuditLogs.Add(new() { ActorId = user.Id, ActorName = user.Username, Action = "Password changed", Target = user.Username, Detail = "The user changed their own password.", CreatedUtc = DateTime.UtcNow });
            await _db.SaveChangesWithAuditAsync();
            ViewBag.Done = true;
            return View(new ChangePasswordInput());
        }
    }
}
