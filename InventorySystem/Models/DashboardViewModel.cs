namespace InventorySystem.Models
{
    public class DashboardViewModel
    {
        // ── Stat Cards ────────────────────────────────────────────────────────
        public decimal TodaySales { get; set; }
        public decimal YesterdaySales { get; set; }
        public decimal TodayPurchases { get; set; }
        public decimal LastWeekPurchases { get; set; }
        public int TotalItems { get; set; }
        public int LowStockCount { get; set; }
        public int PendingOrders { get; set; }
        public int YesterdayPendingOrders { get; set; }

        // ── Sales Chart (last 7 days) ─────────────────────────────────────────
        public List<string> ChartLabels { get; set; } = [];
        public List<decimal> ChartSalesData { get; set; } = [];
        public List<decimal> ChartPurchaseData { get; set; } = [];

        // ── Recent Activity ───────────────────────────────────────────────────
        public List<RecentActivityItem> RecentActivities { get; set; } = [];

        // ── Top Selling Items ─────────────────────────────────────────────────
        public List<TopSellingItem> TopSellingItems { get; set; } = [];

        // ── Low Stock Alerts ──────────────────────────────────────────────────
        public List<LowStockAlert> LowStockAlerts { get; set; } = [];

        // ── Top Customers ─────────────────────────────────────────────────────
        public List<TopParty> TopCustomers { get; set; } = [];

        // ── Top Suppliers ─────────────────────────────────────────────────────
        public List<TopParty> TopSuppliers { get; set; } = [];

        // ── Helpers ───────────────────────────────────────────────────────────
        public decimal SalesChangePercent => YesterdaySales == 0 ? 0
            : Math.Round(((TodaySales - YesterdaySales) / YesterdaySales) * 100, 1);

        public decimal PurchaseChangePercent => LastWeekPurchases == 0 ? 0
            : Math.Round(((TodayPurchases - LastWeekPurchases) / LastWeekPurchases) * 100, 1);
    }

    public class RecentActivityItem
    {
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";
        public string Time { get; set; } = "";
        public string Type { get; set; } = ""; // sale, purchase, stock, production
    }

    public class TopSellingItem
    {
        public long ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal QuantitySold { get; set; }
        public decimal Revenue { get; set; }
        public decimal CurrentStock { get; set; }
    }

    public class LowStockAlert
    {
        public long ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal CurrentStock { get; set; }
    }

    public class TopParty
    {
        public string PartyName { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public int TotalInvoices { get; set; }
    }
}