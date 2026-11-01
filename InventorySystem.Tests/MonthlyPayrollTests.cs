using System.Security.Claims;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Tests;
public class MonthlyPayrollTests
{
    private static ApplicationDbContext Db()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w=>w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static AccountsController Controller(ApplicationDbContext db)=>new(db,new AccountService(db)) { ControllerContext=new() { HttpContext=new DefaultHttpContext { User=new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name,"Payroll QA") },"Test")) } } };
    private static async Task<Employee> Worker(ApplicationDbContext db)
    {db.Database.EnsureCreated();var e=new Employee { EmployeeCode="W1",FullName="Mustaq",Designation="Dye operator",BasicSalary=30000,JoiningDate=new(2026,1,1) };db.Employees.Add(e);await db.SaveChangesAsync();return e;}
    private static AccountsController.PayrollMonthInput Sheet(int employeeId,decimal days=28,decimal hours=6,decimal otPay=750,decimal advance=1000)
        =>new() { Period="2026-09",Lines=[new() { EmployeeId=employeeId,AttendanceDays=days,OvertimeHours=hours,OvertimePay=otPay,Advance=advance }] };
    [Fact] public async Task SheetFormulasFollowTheClientSheet()
    {
        using var db=Db();var e=await Worker(db);var c=Controller(db);
        Assert.IsType<JsonResult>(await c.SavePayrollMonth(Sheet(e.EmployeeId)));
        var line=(await db.PayrollMonths.Include(m=>m.Lines).SingleAsync()).Lines.Single();
        Assert.Equal(28000m,line.SalaryAmount);Assert.Equal(28750m,line.TotalSalary);Assert.Equal(27750m,line.PayableSalary);Assert.Equal(30000m,line.MonthlySalary);
    }
    [Fact] public async Task PostingCreatesOneCashVoucherAndVoidReopensTheMonth()
    {
        using var db=Db();var e=await Worker(db);var c=Controller(db);await c.SavePayrollMonth(Sheet(e.EmployeeId));
        var month=await db.PayrollMonths.SingleAsync();
        var posted=await c.PostPayrollMonth(new() { PayrollMonthId=month.PayrollMonthId,RowVersion=month.RowVersion });Assert.True(posted is JsonResult,System.Text.Json.JsonSerializer.Serialize((posted as ObjectResult)?.Value));
        month=await db.PayrollMonths.SingleAsync();Assert.Equal("Posted",month.Status);
        var voucher=await db.Vouchers.Include(v=>v.Details).ThenInclude(d=>d.AccountHead).SingleAsync();
        Assert.Equal("PV",voucher.VoucherType);Assert.Equal(27750m,voucher.TotalAmount);
        Assert.Equal("5201",voucher.Details.Single(d=>d.Debit>0).AccountHead!.AccountCode);Assert.Equal("1101",voucher.Details.Single(d=>d.Credit>0).AccountHead!.AccountCode);
        Assert.IsType<ConflictObjectResult>(await c.SavePayrollMonth(new() { PayrollMonthId=month.PayrollMonthId,Period="2026-09",RowVersion=month.RowVersion,Lines=Sheet(e.EmployeeId,30).Lines }));
        Assert.IsType<ConflictObjectResult>(await c.PostPayrollMonth(new() { PayrollMonthId=month.PayrollMonthId,RowVersion=month.RowVersion }));
        Assert.IsType<JsonResult>(await c.VoidPayrollMonth(new() { PayrollMonthId=month.PayrollMonthId,RowVersion=month.RowVersion }));
        month=await db.PayrollMonths.SingleAsync();Assert.Equal("Draft",month.Status);Assert.Null(month.VoucherId);Assert.True((await db.Vouchers.SingleAsync()).IsVoid);
    }
    [Fact] public async Task SlipsAreCreatedFromAPostedMonthAndRemovedWhenVoided()
    {
        using var db=Db();var e=await Worker(db);var c=Controller(db);await c.SavePayrollMonth(Sheet(e.EmployeeId));var month=await db.PayrollMonths.SingleAsync();
        Assert.IsType<ConflictObjectResult>(await c.CreatePayrollSlips(new() { PayrollMonthId=month.PayrollMonthId,RowVersion=month.RowVersion }));
        await c.PostPayrollMonth(new() { PayrollMonthId=month.PayrollMonthId,RowVersion=month.RowVersion });month=await db.PayrollMonths.SingleAsync();
        Assert.IsType<JsonResult>(await c.CreatePayrollSlips(new() { PayrollMonthId=month.PayrollMonthId,RowVersion=month.RowVersion }));
        var slip=await db.SalarySlips.SingleAsync();Assert.Equal("Issued",slip.Status);Assert.Equal(month.PayrollMonthId,slip.PayrollMonthId);
        Assert.Equal(28000m,slip.BasicSalary);Assert.Equal(750m,slip.Allowances);Assert.Equal(1000m,slip.Deductions);Assert.Equal(27750m,slip.NetSalary);
        Assert.IsType<ConflictObjectResult>(await c.CreatePayrollSlips(new() { PayrollMonthId=month.PayrollMonthId,RowVersion=month.RowVersion }));
        await c.VoidPayrollMonth(new() { PayrollMonthId=month.PayrollMonthId,RowVersion=month.RowVersion });Assert.Empty(await db.SalarySlips.ToListAsync());
    }
    [Fact] public async Task SlipIsSkippedWhenAManualSlipAlreadyExistsForThatMonth()
    {
        using var db=Db();var e=await Worker(db);var c=Controller(db);
        db.SalarySlips.Add(new SalarySlip { EmployeeId=e.EmployeeId,EmployeeName="Mustaq",Period=new(2026,9,1),SlipDate=new(2026,9,30),BasicSalary=1,Status="Draft" });await db.SaveChangesAsync();
        await c.SavePayrollMonth(Sheet(e.EmployeeId));var month=await db.PayrollMonths.SingleAsync();await c.PostPayrollMonth(new() { PayrollMonthId=month.PayrollMonthId,RowVersion=month.RowVersion });
        Assert.IsType<BadRequestObjectResult>(await c.CreatePayrollSlips(new() { PayrollMonthId=month.PayrollMonthId,RowVersion=month.RowVersion }));
        Assert.Single(await db.SalarySlips.ToListAsync());
    }
    [Fact] public async Task InvalidSheetsAreRejected()
    {
        using var db=Db();var e=await Worker(db);var c=Controller(db);
        Assert.IsType<BadRequestObjectResult>(await c.SavePayrollMonth(Sheet(e.EmployeeId,days:32)));
        Assert.IsType<BadRequestObjectResult>(await c.SavePayrollMonth(Sheet(e.EmployeeId,advance:99999)));
        Assert.IsType<BadRequestObjectResult>(await c.SavePayrollMonth(Sheet(e.EmployeeId,hours:-1)));
        var twice=Sheet(e.EmployeeId);twice.Lines.Add(twice.Lines[0]);Assert.IsType<BadRequestObjectResult>(await c.SavePayrollMonth(twice));
        Assert.Empty(await db.PayrollMonths.ToListAsync());
        await c.SavePayrollMonth(Sheet(e.EmployeeId,days:0,hours:0,otPay:0,advance:0));var m=await db.PayrollMonths.SingleAsync();
        Assert.IsType<BadRequestObjectResult>(await c.PostPayrollMonth(new() { PayrollMonthId=m.PayrollMonthId,RowVersion=m.RowVersion }));
    }
    [Fact] public async Task EmployeeOnAPayrollSheetCannotBeDeleted()
    {
        using var db=Db();var e=await Worker(db);var c=Controller(db);await c.SavePayrollMonth(Sheet(e.EmployeeId));
        Assert.IsType<ConflictObjectResult>(await c.DeleteEmployee(new() { EmployeeId=e.EmployeeId,RowVersion=e.RowVersion }));
    }
    [Fact] public async Task MenuPageIsAddedOnceUnderEmployeePayroll()
    {
        using var db=Db();db.MenuItems.AddRange(LegacyMenuSeed.Create());await db.SaveChangesAsync();await AccountsFolderSeeder.ApplyAsync(db);
        var page=await db.MenuItems.SingleAsync(m=>m.Controller=="Accounts" && m.Action=="MonthlyPayroll");
        Assert.Equal((await db.MenuItems.SingleAsync(m=>m.Action=="Employees")).ParentId,page.ParentId);
        await AccountsFolderSeeder.ApplyAsync(db);Assert.Equal(1,await db.MenuItems.CountAsync(m=>m.Action=="MonthlyPayroll"));
    }
}
