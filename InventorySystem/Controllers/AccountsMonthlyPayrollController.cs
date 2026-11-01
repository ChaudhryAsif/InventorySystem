using System.Globalization;
using InventorySystem.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Controllers;

public partial class AccountsController
{
    public class PayrollLineInput
    {
        public int EmployeeId { get; set; }
        public decimal AttendanceDays { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal OvertimePay { get; set; }
        public decimal Advance { get; set; }
    }
    public class PayrollMonthInput
    {
        public int PayrollMonthId { get; set; }
        public string Period { get; set; } = "";
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public List<PayrollLineInput> Lines { get; set; } = new();
    }
    public class PayrollMonthActionInput
    {
        public int PayrollMonthId { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
    private static bool TryPeriod(string? text, out DateTime period)
        => DateTime.TryParseExact(text, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out period) && DateInRange(period);
    private static object PayrollRow(int employeeId, string name, string designation, decimal salary, decimal days, decimal hours, decimal overtimePay, decimal advance)
        => new { employeeId, name, designation, salary, attendanceDays = days, overtimeHours = hours, overtimePay, advance };

    [HttpGet] public IActionResult MonthlyPayroll() => View();

    [HttpGet] public async Task<IActionResult> GetPayrollMonth(string period)
    {
        if(!TryPeriod(period,out var month))return BadRequest(new { success=false,message="Select a valid month." });
        var saved=await _db.PayrollMonths.AsNoTracking().Include(p=>p.Lines).SingleOrDefaultAsync(p=>p.Period==month);
        var rows=new List<object>();
        if(saved?.Status=="Posted")
            rows.AddRange(saved.Lines.OrderBy(l=>l.EmployeeName).Select(l=>PayrollRow(l.EmployeeId,l.EmployeeName,l.Designation,l.MonthlySalary,l.AttendanceDays,l.OvertimeHours,l.OvertimePay,l.Advance)));
        else
        {
            // Draft salaries follow current Employee Data; active employees not yet on the sheet start at zero, like a blank client-sheet row.
            var lines=saved?.Lines.ToDictionary(l=>l.EmployeeId) ?? new Dictionary<int,PayrollLine>();
            var onSheet=lines.Keys.ToArray();
            var employees=await _db.Employees.AsNoTracking().Where(e=>e.IsActive || onSheet.Contains(e.EmployeeId)).OrderBy(e=>e.FullName).ToListAsync();
            foreach(var e in employees)
            {
                lines.TryGetValue(e.EmployeeId,out var l);
                rows.Add(PayrollRow(e.EmployeeId,e.FullName,e.Designation,e.BasicSalary,l?.AttendanceDays??0m,l?.OvertimeHours??0m,l?.OvertimePay??0m,l?.Advance??0m));
            }
        }
        var slipCount=saved==null?0:await _db.SalarySlips.CountAsync(s=>s.PayrollMonthId==saved.PayrollMonthId);
        return Json(new { success=true,payrollMonthId=saved?.PayrollMonthId??0,status=saved?.Status??"New",voucherNo=saved?.VoucherNo??"",
            voucherId=saved?.VoucherId,rowVersion=saved?.RowVersion,slipCount,rows });
    }

    [HttpPost] public async Task<IActionResult> SavePayrollMonth([FromBody] PayrollMonthInput input)
    {
        if(input==null || !TryPeriod(input.Period,out var period) || input.Lines==null || input.Lines.Count==0 || input.Lines.Count>500)
            return BadRequest(new { success=false,message="Select a month and add at least one employee row." });
        if(input.Lines.Select(l=>l.EmployeeId).Distinct().Count()!=input.Lines.Count)
            return BadRequest(new { success=false,message="An employee appears twice on this sheet." });
        var ids=input.Lines.Select(l=>l.EmployeeId).ToArray();
        var employees=await _db.Employees.Where(e=>ids.Contains(e.EmployeeId)).ToDictionaryAsync(e=>e.EmployeeId);
        if(employees.Count!=ids.Length)return BadRequest(new { success=false,message="An employee on the sheet no longer exists. Reload the month." });
        PayrollMonth? month=null;
        if(input.PayrollMonthId>0)
        {
            month=await _db.PayrollMonths.Include(p=>p.Lines).SingleOrDefaultAsync(p=>p.PayrollMonthId==input.PayrollMonthId);
            if(month==null)return NotFound(new { success=false,message="Payroll month not found." });
            if(month.Period!=period)return BadRequest(new { success=false,message="Reload the month before saving." });
            if(month.Status!="Draft")return Conflict(new { success=false,message="A posted month cannot be changed. Void it first." });
            if(!month.RowVersion.SequenceEqual(input.RowVersion ?? Array.Empty<byte>()))return Conflict(new { success=false,message="Payroll month changed. Reload before saving." });
            _db.Entry(month).Property(p=>p.RowVersion).OriginalValue=input.RowVersion!;
        }
        else if(await _db.PayrollMonths.AnyAsync(p=>p.Period==period))
            return Conflict(new { success=false,message="This month already has a payroll sheet. Reload to open it." });
        foreach(var line in input.Lines)
        {
            var e=employees[line.EmployeeId];
            var existing=month?.Lines.Any(l=>l.EmployeeId==line.EmployeeId)==true;
            if(!e.IsActive && !existing)return BadRequest(new { success=false,message=$"{e.FullName} is inactive." });
            if(line.AttendanceDays<0 || line.AttendanceDays>31 || decimal.Round(line.AttendanceDays,2)!=line.AttendanceDays
                || line.OvertimeHours<0 || line.OvertimeHours>744 || decimal.Round(line.OvertimeHours,2)!=line.OvertimeHours
                || !Money(line.OvertimePay) || !Money(line.Advance))
                return BadRequest(new { success=false,message=$"{e.FullName}: attendance must be 0 to 31 days, overtime hours 0 to 744, amounts non-negative with two decimals." });
            var total=decimal.Round(e.BasicSalary/30m*line.AttendanceDays,2)+line.OvertimePay;
            if(line.Advance>total)return BadRequest(new { success=false,message=$"{e.FullName}: advance cannot exceed the total salary." });
        }
        if(month==null){month=new PayrollMonth { Period=period };_db.PayrollMonths.Add(month);}
        month.PreparedBy=User.Identity?.Name ?? "";
        foreach(var gone in month.Lines.Where(l=>!ids.Contains(l.EmployeeId)).ToList()){month.Lines.Remove(gone);_db.PayrollLines.Remove(gone);}
        foreach(var line in input.Lines)
        {
            var e=employees[line.EmployeeId];
            var row=month.Lines.FirstOrDefault(l=>l.EmployeeId==line.EmployeeId);
            if(row==null){row=new PayrollLine { EmployeeId=e.EmployeeId };month.Lines.Add(row);}
            row.EmployeeName=e.FullName;row.Designation=e.Designation;row.MonthlySalary=e.BasicSalary;
            row.AttendanceDays=line.AttendanceDays;row.OvertimeHours=line.OvertimeHours;row.OvertimePay=line.OvertimePay;row.Advance=line.Advance;
        }
        await _db.SaveChangesAsync();
        return Json(new { success=true,message="Payroll draft saved.",payrollMonthId=month.PayrollMonthId,rowVersion=month.RowVersion });
    }

    [HttpPost] public async Task<IActionResult> PostPayrollMonth([FromBody] PayrollMonthActionInput input)
    {
        if(input==null || input.PayrollMonthId<=0)return BadRequest(new { success=false,message="Save the payroll month first." });
        var month=await _db.PayrollMonths.Include(p=>p.Lines).SingleOrDefaultAsync(p=>p.PayrollMonthId==input.PayrollMonthId);
        if(month==null)return NotFound(new { success=false,message="Payroll month not found." });
        if(month.Status!="Draft")return Conflict(new { success=false,message="This month is already posted." });
        if(!month.RowVersion.SequenceEqual(input.RowVersion ?? Array.Empty<byte>()))return Conflict(new { success=false,message="Payroll month changed. Reload before posting." });
        _db.Entry(month).Property(p=>p.RowVersion).OriginalValue=input.RowVersion!;
        var payable=month.Lines.Sum(l=>l.PayableSalary);
        if(payable<=0)return BadRequest(new { success=false,message="Total payable is zero. Enter attendance before posting." });
        var wages=await _db.AccountHeads.SingleOrDefaultAsync(a=>a.AccountCode=="5201");
        var cash=await _db.AccountHeads.SingleOrDefaultAsync(a=>a.AccountCode=="1101");
        if(wages==null || cash==null)return BadRequest(new { success=false,message="Salaries & Wages (5201) and Cash in Hand (1101) accounts are required." });
        var last=month.Period.AddMonths(1).AddDays(-1);
        var voucher=new Voucher { VoucherType="PV",VoucherDate=last>DateTime.Today?DateTime.Today:last,PaymentMode="Cash",CreatedBy=User.Identity?.Name,
            Narration=$"Labour salary {month.Period:MMMM yyyy} (cash)",
            Details=[new() { AccountHeadId=wages.AccountHeadId,Debit=payable,Credit=0,Narration="Salaries & wages" },
                new() { AccountHeadId=cash.AccountHeadId,Debit=0,Credit=payable,Narration="Cash paid" }] };
        var result=await _accounts.SaveVoucherAsync(voucher);
        if(!result.success)return BadRequest(new { success=false,message=result.message });
        month.Status="Posted";month.VoucherId=result.voucherId;month.VoucherNo=voucher.VoucherNo;month.PostedBy=User.Identity?.Name ?? "";month.PostedUtc=DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Json(new { success=true,message=$"Payroll posted as voucher {voucher.VoucherNo}.",voucherNo=voucher.VoucherNo,rowVersion=month.RowVersion });
    }

    // One issued slip per worker: basic = salary earned, allowances = overtime pay, deductions = advance, so net = payable.
    [HttpPost] public async Task<IActionResult> CreatePayrollSlips([FromBody] PayrollMonthActionInput input)
    {
        if(input==null || input.PayrollMonthId<=0)return BadRequest(new { success=false,message="Select a payroll month." });
        var month=await _db.PayrollMonths.Include(p=>p.Lines).SingleOrDefaultAsync(p=>p.PayrollMonthId==input.PayrollMonthId);
        if(month==null)return NotFound(new { success=false,message="Payroll month not found." });
        if(month.Status!="Posted")return Conflict(new { success=false,message="Post the month before creating salary slips." });
        if(await _db.SalarySlips.AnyAsync(s=>s.PayrollMonthId==month.PayrollMonthId))
            return Conflict(new { success=false,message="Salary slips were already created for this month." });
        var ids=month.Lines.Select(l=>l.EmployeeId).ToArray();
        var taken=(await _db.SalarySlips.Where(s=>s.Period==month.Period && ids.Contains(s.EmployeeId)).Select(s=>s.EmployeeId).ToListAsync()).ToHashSet();
        var codes=await _db.Employees.AsNoTracking().Where(e=>ids.Contains(e.EmployeeId)).ToDictionaryAsync(e=>e.EmployeeId,e=>e.EmployeeCode);
        var created=0;var skipped=new List<string>();
        foreach(var line in month.Lines.OrderBy(l=>l.EmployeeName))
        {
            if(line.TotalSalary<=0)continue;
            if(taken.Contains(line.EmployeeId)){skipped.Add(line.EmployeeName);continue;}
            _db.SalarySlips.Add(new SalarySlip { EmployeeId=line.EmployeeId,EmployeeCode=codes.GetValueOrDefault(line.EmployeeId,""),EmployeeName=line.EmployeeName,Designation=line.Designation,
                Period=month.Period,SlipDate=(month.PostedUtc??DateTime.UtcNow).ToLocalTime().Date,BasicSalary=line.SalaryAmount,Allowances=line.OvertimePay,Deductions=line.Advance,
                Status="Issued",IssuedUtc=DateTime.UtcNow,PreparedBy=User.Identity?.Name ?? "",PayrollMonthId=month.PayrollMonthId,
                Remarks=$"Attendance {line.AttendanceDays:0.##} days of 30, overtime {line.OvertimeHours:0.##} hrs. Allowances are overtime pay; deductions are advance." });
            created++;
        }
        if(created==0)return BadRequest(new { success=false,message=skipped.Count>0?"Every worker already has a slip for this month.":"No worker has a salary to slip." });
        await _db.SaveChangesAsync();
        return Json(new { success=true,created,message=$"{created} salary slip(s) created."+(skipped.Count>0?$" Skipped, slip already exists: {string.Join(", ",skipped)}.":"") });
    }

    [HttpPost] public async Task<IActionResult> VoidPayrollMonth([FromBody] PayrollMonthActionInput input)
    {
        if(input==null || input.PayrollMonthId<=0)return BadRequest(new { success=false,message="Select a payroll month." });
        var month=await _db.PayrollMonths.SingleOrDefaultAsync(p=>p.PayrollMonthId==input.PayrollMonthId);
        if(month==null)return NotFound(new { success=false,message="Payroll month not found." });
        if(month.Status!="Posted" || month.VoucherId==null)return Conflict(new { success=false,message="Only a posted month can be voided." });
        if(!month.RowVersion.SequenceEqual(input.RowVersion ?? Array.Empty<byte>()))return Conflict(new { success=false,message="Payroll month changed. Reload before voiding." });
        _db.Entry(month).Property(p=>p.RowVersion).OriginalValue=input.RowVersion!;
        var result=await _accounts.VoidVoucherAsync(month.VoucherId.Value);
        if(!result.success)return BadRequest(new { success=false,message=result.message });
        // Slips generated from this posting would no longer match the books.
        _db.SalarySlips.RemoveRange(await _db.SalarySlips.Where(s=>s.PayrollMonthId==month.PayrollMonthId).ToListAsync());
        month.Status="Draft";month.VoucherId=null;month.VoucherNo="";month.PostedBy="";month.PostedUtc=null;
        await _db.SaveChangesAsync();
        return Json(new { success=true,message="Payroll voucher voided. The month is a draft again.",rowVersion=month.RowVersion });
    }
}
