using System.Data;
using InventorySystem.Core.Models;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Data;

// One-time upgrade: future restarts preserve the administrator's arrangement.
public static class AccountsFolderSeeder
{
    public const string UpgradeAction = "Accounts/FolderUpgrade";
    public const string VisibilityAction = "Accounts/ClientMenuVisibility";
    public static async Task ApplyAsync(ApplicationDbContext db)
    {
        await ApplyFoldersAsync(db);
        await ApplyVisibilityAsync(db);
        await ApplyMonthlyPayrollMenuAsync(db);
        await ApplyExpensesMenuAsync(db);
    }
    public const string ExpensesMenuAction = "Accounts/ExpensesMenu";
    // Adds the Expenses page once beside Payment Voucher; later admin moves or hides are kept.
    private static async Task ApplyExpensesMenuAsync(ApplicationDbContext db)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(IsolationLevel.Serializable):null;
            if(await db.OperationRecords.AnyAsync(x=>x.UserId=="system" && x.Action==ExpensesMenuAction && x.RequestId=="v1"))return;
            var payment=await db.MenuItems.FirstOrDefaultAsync(m=>m.Controller=="Accounts" && m.Action=="PaymentVoucher");
            if(payment?.ParentId==null)return;
            if(!await db.MenuItems.AnyAsync(m=>m.Controller=="Accounts" && m.Action=="Expenses"))
            {
                var last=await db.MenuItems.Where(m=>m.ParentId==payment.ParentId).MaxAsync(m=>(int?)m.SortOrder) ?? 0;
                db.MenuItems.Add(new() { Controller="Accounts",Action="Expenses",Name="Expenses",RequiredPermission="Accounts.View",Icon="📄",ParentId=payment.ParentId,SortOrder=last+1,CreatedAt=DateTime.UtcNow });
            }
            db.OperationRecords.Add(new() { UserId="system",Action=ExpensesMenuAction,RequestId="v1",CreatedUtc=DateTime.UtcNow,ResponseJson="{}" });
            await db.SaveChangesAsync();if(tx!=null)await tx.CommitAsync();
        });
    }
    public const string PayrollMenuAction = "Accounts/MonthlyPayrollMenu";
    // Adds the Monthly Payroll page once under Employee / Payroll; later admin moves or hides are kept.
    private static async Task ApplyMonthlyPayrollMenuAsync(ApplicationDbContext db)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(IsolationLevel.Serializable):null;
            if(await db.OperationRecords.AnyAsync(x=>x.UserId=="system" && x.Action==PayrollMenuAction && x.RequestId=="v1"))return;
            var employees=await db.MenuItems.FirstOrDefaultAsync(m=>m.Controller=="Accounts" && m.Action=="Employees");
            if(employees?.ParentId==null)return;
            if(!await db.MenuItems.AnyAsync(m=>m.Controller=="Accounts" && m.Action=="MonthlyPayroll"))
            {
                db.MenuItems.Add(new() { Controller="Accounts",Action="MonthlyPayroll",Name="Monthly Payroll",RequiredPermission="Accounts.View",Icon="📄",ParentId=employees.ParentId,SortOrder=employees.SortOrder+1,CreatedAt=DateTime.UtcNow });
                var slips=await db.MenuItems.FirstOrDefaultAsync(m=>m.Controller=="Accounts" && m.Action=="SalarySlips");
                if(slips!=null && slips.ParentId==employees.ParentId && slips.SortOrder<=employees.SortOrder+1)slips.SortOrder=employees.SortOrder+2;
            }
            db.OperationRecords.Add(new() { UserId="system",Action=PayrollMenuAction,RequestId="v1",CreatedUtc=DateTime.UtcNow,ResponseJson="{}" });
            await db.SaveChangesAsync();if(tx!=null)await tx.CommitAsync();
        });
    }
    private static async Task ApplyVisibilityAsync(ApplicationDbContext db)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(IsolationLevel.Serializable):null;
            if(await db.OperationRecords.AnyAsync(x=>x.UserId=="system" && x.Action==VisibilityAction && x.RequestId=="v1"))return;
            var actions=new[] { "JournalVoucher","ContraVoucher","CashBook","DayBook","OutstandingReport","AgingReport" };
            var pages=await db.MenuItems.Where(m=>m.Controller=="Accounts" && actions.Contains(m.Action!)).ToListAsync();
            if(pages.Count==0)return;
            foreach(var page in pages)page.IsActive=false;
            db.OperationRecords.Add(new() { UserId="system",Action=VisibilityAction,RequestId="v1",CreatedUtc=DateTime.UtcNow,ResponseJson="{}" });
            await db.SaveChangesAsync();if(tx!=null)await tx.CommitAsync();
        });
    }
    private static async Task ApplyFoldersAsync(ApplicationDbContext db)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(IsolationLevel.Serializable):null;
            if(await db.OperationRecords.AnyAsync(x=>x.UserId=="system" && x.Action==UpgradeAction && x.RequestId=="v1"))return;
            var rows=await db.MenuItems.ToListAsync();
            var accounts=rows.FirstOrDefault(x=>x.Id==19 && string.IsNullOrEmpty(x.Controller))
                ?? rows.FirstOrDefault(x=>x.ParentId==null && x.Name=="Accounts" && string.IsNullOrEmpty(x.Controller));
            if(accounts==null)return;
            async Task<MenuItem> Folder(string name,int order)
            {
                var folder=rows.FirstOrDefault(x=>x.ParentId==accounts.Id && x.Name==name && string.IsNullOrEmpty(x.Controller));
                if(folder==null) { folder=new() { Name=name, Icon="📁",ParentId=accounts.Id,SortOrder=order,CreatedAt=DateTime.UtcNow };db.MenuItems.Add(folder);rows.Add(folder);await db.SaveChangesAsync(); }
                return folder;
            }
            async Task Pages(MenuItem parent, params (string controller,string action,string name)[] pages)
            {
                int order=0;
                foreach(var p in pages)
                {
                    var page=rows.FirstOrDefault(x=>x.Controller==p.controller && x.Action==p.action);
                    if(page==null) { page=new() { Controller=p.controller,Action=p.action,Name=p.name,RequiredPermission="Accounts.View",Icon="📄",CreatedAt=DateTime.UtcNow };db.MenuItems.Add(page);rows.Add(page); }
                    page.ParentId=parent.Id;page.SortOrder=++order;
                }
                await db.SaveChangesAsync();
            }
            await Pages(await Folder("Vouchers",1),("Accounts","PaymentVoucher","Payment Voucher (PV)"),("Accounts","ReceiptVoucher","Receipt Voucher (RV)"),("Accounts","VoucherList","Voucher History"),("Accounts","JournalVoucher","Journal Voucher"),("Accounts","ContraVoucher","Contra Voucher"));
            await Pages(await Folder("Statements",2),("Accounts","TrialBalance","Trial Balance"),("Accounts","ProfitAndLoss","Profit & Loss"),("Accounts","BalanceSheet","Balance Sheet"),("Accounts","CashFlow","Cash Flow"),("Accounts","CashBook","Cash Book"),("Accounts","DayBook","Day Book"),("Accounts","OutstandingReport","Outstanding Report"),("Accounts","AgingReport","Aging Report"));
            await Pages(await Folder("Notes",3),("Accounts","DebitNote","Debit Note"),("Accounts","CreditNote","Credit Note"));
            await Pages(await Folder("Banking",4),("Accounts","BankReconciliation","Bank Reconciliation"),("Accounts","OpeningBalance","Opening Balance"));
            await Pages(await Folder("Ledgers",5),("Parties","Index","Customers / Suppliers"),("Accounts","PartyLedger","Party Statement"),("Accounts","GeneralLedgerView","General Ledger"),("Accounts","ChartOfAccounts","Chart of Accounts"));
            await Pages(await Folder("Employee / Payroll",6),("Accounts","Employees","Employee Data"),("Accounts","SalarySlips","Salary Slips"));
            db.OperationRecords.Add(new() { UserId="system",Action=UpgradeAction,RequestId="v1",CreatedUtc=DateTime.UtcNow,ResponseJson="{}" });
            await db.SaveChangesAsync();if(tx!=null)await tx.CommitAsync();
        });
    }
}
