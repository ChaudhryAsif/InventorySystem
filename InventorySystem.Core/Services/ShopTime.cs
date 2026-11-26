namespace InventorySystem.Core.Services;

// The shop works on Pakistan time, but the web server's own clock may be set elsewhere (the host is in Europe).
public static class ShopTime
{
    public static DateTime FromUtc(DateTime utc)
    {
        var value = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        foreach (var id in new[] { "Pakistan Standard Time", "Asia/Karachi" })
            try { return TimeZoneInfo.ConvertTimeFromUtc(value, TimeZoneInfo.FindSystemTimeZoneById(id)); } catch (TimeZoneNotFoundException) { }
        return value.AddHours(5);   // Pakistan is UTC+5 all year
    }
}
