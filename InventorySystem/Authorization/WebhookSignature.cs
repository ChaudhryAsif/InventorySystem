using System.Security.Cryptography;
using System.Text;
namespace InventorySystem.Authorization;
public static class WebhookSignature
{
    public static bool IsValid(string body, string signature, string secret)
    {
        if (string.IsNullOrEmpty(secret) || !signature.StartsWith("sha256=", StringComparison.Ordinal)) return false;
        try
        {
            var supplied = Convert.FromHexString(signature[7..]);
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
            return CryptographicOperations.FixedTimeEquals(supplied, expected);
        }
        catch (FormatException) { return false; }
    }
}
