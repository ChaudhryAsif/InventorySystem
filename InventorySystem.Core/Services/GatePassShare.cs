using System.Security.Cryptography;
using System.Text;
using InventorySystem.Core.Models;
namespace InventorySystem.Core.Services;

// Builds the customer's receipt link and the ready-to-send WhatsApp message for a gate pass.
public static class GatePassShare
{
    public const int ValidDays = 90;
    public const string CompanyName = "B. Packages";

    // 32 random bytes, URL-safe: unguessable, so the link itself is the key.
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static bool LooksLikeToken(string? token) => token is { Length: >= 20 and <= 64 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    // Digits for wa.me. 0300... becomes 92300...; anything too short returns null so WhatsApp asks for the contact.
    public static string? WhatsAppPhone(string? raw)
    {
        var digits = new string((raw ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("00")) digits = digits[2..];
        if (digits.StartsWith('0')) digits = "92" + digits[1..];
        if (digits.Length == 10 && digits.StartsWith('3')) digits = "92" + digits;
        return digits.Length is >= 10 and <= 15 ? digits : null;
    }

    public static string Message(GatePass pass, string url, string company = CompanyName)
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
        sb.AppendLine();
        sb.AppendLine("View and download the receipt:");
        sb.Append(url);
        return sb.ToString();
    }

    public static string WhatsAppUrl(string? phone, string message)
        => "https://wa.me/" + (WhatsAppPhone(phone) ?? "") + "?text=" + Uri.EscapeDataString(message);
}
