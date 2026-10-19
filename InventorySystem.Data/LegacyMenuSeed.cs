using InventorySystem.Core.Models;

namespace InventorySystem.Data;

// Frozen historical seed: changing HasData would overwrite user-managed menus on upgrades.
public static class LegacyMenuSeed
{
    public static MenuItem[] Create()
    {
        return new MenuItem[]
            {
                new MenuItem { Id = 1, Name = "Dashboard", Icon = "📊", Controller = "Home", Action = "Index", SortOrder = 1, RequiredPermission = "Dashboard.View" },
                new MenuItem { Id = 2, Name = "Item Category", Icon = "📦", Controller = "Category", Action = "Index", SortOrder = 2, RequiredPermission = "ItemCategory.View" },
                new MenuItem { Id = 3, Name = "Item Management", Icon = "🏷️", Controller = "Item", Action = "Index", SortOrder = 3, RequiredPermission = "Item.View" },
                new MenuItem { Id = 4, Name = "Product List", Icon = "💰", Controller = "ItemRateList", Action = "Index", SortOrder = 4, RequiredPermission = "Item.View" },
                new MenuItem { Id = 5, Name = "Parties", Icon = "👥", Controller = "Parties", Action = "Index", SortOrder = 5, RequiredPermission = "Item.View" },
                new MenuItem { Id = 6, Name = "Purchase Invoice", Icon = "📝", Controller = "Purchase", Action = "Index", SortOrder = 6, RequiredPermission = "Purchase.View" },
                new MenuItem { Id = 7, Name = "Sale Invoice", Icon = "🧾", Controller = "Sale", Action = "Index", SortOrder = 7, RequiredPermission = "Sale.View" },
                new MenuItem { Id = 8, Name = "Raw Material Consume", Icon = "🏭", Controller = "Consume", Action = "Index", SortOrder = 8, RequiredPermission = "Item.View" },
                new MenuItem { Id = 9, Name = "Stock Adjustment", Icon = "📦", SortOrder = 9, RequiredPermission = "StockAdjustment.View" },
                new MenuItem { Id = 10, Name = "All Adjustments", Icon = "📋", Controller = "StockAdjustment", Action = "Index", ParentId = 9, SortOrder = 1, RequiredPermission = "StockAdjustment.View" },
                new MenuItem { Id = 11, Name = "New Adjustment", Icon = "➕", Controller = "StockAdjustment", Action = "Create", ParentId = 9, SortOrder = 2, RequiredPermission = "StockAdjustment.Create" },
                new MenuItem { Id = 12, Name = "Cost Sheet", Icon = "📦", SortOrder = 10, RequiredPermission = "CostSheet.View" },
                new MenuItem { Id = 13, Name = "All Cost Sheets", Icon = "📋", Controller = "CostSheet", Action = "Index", ParentId = 12, SortOrder = 1, RequiredPermission = "CostSheet.View" },
                new MenuItem { Id = 14, Name = "New Cost Sheet", Icon = "➕", Controller = "CostSheet", Action = "Create", ParentId = 12, SortOrder = 2, RequiredPermission = "CostSheet.Create" },
                new MenuItem { Id = 15, Name = "Settings", Icon = "⚙️", Controller = "CostSheet", Action = "Settings", ParentId = 12, SortOrder = 3, RequiredPermission = "CostSheet.Settings" },
                new MenuItem { Id = 16, Name = "Production", Icon = "🏭", SortOrder = 11, RequiredPermission = "Production.View" },
                new MenuItem { Id = 17, Name = "All Orders", Icon = "📋", Controller = "Production", Action = "Index", ParentId = 16, SortOrder = 1, RequiredPermission = "Production.View" },
                new MenuItem { Id = 18, Name = "New Order", Icon = "➕", Controller = "Production", Action = "Create", ParentId = 16, SortOrder = 2, RequiredPermission = "Production.Create" },

                // Add to the menuItems array inside SeedMenuItems():
                new MenuItem { Id = 19, Name = "Accounts",           Icon = "💰", SortOrder = 12, RequiredPermission = "Accounts.View" },
                new MenuItem { Id = 20, Name = "Chart of Accounts",  Icon = "📒", Controller = "Accounts", Action = "ChartOfAccounts", ParentId = 19, SortOrder = 1, RequiredPermission = "Accounts.View" },
                new MenuItem { Id = 21, Name = "Payment Voucher",    Icon = "💳", Controller = "Accounts", Action = "PaymentVoucher",  ParentId = 19, SortOrder = 2, RequiredPermission = "Accounts.Create" },
                new MenuItem { Id = 22, Name = "Receipt Voucher",    Icon = "🧾", Controller = "Accounts", Action = "ReceiptVoucher",  ParentId = 19, SortOrder = 3, RequiredPermission = "Accounts.Create" },
                new MenuItem { Id = 23, Name = "Journal Voucher",    Icon = "📓", Controller = "Accounts", Action = "JournalVoucher",  ParentId = 19, SortOrder = 4, RequiredPermission = "Accounts.Create" },
                new MenuItem { Id = 24, Name = "Contra Voucher",     Icon = "🏦", Controller = "Accounts", Action = "ContraVoucher",   ParentId = 19, SortOrder = 5, RequiredPermission = "Accounts.Create" },
                new MenuItem { Id = 25, Name = "Voucher List",       Icon = "📋", Controller = "Accounts", Action = "VoucherList",     ParentId = 19, SortOrder = 6, RequiredPermission = "Accounts.View" },
                new MenuItem { Id = 26, Name = "General Ledger",     Icon = "📖", Controller = "Accounts", Action = "GeneralLedgerView", ParentId = 19, SortOrder = 7, RequiredPermission = "Accounts.View" },
                new MenuItem { Id = 27, Name = "Party Statement",    Icon = "👤", Controller = "Accounts", Action = "PartyLedger",     ParentId = 19, SortOrder = 8, RequiredPermission = "Accounts.View" },
                new MenuItem { Id = 28, Name = "Trial Balance",      Icon = "⚖️", Controller = "Accounts", Action = "TrialBalance",    ParentId = 19, SortOrder = 9, RequiredPermission = "Accounts.View" },
                new MenuItem { Id = 29, Name = "Profit & Loss",      Icon = "📈", Controller = "Accounts", Action = "ProfitAndLoss",   ParentId = 19, SortOrder = 10, RequiredPermission = "Accounts.View" },
                new MenuItem { Id = 30, Name = "Balance Sheet",      Icon = "🏦", Controller = "Accounts", Action = "BalanceSheet",    ParentId = 19, SortOrder = 11, RequiredPermission = "Accounts.View" },
                
                // Reports
                new MenuItem { Id = 31, Name = "Reports",          Icon = "📊", SortOrder = 13, RequiredPermission = "Report.View" },
                new MenuItem { Id = 32, Name = "Stock Report",     Icon = "📦", Controller = "Report", Action = "StockReport",    ParentId = 31, SortOrder = 1, RequiredPermission = "Report.View" },
                new MenuItem { Id = 33, Name = "Purchase Report",  Icon = "📝", Controller = "Report", Action = "PurchaseReport", ParentId = 31, SortOrder = 2, RequiredPermission = "Report.View" },
                new MenuItem { Id = 34, Name = "Sale Report",      Icon = "🧾", Controller = "Report", Action = "SaleReport",     ParentId = 31, SortOrder = 3, RequiredPermission = "Report.View" },
                new MenuItem { Id = 35, Name = "Consume Report",   Icon = "🏭", Controller = "Report", Action = "ConsumeReport",  ParentId = 31, SortOrder = 4, RequiredPermission = "Report.View" },

                // New Accounts sub-menu items
                new MenuItem { Id = 36, Name = "Cash Book",          Icon = "💵", Controller = "Accounts", Action = "CashBook",          ParentId = 19, SortOrder = 12, RequiredPermission = "Accounts.View" },
                new MenuItem { Id = 37, Name = "Cash Flow",          Icon = "💧", Controller = "Accounts", Action = "CashFlow",          ParentId = 19, SortOrder = 13, RequiredPermission = "Accounts.View" },
                new MenuItem { Id = 38, Name = "Aging Report",       Icon = "📅", Controller = "Accounts", Action = "AgingReport",       ParentId = 19, SortOrder = 14, RequiredPermission = "Accounts.View" },
                new MenuItem { Id = 39, Name = "Outstanding Report", Icon = "📋", Controller = "Accounts", Action = "OutstandingReport", ParentId = 19, SortOrder = 15, RequiredPermission = "Accounts.View" },

                // Day Book and Bank Reconciliation
                new MenuItem { Id = 40, Name = "Day Book",            Icon = "📅", Controller = "Accounts", Action = "DayBook",           ParentId = 19, SortOrder = 16, RequiredPermission = "Accounts.View"   },
                new MenuItem { Id = 41, Name = "Bank Reconciliation", Icon = "🏦", Controller = "Accounts", Action = "BankReconciliation", ParentId = 19, SortOrder = 17, RequiredPermission = "Accounts.View"   },
                new MenuItem { Id = 42, Name = "Debit Note",          Icon = "📉", Controller = "Accounts", Action = "DebitNote",          ParentId = 19, SortOrder = 18, RequiredPermission = "Accounts.Create" },
                new MenuItem { Id = 43, Name = "Credit Note",         Icon = "📈", Controller = "Accounts", Action = "CreditNote",         ParentId = 19, SortOrder = 19, RequiredPermission = "Accounts.Create" },
                new MenuItem { Id = 44, Name = "Opening Balance",     Icon = "🔓", Controller = "Accounts", Action = "OpeningBalance",     ParentId = 19, SortOrder = 20, RequiredPermission = "Accounts.View"   },
                
                // ── WhatsApp ──────────────────────────────────────────────────────────
                new MenuItem { Id = 50, Name = "WhatsApp",     Icon = "💬",                                                              SortOrder = 21, RequiredPermission = "WhatsApp.View"   },
                new MenuItem { Id = 51, Name = "Configuration",Icon = "⚙️", Controller = "WhatsApp", Action = "Configure",   ParentId = 50, SortOrder = 1, RequiredPermission = "WhatsApp.Config" },
                new MenuItem { Id = 52, Name = "Inbox",        Icon = "📥", Controller = "WhatsApp", Action = "Inbox",        ParentId = 50, SortOrder = 2, RequiredPermission = "WhatsApp.View"   },
                new MenuItem { Id = 53, Name = "Test Message", Icon = "🧪", Controller = "WhatsApp", Action = "TestMessage",  ParentId = 50, SortOrder = 3, RequiredPermission = "WhatsApp.Config" },
                new MenuItem { Id = 54, Name = "AI Playground", Icon = "🤖", Controller = "WhatsApp", Action = "Playground",   ParentId = 50, SortOrder = 4, RequiredPermission = "WhatsApp.Config" },
            };
    }
}
