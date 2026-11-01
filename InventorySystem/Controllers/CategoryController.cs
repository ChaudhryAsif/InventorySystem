using InventorySystem.Core.Services;
using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class CategoryController : Controller
    {
        private readonly ApplicationDbContext _context;
        public CategoryController(ApplicationDbContext context)
        {
            _context = context;
        }

        private async Task<bool> CanDeleteAsync() => User?.IsInRole("SuperAdmin") == true || User!=null && await new PermissionService(_context).HasPermissionAsync(User,"ItemCategory.Delete");

        public async Task<IActionResult> Index() => await Section("RawMaterial");
        public async Task<IActionResult> FinishedGoods() => await Section("FinishedGoods");
        private async Task<IActionResult> Section(string type)
        {
            ViewBag.GroupType=type;
            ViewBag.CanDelete=await CanDeleteAsync();
            return View("Index",await _context.ItemCategory.Where(g=>g.GroupType==type).ToListAsync());
        }

        [HttpPost]
        public async Task<JsonResult> Save(ItemCategoryViewModel categoryViewModel)
        {
            if (categoryViewModel == null || string.IsNullOrWhiteSpace(categoryViewModel.CategoryName) ||
                !categoryViewModel.IsActive.HasValue || categoryViewModel.CategoryId < 0)
                return Json(new { success = false, message = "Enter a group name and select Active or Inactive." });

            if(categoryViewModel.PageGroupType!=null && (categoryViewModel.PageGroupType is not ("RawMaterial" or "FinishedGoods") || categoryViewModel.GroupType!=categoryViewModel.PageGroupType || categoryViewModel.CategoryId>0 && !await _context.ItemCategory.AnyAsync(g=>g.CategoryId==categoryViewModel.CategoryId&&g.GroupType==categoryViewModel.PageGroupType)))
                return Json(new{success=false,message="This group belongs to a different inventory section."});
            var address = string.IsNullOrWhiteSpace(categoryViewModel.Address) ? null : categoryViewModel.Address.Trim();
            var contactNumber = string.IsNullOrWhiteSpace(categoryViewModel.ContactNumber) ? null : categoryViewModel.ContactNumber.Trim();
            if (address?.Length > 500 || contactNumber?.Length > 32)
                return Json(new { success = false, message = "Address/location must be at most 500 characters and contact number at most 32 characters." });

            try
            {
                // Keep the preview check and all category/item writes in one transaction.
                // Serializable protects the item set from changing during the confirmed save.
                await using var tx = _context.Database.CurrentTransaction is { } current
                    ? new AtomicTransaction(current, false)
                    : new AtomicTransaction(_context.Database.IsRelational()
                        ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                        : await _context.Database.BeginTransactionAsync(), true);
                var category = categoryViewModel.CategoryId == 0 ? new ItemCategory { CreatedDate = DateTime.Now }
                    : await _context.ItemCategory.SingleOrDefaultAsync(c => c.CategoryId == categoryViewModel.CategoryId);
                if (category == null) return Json(new { success = false, message = "Group not found. Reload the group list." });
                // Missing type on older callers preserves the classification of an existing group.
                var groupType = categoryViewModel.GroupType ?? category.GroupType;
                if ((category.CategoryId == 0 || groupType != null) && groupType is not ("RawMaterial" or "FinishedGoods"))
                    return Json(new { success = false, message = "Select Raw Material or Finished Goods as the group type." });
                if (groupType != null && await _context.Items.AnyAsync(i => i.CategoryID == category.CategoryId && i.ItemType != groupType))
                    return Json(new { success = false, message = "This group contains items of a different type. Move them to a matching group before changing the group type." });
                var statusChanged = category.CategoryId != 0 && (category.IsActive == true) != categoryViewModel.IsActive.Value;
                var items = statusChanged
                    ? await _context.Items.Where(i => i.CategoryID == category.CategoryId).OrderBy(i => i.ItemID).ToListAsync()
                    : new List<Items>();
                if (statusChanged)
                {
                    // This is a stale-preview fingerprint, not an authorization credential.
                    var snapshot = JsonSerializer.Serialize(new { category.CategoryId, category.CategoryName,
                        category.Description, category.Address, category.ContactNumber,
                        requestedAddress = address, requestedContactNumber = contactNumber,
                        category.GroupType, requestedGroupType = groupType, category.IsActive, target = categoryViewModel.IsActive,
                        requestedName = categoryViewModel.CategoryName.Trim(), requestedDescription = categoryViewModel.Description,
                        items = items.Select(i => new { i.ItemID, i.ItemName, i.IsActive }) });
                    var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
                    if (categoryViewModel.StatusConfirmationToken != token)
                        return Json(new { success = false, requiresConfirmation = true, confirmationToken = token,
                            categoryName = category.CategoryName, targetActive = categoryViewModel.IsActive.Value,
                            message = string.IsNullOrEmpty(categoryViewModel.StatusConfirmationToken)
                                ? "Review the linked items before confirming the status change."
                                : "The group or its items changed. Review the updated list and confirm again.",
                            items = items.Select(i => new { itemId = i.ItemID, itemName = i.ItemName ?? "Unnamed item", isActive = i.IsActive == true }),
                            changedItemCount = items.Count(i => (i.IsActive == true) != categoryViewModel.IsActive.Value) });
                }
                else if (!string.IsNullOrEmpty(categoryViewModel.StatusConfirmationToken))
                {
                    return Json(new { success = false, message = "The group status changed while you were reviewing. Reload it before saving." });
                }

                category.CategoryName = categoryViewModel.CategoryName.Trim();
                category.Description = categoryViewModel.Description;
                category.Address = address;
                category.ContactNumber = contactNumber;
                category.GroupType = groupType;
                category.IsActive = categoryViewModel.IsActive;
                foreach (var item in items) item.IsActive = categoryViewModel.IsActive;
                if (category.CategoryId == 0) await _context.ItemCategory.AddAsync(category);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Json(new { success = true, message = statusChanged
                    ? $"Group and all {items.Count} linked item(s) are now {(category.IsActive == true ? "active" : "inactive")}."
                    : "Group saved successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error saving group.", error = ex.Message });
            }
        }

        // Print page for the ticked group ids (or every group of the type). detail=1 prints full cards.
        [HttpGet]
        public async Task<IActionResult> Print(string? ids, string? groupType, bool detail = false, bool popup = false)
        {
            if (groupType is not (null or "RawMaterial" or "FinishedGoods")) return BadRequest();
            var query = _context.ItemCategory.AsNoTracking().Where(g => groupType == null || g.GroupType == groupType);
            if (ids != null)
            {
                var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).Distinct().Take(2000).ToList();
                query = query.Where(g => idList.Contains(g.CategoryId));
            }
            var rows = await query.OrderBy(g => g.CategoryName).Take(5000).Select(g => new GroupPrintRow(g.CategoryId, g.CategoryName, g.GroupType,
                g.Description, g.Address, g.ContactNumber, g.IsActive ?? false, g.CreatedDate, _context.Items.Count(i => i.CategoryID == g.CategoryId))).ToListAsync();
            return View(new PrintPage<GroupPrintRow>(rows, detail, popup));
        }

        [HttpGet]
        public async Task<IActionResult> GetById(int id,string? groupType=null)
        {
            var category = await _context.ItemCategory
                .FirstOrDefaultAsync(c => c.CategoryId == id && (groupType==null||c.GroupType==groupType));

            if (category == null)
                return NotFound();

            return Json(category);
        }

        public async Task<IActionResult> GetList(string? groupType=null)
        {
            if(groupType is not (null or "RawMaterial" or "FinishedGoods"))return BadRequest();
            var categories = _context.ItemCategory.Where(g=>groupType==null||g.GroupType==groupType).ToList();
            return Json(new { success = true, data = categories, canDelete = await CanDeleteAsync() });
        }

        [HttpPost]
        public async Task<JsonResult> Delete(int id,string? groupType=null)
        {
            if (!await CanDeleteAsync())
                return new JsonResult(new { success = false, message = "You do not have permission to delete groups." }) { StatusCode = 403 };
            try
            {
                var category = await _context.ItemCategory.SingleOrDefaultAsync(g=>g.CategoryId==id&&(groupType==null||g.GroupType==groupType));

                if (category == null)
                    return Json(new { success = false, message = "Group not found." });

                if (await _context.Items.AnyAsync(i => i.CategoryID == id))
                    return Json(new { success = false, message = "This group has linked items and cannot be deleted. Set it to Inactive to preserve invoice history." });

                _context.ItemCategory.Remove(category);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Group deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error deleting group.", error = ex.Message });
            }
        }

        public int GetMaxCategoryId()
        {
            var maxId = _context.ItemCategory.Max(c => (int?)c.CategoryId) ?? 0;
            return maxId + 1;
        }

        [HttpGet]
        public JsonResult GetCategoriesDropdown()
        {
            var categories = _context.ItemCategory
                .Where(c => c.IsActive == true)
                .Select(c => new
                {
                    CategoryId = c.CategoryId,
                    CategoryName = c.CategoryName,
                    c.GroupType
                })
                .ToList();

            return Json(categories);
        }

    }
}
