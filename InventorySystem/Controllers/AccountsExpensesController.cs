using System.Globalization;
using InventorySystem.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Controllers;

public partial class AccountsController
{
    public class ExpenseInput
    {
        public DateTime ExpenseDate { get; set; }
        public int AccountHeadId { get; set; }
        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
    }
    public class ExpenseVoidInput
    {
        public int ExpenseEntryId { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
    // Purchases, salaries (Monthly Payroll), depreciation and bank/discount lines are not cash expenses.
    private static readonly string[] NonExpenseCodes = ["5101","5102","5201","5204","5301","5302"];
    private IQueryable<AccountHead> ExpenseCategories()
        => _db.AccountHeads.Where(a=>a.Level==3 && a.IsActive && a.AccountType=="Expenses" && !NonExpenseCodes.Contains(a.AccountCode));

    [HttpGet] public IActionResult Expenses() => View();

    [HttpGet] public async Task<IActionResult> GetExpenseCategories()
        => Json(new { success=true,data=await ExpenseCategories().AsNoTracking().OrderBy(a=>a.AccountName).Select(a=>new { a.AccountHeadId,a.AccountCode,a.AccountName }).ToListAsync() });

    [HttpGet] public async Task<IActionResult> GetExpenses(string? month=null,int? categoryId=null,string? search=null,bool voidedOnly=false)
    {
        var query=_db.ExpenseEntries.AsNoTracking().AsQueryable();
        if(!string.IsNullOrWhiteSpace(month))
        {
            if(!DateTime.TryParseExact(month,"yyyy-MM",CultureInfo.InvariantCulture,DateTimeStyles.None,out var from) || !DateInRange(from))
                return BadRequest(new { success=false,message="Select a valid month." });
            var to=from.AddMonths(1);query=query.Where(e=>e.ExpenseDate>=from && e.ExpenseDate<to);
        }
        if(categoryId>0)query=query.Where(e=>e.AccountHeadId==categoryId);
        if(!string.IsNullOrWhiteSpace(search)){var s=search.Trim();query=query.Where(e=>e.Description.Contains(s) || e.VoucherNo.Contains(s));}
        var status=voidedOnly?"Void":"Posted";
        query=query.Where(e=>e.Status==status);
        var rows=await query.OrderByDescending(e=>e.ExpenseDate).ThenByDescending(e=>e.ExpenseEntryId).Take(1000)
            .Select(e=>new { e.ExpenseEntryId,e.ExpenseDate,e.AccountHeadId,category=e.AccountHead!.AccountName,e.Description,e.Amount,e.VoucherNo,e.Status,e.CreatedBy,e.RowVersion }).ToListAsync();
        return Json(new { success=true,data=rows,total=rows.Sum(r=>r.Amount) });
    }

    [HttpPost] public async Task<IActionResult> SaveExpense([FromBody] ExpenseInput input)
    {
        var description=input?.Description?.Trim() ?? "";
        if(input==null || description.Length==0 || description.Length>200 || input.Amount<=0 || !Money(input.Amount) || input.Amount>100000000m
            || !DateInRange(input.ExpenseDate) || input.ExpenseDate.Date>DateTime.Today)
            return BadRequest(new { success=false,message="Enter a date not in the future, a description (up to 200 characters) and an amount above zero with at most two decimals." });
        var category=await ExpenseCategories().SingleOrDefaultAsync(a=>a.AccountHeadId==input.AccountHeadId);
        if(category==null)return BadRequest(new { success=false,message="Select an expense category." });
        var cash=await _db.AccountHeads.SingleOrDefaultAsync(a=>a.AccountCode=="1101");
        if(cash==null)return BadRequest(new { success=false,message="Cash in Hand (1101) account is required." });
        var voucher=new Voucher { VoucherType="PV",VoucherDate=input.ExpenseDate.Date,PaymentMode="Cash",CreatedBy=User.Identity?.Name,
            Narration=$"{category.AccountName}: {description}",
            Details=[new() { AccountHeadId=category.AccountHeadId,Debit=input.Amount,Credit=0,Narration=description },
                new() { AccountHeadId=cash.AccountHeadId,Debit=0,Credit=input.Amount,Narration="Cash paid" }] };
        var result=await _accounts.SaveVoucherAsync(voucher);
        if(!result.success)return BadRequest(new { success=false,message=result.message });
        var entry=new ExpenseEntry { ExpenseDate=input.ExpenseDate.Date,AccountHeadId=category.AccountHeadId,Description=description,Amount=input.Amount,
            VoucherId=result.voucherId,VoucherNo=voucher.VoucherNo,CreatedBy=User.Identity?.Name ?? "" };
        _db.ExpenseEntries.Add(entry);await _db.SaveChangesAsync();
        return Json(new { success=true,message=$"Expense saved as voucher {voucher.VoucherNo}.",id=entry.ExpenseEntryId });
    }

    [HttpPost] public async Task<IActionResult> VoidExpense([FromBody] ExpenseVoidInput input)
    {
        if(input==null || input.ExpenseEntryId<=0)return BadRequest(new { success=false,message="Select an expense." });
        var entry=await _db.ExpenseEntries.SingleOrDefaultAsync(e=>e.ExpenseEntryId==input.ExpenseEntryId);
        if(entry==null)return NotFound(new { success=false,message="Expense not found." });
        if(entry.Status!="Posted" || entry.VoucherId==null)return Conflict(new { success=false,message="This expense is already void." });
        if(!entry.RowVersion.SequenceEqual(input.RowVersion ?? Array.Empty<byte>()))return Conflict(new { success=false,message="Expense changed. Reload before voiding." });
        _db.Entry(entry).Property(e=>e.RowVersion).OriginalValue=input.RowVersion!;
        var result=await _accounts.VoidVoucherAsync(entry.VoucherId.Value);
        if(!result.success)return BadRequest(new { success=false,message=result.message });
        entry.Status="Void";await _db.SaveChangesAsync();
        return Json(new { success=true,message="Expense voided and its voucher cancelled." });
    }
}
