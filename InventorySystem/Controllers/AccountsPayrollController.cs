using System.ComponentModel.DataAnnotations;
using InventorySystem.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Controllers;

public partial class AccountsController
{
    [HttpGet] public IActionResult Employees() => View();
    [HttpGet] public IActionResult SalarySlips() => View();
    [HttpGet] public async Task<IActionResult> GetEmployees(string? search=null, bool activeOnly=false)
    {
        var query=_db.Employees.AsNoTracking().AsQueryable();
        if(activeOnly)query=query.Where(e=>e.IsActive);
        if(!string.IsNullOrWhiteSpace(search))query=query.Where(e=>e.FullName.Contains(search) || e.EmployeeCode.Contains(search));
        return Json(new { success=true,data=await query.OrderBy(e=>e.FullName).ToListAsync() });
    }
    private static bool Money(decimal value) => value>=0 && value<=1000000000m && decimal.Round(value,2)==value;
    private static bool DateInRange(DateTime date) => date.Year>=1900 && date.Year<=2100;
    [HttpPost] public async Task<IActionResult> SaveEmployee([FromBody] Employee input)
    {
        if(input==null || input.EmployeeId<0 || !ModelState.IsValid || string.IsNullOrWhiteSpace(input.EmployeeCode) || string.IsNullOrWhiteSpace(input.FullName)
            || input.EmployeeCode.Trim().Length>30 || input.FullName.Trim().Length>120 || input.Designation?.Length>100 || input.Phone?.Length>40
            || !Money(input.BasicSalary) || !DateInRange(input.JoiningDate))
            return BadRequest(new { success=false,message="Enter employee code, name, a valid joining date and a non-negative salary with at most two decimal places." });
        var code=input.EmployeeCode.Trim().ToUpperInvariant();
        if(await _db.Employees.AnyAsync(e=>e.EmployeeCode==code && e.EmployeeId!=input.EmployeeId))
            return Conflict(new { success=false,message="This employee code already exists." });
        Employee employee;
        if(input.EmployeeId>0)
        {
            employee=await _db.Employees.FindAsync(input.EmployeeId) ?? new Employee();
            if(employee.EmployeeId==0)return NotFound();
            if(!employee.RowVersion.SequenceEqual(input.RowVersion ?? Array.Empty<byte>()))return Conflict(new { success=false,message="Employee changed. Reload before saving." });
            _db.Entry(employee).Property(e=>e.RowVersion).OriginalValue=input.RowVersion!;
        }
        else { employee=new();_db.Employees.Add(employee); }
        employee.EmployeeCode=code;employee.FullName=input.FullName.Trim();employee.Designation=input.Designation?.Trim()??"";
        employee.Phone=input.Phone?.Trim()??"";employee.BasicSalary=input.BasicSalary;employee.JoiningDate=input.JoiningDate.Date;employee.IsActive=input.IsActive;
        await _db.SaveChangesAsync();return Json(new { success=true,data=employee,message="Employee saved." });
    }
    public class SalarySlipInput
    {
        public int SalarySlipId { get; set; }
        public int EmployeeId { get; set; }
        public DateTime Period { get; set; }
        public DateTime SlipDate { get; set; }
        public decimal BasicSalary { get; set; }
        public decimal Allowances { get; set; }
        public decimal Deductions { get; set; }
        [MaxLength(500)] public string Remarks { get; set; } = "";
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
    public class SalaryIssueInput
    {
        public int SalarySlipId { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
    [HttpGet] public async Task<IActionResult> GetSalarySlips(int? employeeId=null,int? year=null,int? month=null)
    {
        var query=_db.SalarySlips.AsNoTracking().AsQueryable();
        if(employeeId>0)query=query.Where(s=>s.EmployeeId==employeeId);
        if(year.HasValue)query=query.Where(s=>s.Period.Year==year.Value);
        if(month is>=1 and <=12)query=query.Where(s=>s.Period.Month==month.Value);
        return Json(new { success=true,data=await query.OrderByDescending(s=>s.Period).ThenBy(s=>s.EmployeeName).ToListAsync() });
    }
    [HttpPost] public async Task<IActionResult> SaveSalarySlip([FromBody] SalarySlipInput input)
    {
        if(input==null || input.SalarySlipId<0 || input.EmployeeId<=0 || !ModelState.IsValid || !DateInRange(input.Period) || !DateInRange(input.SlipDate) || !Money(input.BasicSalary)
            || !Money(input.Allowances) || !Money(input.Deductions) || input.Deductions>input.BasicSalary+input.Allowances || input.Remarks?.Length>500)
            return BadRequest(new { success=false,message="Enter a valid month/date and non-negative amounts with at most two decimal places. Deductions cannot exceed gross salary." });
        var employee=await _db.Employees.FindAsync(input.EmployeeId);
        if(employee==null || !employee.IsActive)return BadRequest(new { success=false,message="Select an active employee." });
        var period=new DateTime(input.Period.Year,input.Period.Month,1);
        if(await _db.SalarySlips.AnyAsync(s=>s.EmployeeId==input.EmployeeId && s.Period==period && s.SalarySlipId!=input.SalarySlipId))
            return Conflict(new { success=false,message="A salary slip already exists for this employee and month. Open that slip instead." });
        SalarySlip slip;
        if(input.SalarySlipId>0)
        {
            slip=await _db.SalarySlips.FindAsync(input.SalarySlipId) ?? new SalarySlip();
            if(slip.SalarySlipId==0)return NotFound();
            if(slip.Status!="Draft")return Conflict(new { success=false,message="Issued salary slips cannot be changed." });
            if(!slip.RowVersion.SequenceEqual(input.RowVersion ?? Array.Empty<byte>()))return Conflict(new { success=false,message="Salary slip changed. Reload before saving." });
            _db.Entry(slip).Property(s=>s.RowVersion).OriginalValue=input.RowVersion!;
        }
        else { slip=new();_db.SalarySlips.Add(slip); }
        slip.EmployeeId=employee.EmployeeId;slip.EmployeeCode=employee.EmployeeCode;slip.EmployeeName=employee.FullName;slip.Designation=employee.Designation;
        slip.Period=period;slip.SlipDate=input.SlipDate.Date;slip.BasicSalary=input.BasicSalary;slip.Allowances=input.Allowances;slip.Deductions=input.Deductions;
        slip.Remarks=input.Remarks?.Trim()??"";slip.PreparedBy=User.Identity?.Name??"";
        await _db.SaveChangesAsync();return Json(new { success=true,data=slip,message="Draft salary slip saved." });
    }
    [HttpPost] public async Task<IActionResult> UpdateSalarySlipStatus([FromBody] SalaryIssueInput input)
    {
        if(input==null || input.SalarySlipId<=0)return BadRequest(new { success=false,message="Select a salary slip." });
        var slip=await _db.SalarySlips.FindAsync(input.SalarySlipId);
        if(slip==null)return NotFound();
        if(slip.Status!="Draft")return Conflict(new { success=false,message="This salary slip has already been issued." });
        if(!slip.RowVersion.SequenceEqual(input.RowVersion ?? Array.Empty<byte>()))return Conflict(new { success=false,message="Salary slip changed. Reload before issuing." });
        _db.Entry(slip).Property(s=>s.RowVersion).OriginalValue=input.RowVersion!;
        slip.Status="Issued";slip.IssuedUtc=DateTime.UtcNow;
        await _db.SaveChangesAsync();return Json(new { success=true,data=slip,message="Salary slip issued." });
    }
    [HttpGet] public async Task<IActionResult> PrintSalarySlip(int id)
    {
        var slip=await _db.SalarySlips.AsNoTracking().SingleOrDefaultAsync(s=>s.SalarySlipId==id);
        return slip==null?NotFound():View("PrintSalarySlip",slip);
    }
    public class EmployeeDeleteInput
    {
        public int EmployeeId { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
    [HttpPost] public async Task<IActionResult> DeleteEmployee([FromBody] EmployeeDeleteInput input)
    {
        if(input==null || input.EmployeeId<=0)return BadRequest(new { success=false,message="Select an employee." });
        var employee=await _db.Employees.FindAsync(input.EmployeeId);
        if(employee==null)return NotFound();
        if(!employee.RowVersion.SequenceEqual(input.RowVersion??Array.Empty<byte>()))return Conflict(new { success=false,message="Employee changed. Reload before deleting." });
        if(await _db.SalarySlips.AnyAsync(s=>s.EmployeeId==input.EmployeeId))return Conflict(new { success=false,message="This employee has salary history and cannot be deleted. Set the employee to Inactive instead." });
        if(await _db.PayrollLines.AnyAsync(l=>l.EmployeeId==input.EmployeeId))return Conflict(new { success=false,message="This employee is on a monthly payroll sheet and cannot be deleted. Set the employee to Inactive instead." });
        _db.Entry(employee).Property(e=>e.RowVersion).OriginalValue=input.RowVersion!;
        _db.Employees.Remove(employee);await _db.SaveChangesAsync();return Json(new { success=true,message="Employee deleted." });
    }
    [HttpPost] public async Task<IActionResult> DeleteSalarySlip([FromBody] SalaryIssueInput input)
    {
        if(input==null || input.SalarySlipId<=0)return BadRequest(new { success=false,message="Select a salary slip." });
        var slip=await _db.SalarySlips.FindAsync(input.SalarySlipId);
        if(slip==null)return NotFound();
        if(slip.Status!="Draft")return Conflict(new { success=false,message="Issued salary slips cannot be deleted." });
        if(!slip.RowVersion.SequenceEqual(input.RowVersion??Array.Empty<byte>()))return Conflict(new { success=false,message="Salary slip changed. Reload before deleting." });
        _db.Entry(slip).Property(s=>s.RowVersion).OriginalValue=input.RowVersion!;
        _db.SalarySlips.Remove(slip);await _db.SaveChangesAsync();return Json(new { success=true,message="Draft salary slip deleted." });
    }

}
