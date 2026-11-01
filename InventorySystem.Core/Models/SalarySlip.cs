using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace InventorySystem.Core.Models;
public class SalarySlip
{
    public int SalarySlipId { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateTime Period { get; set; }
    public DateTime SlipDate { get; set; }
    [MaxLength(120)] public string EmployeeName { get; set; } = "";
    [MaxLength(30)] public string EmployeeCode { get; set; } = "";
    [MaxLength(100)] public string Designation { get; set; } = "";
    [Column(TypeName="decimal(18,2)")] public decimal BasicSalary { get; set; }
    [Column(TypeName="decimal(18,2)")] public decimal Allowances { get; set; }
    [Column(TypeName="decimal(18,2)")] public decimal Deductions { get; set; }
    [NotMapped] public decimal NetSalary => BasicSalary+Allowances-Deductions;
    [Required, MaxLength(15)] public string Status { get; set; } = "Draft";
    [MaxLength(500)] public string Remarks { get; set; } = "";
    [MaxLength(100)] public string PreparedBy { get; set; } = "";
    public DateTime? IssuedUtc { get; set; }
    // Set when the slip was generated from a posted Monthly Payroll sheet.
    public int? PayrollMonthId { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
