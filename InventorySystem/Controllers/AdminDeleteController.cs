using System.Security.Claims;
using InventorySystem.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Controllers;
public partial class AdminController
{
    [HttpPost("Users/{id}/Delete")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var check=SuperAdminOnly();if(check!=null)return check;
        if(User.FindFirstValue(ClaimTypes.NameIdentifier)==id.ToString())return BadRequest(new { success=false,message="You cannot delete your own login." });
        var user=await _context.AppUsers.Include(u=>u.UserRoles!).ThenInclude(r=>r.Role).SingleOrDefaultAsync(u=>u.Id==id);
        if(user==null)return NotFound();
        if(user.UserRoles?.Any(r=>r.Role?.IsActive==true&&r.Role.Name=="SuperAdmin")==true && !await _context.AppUsers.AnyAsync(u=>u.Id!=id&&u.IsActive&&u.UserRoles!.Any(r=>r.Role.IsActive&&r.Role.Name=="SuperAdmin")))
            return BadRequest(new { success=false,message="Keep at least one active SuperAdmin login." });
        user.IsActive=false;await _context.SaveChangesAsync();
        return Json(new { success=true,message="User disabled. Previous audit records were preserved." });
    }
    [HttpPost("MenuItems/{id}/DeletePage")]
    public async Task<IActionResult> DeleteMenuPage(int id)
    {
        var check=SuperAdminOnly();if(check!=null)return check;
        var menu=await _context.MenuItems.FindAsync(id);if(menu==null)return NotFound();
        if(string.IsNullOrEmpty(menu.Controller))return BadRequest(new { success=false,message="Use Delete Group for folders. Non-empty folders cannot be deleted." });
        menu.IsActive=false;await _context.SaveChangesAsync();return Json(new { success=true,message="Menu page hidden. It can be restored in Menu Builder." });
    }
}
