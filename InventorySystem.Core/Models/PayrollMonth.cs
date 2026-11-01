using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace InventorySystem.Core.Models;

// One monthly labour payroll sheet; posting creates a cash payment voucher.
public class PayrollMonth
{
    public int PayrollMonthId { get; set; }
    public DateTime Period { get; set; }
    [Required, MaxLength(15)] public string Status { get; set; } = "Draft";
    public int? VoucherId { get; set; }
    [MaxLength(20)] public string VoucherNo { get; set; } = "";
    [MaxLength(100)] public string PreparedBy { get; set; } = "";
    [MaxLength(100)] public string PostedBy { get; set; } = "";
    public DateTime? PostedUtc { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public List<PayrollLine> Lines { get; set; } = new();
}

public class PayrollLine
{
    public int PayrollLineId { get; set; }
    public int PayrollMonthId { get; set; }
    public PayrollMonth? PayrollMonth { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    [MaxLength(120)] public string EmployeeName { get; set; } = "";
    [MaxLength(100)] public string Designation { get; set; } = "";
    [Column(TypeName="decimal(18,2)")] public decimal MonthlySalary { get; set; }
    [Column(TypeName="decimal(5,2)")] public decimal AttendanceDays { get; set; }
    [Column(TypeName="decimal(7,2)")] public decimal OvertimeHours { get; set; }
    [Column(TypeName="decimal(18,2)")] public decimal OvertimePay { get; set; }
    [Column(TypeName="decimal(18,2)")] public decimal Advance { get; set; }
    // Sheet formulas: per day = salary / 30, salary = per day x attendance, total = salary + overtime.
    [NotMapped] public decimal SalaryAmount => decimal.Round(MonthlySalary/30m*AttendanceDays,2);
    [NotMapped] public decimal TotalSalary => SalaryAmount+OvertimePay;
    [NotMapped] public decimal PayableSalary => TotalSalary-Advance;
}
