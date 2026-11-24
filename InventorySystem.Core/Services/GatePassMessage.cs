using System.Text;
using InventorySystem.Core.Models;
namespace InventorySystem.Core.Services;

// The short text that goes under the PDF in the WhatsApp group.
public static class GatePassMessage
{
    public const string CompanyName = "B. Packages";

    public static string Caption(GatePass pass, string company = CompanyName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{company}: {pass.Direction} Gate Pass {pass.PassNumber}");
        sb.AppendLine($"Date: {pass.PassDate:dd-MMM-yyyy}");
        sb.AppendLine($"Party: {pass.PartyName}");
        if (!string.IsNullOrWhiteSpace(pass.VehicleNumber)) sb.AppendLine($"Vehicle: {pass.VehicleNumber}" + (string.IsNullOrWhiteSpace(pass.DriverName) ? "" : $" ({pass.DriverName})"));
        var lines = pass.Lines.OrderBy(l => l.SerialNo).ToList();
        if (lines.Count > 0)
        {
            sb.AppendLine("Items:");
            foreach (var l in lines.Take(8)) sb.AppendLine($"- {l.Description}: {l.TotalQuantity:0.###} {l.Unit.ToUpperInvariant()}");
            if (lines.Count > 8) sb.AppendLine($"- and {lines.Count - 8} more");
        }
        return sb.ToString().TrimEnd();
    }
}
