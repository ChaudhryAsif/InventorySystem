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
public class ExpenseTests
{
    private static ApplicationDbContext Db(bool seed=true)
    {var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w=>w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);if(seed)db.Database.EnsureCreated();return db;}
    private static AccountsController Controller(ApplicationDbContext db)=>new(db,new AccountService(db)) { ControllerContext=new() { HttpContext=new DefaultHttpContext { User=new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name,"Expense QA") },"Test")) } } };
    private static int Cat(ApplicationDbContext db,string code)=>db.AccountHeads.Single(a=>a.AccountCode==code).AccountHeadId;
    private static AccountsController.ExpenseInput Input(int category,decimal amount=500,string text="Petrol")=>new() { ExpenseDate=DateTime.Today,AccountHeadId=category,Description=text,Amount=amount };
    [Fact] public async Task SavingPostsCategoryDebitAndCashCredit()
    {
        using var db=Db();var c=Controller(db);Assert.IsType<JsonResult>(await c.SaveExpense(Input(Cat(db,"5205"))));
        var entry=await db.ExpenseEntries.SingleAsync();Assert.Equal("Posted",entry.Status);Assert.Equal(500m,entry.Amount);
        var voucher=await db.Vouchers.Include(v=>v.Details).ThenInclude(d=>d.AccountHead).SingleAsync();
        Assert.Equal("5205",voucher.Details.Single(d=>d.Debit>0).AccountHead!.AccountCode);Assert.Equal("1101",voucher.Details.Single(d=>d.Credit>0).AccountHead!.AccountCode);
    }
    [Fact] public async Task OnlyCashExpenseCategoriesAreOffered()
    {
        using var db=Db();var c=Controller(db);
        var codes=JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(await c.GetExpenseCategories()).Value).GetProperty("data").EnumerateArray().Select(x=>x.GetProperty("AccountCode").GetString()).ToList();
        Assert.Contains("5205",codes);Assert.Contains("5206",codes);Assert.Contains("5207",codes);Assert.Contains("5203",codes);Assert.Contains("5103",codes);
        foreach(var excluded in new[] { "5101","5102","5201","5204","5301","5302","1101","4101" })Assert.DoesNotContain(excluded,codes);
        Assert.IsType<BadRequestObjectResult>(await c.SaveExpense(Input(Cat(db,"5201"))));
        Assert.IsType<BadRequestObjectResult>(await c.SaveExpense(Input(Cat(db,"1101"))));
        Assert.Empty(await db.ExpenseEntries.ToListAsync());
    }
    [Fact] public async Task InvalidExpensesAreRejected()
    {
        using var db=Db();var c=Controller(db);var cat=Cat(db,"5205");
        Assert.IsType<BadRequestObjectResult>(await c.SaveExpense(Input(cat,0)));
        Assert.IsType<BadRequestObjectResult>(await c.SaveExpense(Input(cat,10.005m)));
        Assert.IsType<BadRequestObjectResult>(await c.SaveExpense(Input(cat,text:"  ")));
        var future=Input(cat);future.ExpenseDate=DateTime.Today.AddDays(2);Assert.IsType<BadRequestObjectResult>(await c.SaveExpense(future));
        Assert.Empty(await db.Vouchers.ToListAsync());
    }
    [Fact] public async Task VoidCancelsTheVoucherAndHidesTheRowByDefault()
    {
        using var db=Db();var c=Controller(db);var cat=Cat(db,"5205");await c.SaveExpense(Input(cat));await c.SaveExpense(Input(cat,200,"Tea"));
        var entry=await db.ExpenseEntries.OrderBy(e=>e.ExpenseEntryId).FirstAsync();
        Assert.IsType<JsonResult>(await c.VoidExpense(new() { ExpenseEntryId=entry.ExpenseEntryId,RowVersion=entry.RowVersion }));
        Assert.IsType<ConflictObjectResult>(await c.VoidExpense(new() { ExpenseEntryId=entry.ExpenseEntryId,RowVersion=entry.RowVersion }));
        Assert.True((await db.Vouchers.FindAsync(entry.VoucherId))!.IsVoid);
        var shown=JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(await c.GetExpenses()).Value);
        Assert.Single(shown.GetProperty("data").EnumerateArray());Assert.Equal(200m,shown.GetProperty("total").GetDecimal());
        var voided=JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(await c.GetExpenses(voidedOnly:true)).Value);
        Assert.Single(voided.GetProperty("data").EnumerateArray());Assert.Equal("Void",voided.GetProperty("data")[0].GetProperty("Status").GetString());Assert.Equal(entry.Amount,voided.GetProperty("total").GetDecimal());
    }
    [Fact] public async Task FiltersByMonthCategoryAndSearch()
    {
        using var db=Db();var c=Controller(db);var a=Cat(db,"5205");var b=Cat(db,"5207");await c.SaveExpense(Input(a,100,"Petrol"));await c.SaveExpense(Input(b,300,"Daig"));
        int Count(object? v)=>JsonSerializer.SerializeToElement(v).GetProperty("data").GetArrayLength();
        Assert.Equal(2,Count(Assert.IsType<JsonResult>(await c.GetExpenses(DateTime.Today.ToString("yyyy-MM"))).Value));
        Assert.Equal(0,Count(Assert.IsType<JsonResult>(await c.GetExpenses(DateTime.Today.AddMonths(-1).ToString("yyyy-MM"))).Value));
        Assert.Equal(1,Count(Assert.IsType<JsonResult>(await c.GetExpenses(categoryId:b)).Value));
        Assert.Equal(1,Count(Assert.IsType<JsonResult>(await c.GetExpenses(search:"Petr")).Value));
        Assert.IsType<BadRequestObjectResult>(await c.GetExpenses("bad"));
    }
    [Fact] public async Task MenuPageIsAddedOnceBesidePaymentVoucher()
    {
        using var db=Db(false);db.MenuItems.AddRange(LegacyMenuSeed.Create());await db.SaveChangesAsync();await AccountsFolderSeeder.ApplyAsync(db);
        var page=await db.MenuItems.SingleAsync(m=>m.Controller=="Accounts" && m.Action=="Expenses");
        Assert.Equal((await db.MenuItems.SingleAsync(m=>m.Action=="PaymentVoucher")).ParentId,page.ParentId);
        await AccountsFolderSeeder.ApplyAsync(db);Assert.Equal(1,await db.MenuItems.CountAsync(m=>m.Action=="Expenses"));
    }
}
