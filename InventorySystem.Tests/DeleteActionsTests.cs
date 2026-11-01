using System.Security.Claims;
using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace InventorySystem.Tests;
public class DeleteActionsTests
{
    private static ApplicationDbContext Db()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static ControllerContext Context(string role="SuperAdmin",int id=1)=>new() { HttpContext=new DefaultHttpContext { User=new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role,role),new Claim(ClaimTypes.NameIdentifier,id.ToString()) },"Test")) } };
    private static AccountsController Payroll(ApplicationDbContext db)=>new(db,new AccountService(db)) { ControllerContext=Context() };
    private static AdminController Admin(ApplicationDbContext db,string role="SuperAdmin",int id=1)=>new(db,new PermissionService(db),Options.Create(new BranchOptions())) { ControllerContext=Context(role,id) };
    private static bool Success(IActionResult result)=>JsonSerializer.SerializeToElement(result is JsonResult j?j.Value:((ObjectResult)result).Value).GetProperty("success").GetBoolean();
    private static async Task SeedEmployee(ApplicationDbContext db){db.Employees.Add(new(){EmployeeId=1,EmployeeCode="E1",FullName="Employee",BasicSalary=10});await db.SaveChangesAsync();}
    [Fact] public async Task EmployeeWithoutSalaryCanBeDeleted()
    {using var db=Db();await SeedEmployee(db);Assert.True(Success(await Payroll(db).DeleteEmployee(new(){EmployeeId=1})));Assert.Empty(db.Employees);}
    [Fact] public async Task EmployeeWithSalaryHistoryCannotBeDeleted()
    {using var db=Db();await SeedEmployee(db);db.SalarySlips.Add(new(){EmployeeId=1,Period=new(2026,10,1)});await db.SaveChangesAsync();Assert.IsType<ConflictObjectResult>(await Payroll(db).DeleteEmployee(new(){EmployeeId=1}));Assert.Single(db.Employees);Assert.Single(db.SalarySlips);}
    [Fact] public async Task SalaryDraftDeleteAndIssuedProtection()
    {using var db=Db();await SeedEmployee(db);db.SalarySlips.AddRange(new(){SalarySlipId=1,EmployeeId=1,Period=new(2026,10,1)},new(){SalarySlipId=2,EmployeeId=1,Period=new(2026,11,1),Status="Issued"});await db.SaveChangesAsync();var c=Payroll(db);Assert.True(Success(await c.DeleteSalarySlip(new(){SalarySlipId=1})));Assert.IsType<ConflictObjectResult>(await c.DeleteSalarySlip(new(){SalarySlipId=2}));Assert.Equal(2,(await db.SalarySlips.SingleAsync()).SalarySlipId);Assert.Empty(db.GeneralLedger);}
    [Fact] public async Task PayrollDeleteRejectsStaleMissingAndInvalidRecords()
    {using var db=Db();await SeedEmployee(db);(await db.Employees.SingleAsync()).RowVersion=new byte[]{1};db.SalarySlips.Add(new(){SalarySlipId=1,EmployeeId=1,Period=new(2026,10,1),RowVersion=new byte[]{2}});await db.SaveChangesAsync();var c=Payroll(db);Assert.IsType<ConflictObjectResult>(await c.DeleteEmployee(new(){EmployeeId=1}));Assert.IsType<ConflictObjectResult>(await c.DeleteSalarySlip(new(){SalarySlipId=1}));Assert.IsType<NotFoundResult>(await c.DeleteEmployee(new(){EmployeeId=99}));Assert.IsType<NotFoundResult>(await c.DeleteSalarySlip(new(){SalarySlipId=99}));Assert.IsType<BadRequestObjectResult>(await c.DeleteEmployee(new()));Assert.IsType<BadRequestObjectResult>(await c.DeleteSalarySlip(new()));}
    [Fact] public async Task AdminCannotDeleteOwnOrLastSuperAdminLogin()
    {using var db=Db();db.Roles.Add(new(){Id=1,Name="SuperAdmin"});db.AppUsers.Add(new(){Id=1,Username="root",FullName="Root",IsActive=true});db.UserRoles.Add(new(){UserId=1,RoleId=1});await db.SaveChangesAsync();Assert.IsType<BadRequestObjectResult>(await Admin(db).DeleteUser(1));Assert.IsType<BadRequestObjectResult>(await Admin(db,id:99).DeleteUser(1));Assert.True((await db.AppUsers.SingleAsync()).IsActive);}
    [Fact] public async Task AdminDeleteDisablesOtherUserAndPreservesIdentity()
    {using var db=Db();db.Roles.Add(new(){Id=1,Name="SuperAdmin"});db.AppUsers.AddRange(new(){Id=1,Username="root",FullName="Root"},new(){Id=2,Username="other",FullName="Other"});db.UserRoles.AddRange(new(){UserId=1,RoleId=1},new(){UserId=2,RoleId=1});await db.SaveChangesAsync();Assert.True(Success(await Admin(db).DeleteUser(2)));Assert.False((await db.AppUsers.FindAsync(2))!.IsActive);Assert.Equal(2,await db.AppUsers.CountAsync());Assert.Equal(2,await db.UserRoles.CountAsync());Assert.IsType<ForbidResult>(await Admin(db,"Admin").DeleteUser(2));}
    [Fact] public async Task MenuDeleteHidesPageWithoutDeletingDataOrFolder()
    {using var db=Db();db.MenuItems.AddRange(new(){Id=1,Name="Folder"},new(){Id=2,ParentId=1,Name="Page",Controller="Accounts",Action="Employees",RequiredPermission="Accounts.View"});await db.SaveChangesAsync();Assert.True(Success(await Admin(db).DeleteMenuPage(2)));Assert.False((await db.MenuItems.FindAsync(2))!.IsActive);Assert.Equal(2,await db.MenuItems.CountAsync());Assert.IsType<BadRequestObjectResult>(await Admin(db).DeleteMenuPage(1));Assert.IsType<ForbidResult>(await Admin(db,"Admin").DeleteMenuPage(2));}
    [Fact] public async Task DeletePermissionRatherThanRoleNameControlsItemAndGroupDelete()
    {
        using var db=Db();db.Roles.Add(new(){Id=1,Name="Operator"});db.AppUsers.Add(new(){Id=1,Username="operator",FullName="Operator"});db.UserRoles.Add(new(){UserId=1,RoleId=1});db.Permissions.AddRange(new(){Id=1,Code="Item.Delete",Name="Delete items"},new(){Id=2,Code="ItemCategory.Delete",Name="Delete groups"});db.RolePermissions.AddRange(new(){RoleId=1,PermissionId=1},new(){RoleId=1,PermissionId=2});db.ItemCategory.AddRange(new(){CategoryId=1,CategoryName="Used"},new(){CategoryId=2,CategoryName="Unused"});db.Items.Add(new(){ItemID=1,CategoryID=1,ItemName="Item",IsActive=true});await db.SaveChangesAsync();var item=new ItemController(db){ControllerContext=Context("Operator")};var group=new CategoryController(db){ControllerContext=Context("Operator")};Assert.True(JsonSerializer.SerializeToElement((await item.GetList()).Value).GetProperty("canDelete").GetBoolean());Assert.True(Success(await item.Delete(new(){Id=1})));Assert.False((await db.Items.SingleAsync()).IsActive);Assert.True(Success(await group.Delete(2)));Assert.False(Success(await group.Delete(1)));db.RolePermissions.RemoveRange(db.RolePermissions);await db.SaveChangesAsync();Assert.Equal(403,(await item.Delete(new(){Id=1})).StatusCode);Assert.Equal(403,(await group.Delete(1)).StatusCode);
    }
    [Fact] public async Task GatePassDeleteCancelsAndRejectsStaleRevision()
    {using var db=Db();db.GatePasses.Add(new(){Id=1,Direction="Outward",PartyName="Party",Revision=3});await db.SaveChangesAsync();var c=new GatePassController(db);Assert.IsType<ConflictObjectResult>(await c.Delete(new(){Id=1,Revision=2,Reason="Wrong document"}));Assert.True(Success(await c.Delete(new(){Id=1,Revision=3,Reason="Wrong document"})));var g=await db.GatePasses.SingleAsync();Assert.True(g.IsCancelled);Assert.Equal(4,g.Revision);Assert.Equal("Wrong document",g.CancellationReason);Assert.IsType<ConflictObjectResult>(await c.Delete(new(){Id=1,Revision=4,Reason="Duplicate"}));}
}
