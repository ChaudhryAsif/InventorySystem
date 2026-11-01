using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace InventorySystem.Core.Models;
public class Employee
{
    public int EmployeeId { get; set; }
    [Required, MaxLength(30)] public string EmployeeCode { get; set; } = "";
    [Required, MaxLength(120)] public string FullName { get; set; } = "";
    [MaxLength(100)] public string Designation { get; set; } = "";
    [MaxLength(40)] public string Phone { get; set; } = "";
    public DateTime JoiningDate { get; set; } = DateTime.Today;
    [Column(TypeName="decimal(18,2)")] public decimal BasicSalary { get; set; }
    public bool IsActive { get; set; } = true;
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
