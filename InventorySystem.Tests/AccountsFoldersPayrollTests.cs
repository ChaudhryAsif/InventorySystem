using System.Security.Claims;
using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Tests;
public class AccountsFoldersPayrollTests
{
    private static ApplicationDbContext Db()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static AccountsController Controller(ApplicationDbContext db)=>new(db,new AccountService(db)) { ControllerContext=new() { HttpContext=new DefaultHttpContext { User=new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name,"Payroll QA") },"Test")) } } };
    private static Employee Person()=>new() { EmployeeCode="e-1",FullName="Employee <One>",BasicSalary=30000,Designation="Operator",JoiningDate=new(2026,1,1) };
    private static AccountsController.SalarySlipInput Input(int id)=>new() { EmployeeId=id,Period=new(2026,10,20),SlipDate=new(2026,10,28),BasicSalary=30000,Allowances=2500.50m,Deductions=1250.25m };
    [Fact] public async Task FolderUpgradeIsCompleteAndPreservesLaterCustomization()
    {
        using var db=Db();db.MenuItems.AddRange(LegacyMenuSeed.Create());await db.SaveChangesAsync();await AccountsFolderSeeder.ApplyAsync(db);
        Assert.Equal(6,await db.MenuItems.CountAsync(m=>m.ParentId==19 && (m.Controller==null || m.Controller=="")));
        foreach(var action in new[] { "PaymentVoucher","ReceiptVoucher","VoucherList","TrialBalance","ProfitAndLoss","BalanceSheet","CashFlow","DebitNote","CreditNote","BankReconciliation","OpeningBalance","PartyLedger","GeneralLedgerView","Employees","SalarySlips" })
            Assert.NotEqual(19,(await db.MenuItems.SingleAsync(m=>m.Controller=="Accounts" && m.Action==action)).ParentId);
        var payroll=await db.MenuItems.SingleAsync(m=>m.Name=="Employee / Payroll");payroll.Name="Staff";payroll.SortOrder=99;await db.SaveChangesAsync();
        var count=await db.MenuItems.CountAsync();await AccountsFolderSeeder.ApplyAsync(db);Assert.Equal(count,await db.MenuItems.CountAsync());Assert.Equal("Staff",payroll.Name);Assert.Equal(99,payroll.SortOrder);
    }
    [Fact] public async Task ClientMenuVisibilityUpgradeWorksAfterExistingFolderUpgradeAndCanBeRestored()
    {
        using var db=Db();db.MenuItems.AddRange(LegacyMenuSeed.Create());await db.SaveChangesAsync();
        await AccountsFolderSeeder.ApplyAsync(db);
        // Simulate a database where the original folder upgrade was already deployed.
        var marker=await db.OperationRecords.SingleAsync(r=>r.Action==AccountsFolderSeeder.VisibilityAction);db.OperationRecords.Remove(marker);
        var extraActions=new[] { "JournalVoucher","ContraVoucher","CashBook","DayBook","OutstandingReport","AgingReport" };
        foreach(var page in await db.MenuItems.Where(m=>m.Controller=="Accounts"&&extraActions.Contains(m.Action!)).ToListAsync())page.IsActive=true;
        await db.SaveChangesAsync();var before=await db.MenuItems.AsNoTracking().ToDictionaryAsync(m=>m.Id);
        await AccountsFolderSeeder.ApplyAsync(db);
        var after=await db.MenuItems.AsNoTracking().ToListAsync();Assert.Equal(before.Count,after.Count);
        foreach(var page in after)
        {
            var old=before[page.Id];Assert.Equal(old.Name,page.Name);Assert.Equal(old.Controller,page.Controller);Assert.Equal(old.Action,page.Action);Assert.Equal(old.ParentId,page.ParentId);Assert.Equal(old.RequiredPermission,page.RequiredPermission);
            if(page.Controller=="Accounts"&&extraActions.Contains(page.Action!))Assert.False(page.IsActive);else Assert.Equal(old.IsActive,page.IsActive);
        }
        Assert.True((await db.MenuItems.SingleAsync(m=>m.Controller=="Accounts"&&m.Action=="ChartOfAccounts")).IsActive);
        var restored=await db.MenuItems.SingleAsync(m=>m.Controller=="Accounts"&&m.Action=="CashBook");restored.IsActive=true;await db.SaveChangesAsync();
        await AccountsFolderSeeder.ApplyAsync(db);Assert.True(restored.IsActive);Assert.Single(await db.OperationRecords.Where(r=>r.Action==AccountsFolderSeeder.VisibilityAction).ToListAsync());
    }
    [Fact] public async Task NestedMenusPruneUnauthorizedAndHiddenFoldersWithoutMutatingTrackedRows()
    {
        using var db=Db();db.MenuItems.AddRange(new MenuItem { Id=1,Name="Accounts" },new MenuItem { Id=2,Name="Payroll",ParentId=1 },new MenuItem { Id=3,Name="Employees",ParentId=2,Controller="Accounts",Action="Employees",RequiredPermission="Accounts.View" },new MenuItem { Id=4,Name="Other",ParentId=2,Controller="Admin",Action="Secret",RequiredPermission="Secret" });
        db.Roles.Add(new Role { Id=1,Name="Viewer" });db.Permissions.Add(new Permission { Id=1,Code="Accounts.View",Name="View" });db.RolePermissions.Add(new RolePermission { Id=1,RoleId=1,PermissionId=1 });await db.SaveChangesAsync();
        var service=new MenuService(db,new PermissionService(db));var tree=await service.GetMenuForRoleAsync(1);
        Assert.Equal(3,Assert.Single(Assert.Single(Assert.Single(tree).Children!).Children!).Id);
        var admin=await service.GetMenuForUserAsync(1,true);Assert.Equal(2,Assert.Single(admin).Children!.Single().Children!.Count);
        (await db.MenuItems.FindAsync(2))!.IsActive=false;await db.SaveChangesAsync();Assert.Empty(await service.GetMenuForUserAsync(1,true));
    }
    [Fact] public void RecursiveMenuValidationAcceptsTreeAndRejectsCyclesMissingPagesAndPageParents()
    {
        var rows=new List<MenuItem> { new(){Id=1,Name="Accounts"},new(){Id=2,Name="Folder"},new(){Id=3,Name="Page",Controller="Accounts"} };
        var tree=new List<AdminController.MenuNodeDto> { new(){Id=1,Branches=new(){new(){Id=2,Branches=new(){new(){Id=3}}}}} };
        Assert.Null(AdminController.ValidateMenuTree(tree,rows));tree[0].Branches![0].Branches!.Add(new(){Id=1});Assert.NotNull(AdminController.ValidateMenuTree(tree,rows));
        Assert.NotNull(AdminController.ValidateMenuTree(new(){new(){Id=1},new(){Id=2},new(){Id=3,Branches=new(){new(){Id=1}}}},rows));
        Assert.NotNull(AdminController.ValidateMenuTree(new(){new(){Id=1},new(){Id=2}},rows));
    }
    [Fact] public async Task EmployeeSaveNormalizesCodeAndKeepsInactiveHistory()
    {
        using var db=Db();var c=Controller(db);var person=Person();Assert.IsType<JsonResult>(await c.SaveEmployee(person));var saved=await db.Employees.SingleAsync();Assert.Equal("E-1",saved.EmployeeCode);
        Assert.IsType<ConflictObjectResult>(await c.SaveEmployee(Person()));
        saved.IsActive=false;await db.SaveChangesAsync();var result=JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(await c.GetEmployees(activeOnly:true)).Value);Assert.Empty(result.GetProperty("data").EnumerateArray());
        Assert.Single(JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(await c.GetEmployees()).Value).GetProperty("data").EnumerateArray());
    }
    [Theory][InlineData(-1)][InlineData(1.001)][InlineData(1000000001)] public async Task EmployeeRejectsInvalidSalary(decimal salary)
    {using var db=Db();var p=Person();p.BasicSalary=salary;Assert.IsType<BadRequestObjectResult>(await Controller(db).SaveEmployee(p));Assert.Empty(db.Employees);}
    [Fact] public async Task SalaryDraftCalculatesNetLocksAfterIssueAndKeepsEmployeeSnapshot()
    {
        using var db=Db();var c=Controller(db);await c.SaveEmployee(Person());var employee=await db.Employees.SingleAsync();var input=Input(employee.EmployeeId);
        Assert.IsType<JsonResult>(await c.SaveSalarySlip(input));var slip=await db.SalarySlips.SingleAsync();Assert.Equal(31250.25m,slip.NetSalary);Assert.Equal(1,slip.Period.Day);Assert.Equal("Payroll QA",slip.PreparedBy);
        input.SalarySlipId=slip.SalarySlipId;input.Allowances=3000;Assert.IsType<JsonResult>(await c.SaveSalarySlip(input));
        Assert.IsType<JsonResult>(await c.UpdateSalarySlipStatus(new(){SalarySlipId=slip.SalarySlipId}));Assert.Equal("Issued",slip.Status);Assert.NotNull(slip.IssuedUtc);
        employee.FullName="Changed employee";employee.BasicSalary=999;await db.SaveChangesAsync();Assert.Equal("Employee <One>",slip.EmployeeName);Assert.Equal(30000,slip.BasicSalary);
        Assert.IsType<ConflictObjectResult>(await c.SaveSalarySlip(input));Assert.IsType<ConflictObjectResult>(await c.UpdateSalarySlipStatus(new(){SalarySlipId=slip.SalarySlipId}));
        Assert.IsType<ViewResult>(await c.PrintSalarySlip(slip.SalarySlipId));Assert.Empty(db.Vouchers);Assert.Empty(db.GeneralLedger);
    }
    [Fact] public async Task SalaryRejectsDuplicateMonthAndInactiveEmployee()
    {
        using var db=Db();var c=Controller(db);await c.SaveEmployee(Person());var employee=await db.Employees.SingleAsync();var input=Input(employee.EmployeeId);
        await c.SaveSalarySlip(input);Assert.IsType<ConflictObjectResult>(await c.SaveSalarySlip(input));Assert.Single(db.SalarySlips);
        employee.IsActive=false;await db.SaveChangesAsync();input.Period=input.Period.AddMonths(1);Assert.IsType<BadRequestObjectResult>(await c.SaveSalarySlip(input));
    }
    [Theory][InlineData(-1,0,0)][InlineData(1,0,2)][InlineData(1,0.001,0)][InlineData(1000000001,0,0)]
    public async Task SalaryRejectsInvalidAmounts(decimal basic,decimal allowance,decimal deduction)
    {using var db=Db();var c=Controller(db);await c.SaveEmployee(Person());var input=Input((await db.Employees.SingleAsync()).EmployeeId);input.BasicSalary=basic;input.Allowances=allowance;input.Deductions=deduction;Assert.IsType<BadRequestObjectResult>(await c.SaveSalarySlip(input));Assert.Empty(db.SalarySlips);}
    [Fact] public async Task EmployeeAndDraftRejectStaleVersions()
    {
        using var db=Db();var c=Controller(db);await c.SaveEmployee(Person());var employee=await db.Employees.SingleAsync();employee.RowVersion=new byte[]{1};await db.SaveChangesAsync();var edit=Person();edit.EmployeeId=employee.EmployeeId;Assert.IsType<ConflictObjectResult>(await c.SaveEmployee(edit));
        await c.SaveSalarySlip(Input(employee.EmployeeId));var slip=await db.SalarySlips.SingleAsync();slip.RowVersion=new byte[]{2};await db.SaveChangesAsync();var input=Input(employee.EmployeeId);input.SalarySlipId=slip.SalarySlipId;
        Assert.IsType<ConflictObjectResult>(await c.SaveSalarySlip(input));Assert.IsType<ConflictObjectResult>(await c.UpdateSalarySlipStatus(new(){SalarySlipId=slip.SalarySlipId}));
    }
    [Fact] public void ModelIncludesUniqueMonthEmployeeCodeAndConcurrencyTokens()
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=AccountsModelCheck;Trusted_Connection=True;TrustServerCertificate=True").Options);Assert.False(db.Database.HasPendingModelChanges());
        Assert.Contains(db.Model.FindEntityType(typeof(SalarySlip))!.GetIndexes(),i=>i.IsUnique&&i.Properties.Select(p=>p.Name).SequenceEqual(new[]{"EmployeeId","Period"}));
        Assert.True(db.Model.FindEntityType(typeof(Employee))!.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.True(db.Model.FindEntityType(typeof(SalarySlip))!.FindProperty("RowVersion")!.IsConcurrencyToken);
    }
    public static async Task RunIsolatedSqlAsync()
    {
        var name="AccountsPayroll_Test_"+Guid.NewGuid().ToString("N");
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={name};Trusted_Connection=True;TrustServerCertificate=True").Options;
        ApplicationDbContext NewDb()=>new(options);
        try
        {
            await using(var db=NewDb()) { await db.Database.MigrateAsync();Assert.False(db.Database.HasPendingModelChanges());await AccountsFolderSeeder.ApplyAsync(db);await AccountsFolderSeeder.ApplyAsync(db);Assert.Single(await db.OperationRecords.Where(r=>r.Action==AccountsFolderSeeder.UpgradeAction).ToListAsync()); }
            Console.WriteLine("PASS SQL full migration chain and one-time menu upgrade");
            int employeeId,slipId;byte[] employeeVersion;
            await using(var db=NewDb()) { var c=Controller(db);await c.SaveEmployee(Person());var e=await db.Employees.SingleAsync();employeeId=e.EmployeeId;employeeVersion=e.RowVersion;Assert.Equal(8,e.RowVersion.Length);await c.SaveSalarySlip(Input(employeeId));var slip=await db.SalarySlips.SingleAsync();Assert.Equal(8,slip.RowVersion.Length);Assert.Equal(31250.25m,slip.NetSalary);slipId=slip.SalarySlipId; }
            Console.WriteLine("PASS SQL employee and monthly salary persistence with rowversions");
            await using(var db=NewDb()) { var duplicate=Person();duplicate.EmployeeCode="E-1";db.Employees.Add(duplicate);await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync()); }
            Console.WriteLine("PASS SQL unique employee code index");
            await using(var a=NewDb()) await using(var b=NewDb()) { var one=await a.Employees.FindAsync(employeeId);var two=await b.Employees.FindAsync(employeeId);one!.FullName="Updated employee";two!.FullName="Lost update";await a.SaveChangesAsync();await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>b.SaveChangesAsync()); }
            Console.WriteLine("PASS SQL concurrent employee update rejects lost update");
            await using(var db=NewDb()) { var c=Controller(db);var stale=Person();stale.EmployeeId=employeeId;stale.RowVersion=employeeVersion;Assert.IsType<ConflictObjectResult>(await c.SaveEmployee(stale));var slip=await db.SalarySlips.FindAsync(slipId);Assert.IsType<JsonResult>(await c.UpdateSalarySlipStatus(new(){SalarySlipId=slipId,RowVersion=slip!.RowVersion}));Assert.Equal("Employee <One>",slip.EmployeeName);Assert.Equal("Issued",slip.Status);var edit=Input(employeeId);edit.SalarySlipId=slipId;edit.RowVersion=slip.RowVersion;Assert.IsType<ConflictObjectResult>(await c.SaveSalarySlip(edit));Assert.Empty(db.GeneralLedger); }
            Console.WriteLine("PASS SQL stale form, issued-slip lock and historical employee snapshot");
            await using(var db=NewDb()) { db.SalarySlips.Add(new() {EmployeeId=employeeId,Period=new(2026,10,1),SlipDate=new(2026,10,28)});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync()); }
            Console.WriteLine("PASS SQL unique employee/month index");
        }
        finally
        {
            if(!name.StartsWith("AccountsPayroll_Test_") || name.Length!=53)throw new InvalidOperationException("Unsafe test database name.");
            await using var db=NewDb();await db.Database.EnsureDeletedAsync();Console.WriteLine("Isolated payroll SQL database removed.");
        }
    }

}
