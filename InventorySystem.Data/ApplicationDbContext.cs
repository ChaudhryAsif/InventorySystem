using InventorySystem.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        // ── Master data ──────────────────────────────────────────────────────────
        public DbSet<ItemCategory> ItemCategory => Set<ItemCategory>();
        public DbSet<Items> Items => Set<Items>();
        public DbSet<Party> Parties => Set<Party>();

        // ── Inventory ────────────────────────────────────────────────────────────
        public DbSet<Stock> Stock => Set<Stock>();
        public DbSet<StockAdjustment> StockAdjustment => Set<StockAdjustment>();
        public DbSet<StockAdjustmentDetail> StockAdjustmentDetail => Set<StockAdjustmentDetail>();

        // ── Purchase ─────────────────────────────────────────────────────────────
        public DbSet<PurchaseInvoice> PurchaseInvoice => Set<PurchaseInvoice>();
        public DbSet<PurchaseInvoiceBody> PurchaseInvoiceBody => Set<PurchaseInvoiceBody>();

        // ── Purchase Returns ─────────────────────────────────────────────────────
        public DbSet<PurchaseReturn> PurchaseReturn => Set<PurchaseReturn>();
        public DbSet<PurchaseReturnBody> PurchaseReturnBody => Set<PurchaseReturnBody>();

        // ── Sale ─────────────────────────────────────────────────────────────────
        public DbSet<SaleInvoice> SaleInvoice => Set<SaleInvoice>();
        public DbSet<SaleInvoiceBody> SaleInvoiceBody => Set<SaleInvoiceBody>();

        // ── Sale Returns ─────────────────────────────────────────────────────────
        public DbSet<SaleReturn> SaleReturn => Set<SaleReturn>();
        public DbSet<SaleReturnBody> SaleReturnBody => Set<SaleReturnBody>();

        // ── Accounts ─────────────────────────────────────────────────────────────
        public DbSet<AccountLedger> AccountLedger => Set<AccountLedger>();
        public DbSet<PaymentVoucher> PaymentVoucher => Set<PaymentVoucher>();

        // ── Auth ──────────────────────────────────────────────────────────────────
        public DbSet<AppUser> AppUsers => Set<AppUser>();

        // ── Consumption ───────────────────────────────────────────────────────────
        public DbSet<ConsumeInvoice> ConsumeInvoice => Set<ConsumeInvoice>();
        public DbSet<ConsumeInvoiceBody> ConsumeInvoiceBody => Set<ConsumeInvoiceBody>();

        // ── Cost Sheet ────────────────────────────────────────────────────────────
        public DbSet<CostSheetSettings> CostSheetSettings => Set<CostSheetSettings>();
        public DbSet<CostSheet> CostSheet => Set<CostSheet>();
        public DbSet<CostSheetPly> CostSheetPly => Set<CostSheetPly>();

        // ── Production ────────────────────────────────────────────────────────────
        public DbSet<ProductionOrder> ProductionOrder => Set<ProductionOrder>();
        public DbSet<ProductionMaterial> ProductionMaterial => Set<ProductionMaterial>();
        public DbSet<ProductionConsumption> ProductionConsumption => Set<ProductionConsumption>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PurchaseInvoiceBody>()
                .Property(p => p.Srno).ValueGeneratedNever();

            modelBuilder.Entity<PurchaseInvoiceBody>()
                .Property(p => p.Srno).ValueGeneratedOnAdd();

            modelBuilder.Entity<SaleInvoiceBody>()
                .Property(p => p.Srno).ValueGeneratedOnAdd();

            modelBuilder.Entity<PurchaseReturnBody>()
                .Property(p => p.Srno).ValueGeneratedOnAdd();

            modelBuilder.Entity<SaleReturnBody>()
                .Property(p => p.Srno).ValueGeneratedOnAdd();

            modelBuilder.Entity<Stock>()
                .HasIndex(s => new { s.ItemId, s.BranchId }).IsUnique();

            modelBuilder.Entity<AccountLedger>()
                .HasIndex(l => new { l.PartyId, l.EntryDate });

            modelBuilder.Entity<PaymentVoucher>()
                .HasIndex(v => new { v.PartyId, v.VoucherDate });

            // Unique username
            modelBuilder.Entity<AppUser>()
                .HasIndex(u => u.Username).IsUnique();

            // Seed default admin  →  password: Admin@123
            modelBuilder.Entity<AppUser>().HasData(new AppUser
            {
                Id = 1,
                Username = "admin",
                // Pre-computed BCrypt hash for:  Admin@123
                PasswordHash = "$2a$11$gGgjUBMKp1bmCsGlTZzUp.REHp88eeHmVtsF3ur3CA0wREWLPqQA6",
                FullName = "System Administrator",
                Role = "Admin",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

            modelBuilder.Entity<ConsumeInvoiceBody>()
                .Property(p => p.Srno).ValueGeneratedOnAdd();

            modelBuilder.Entity<CostSheetPly>()
                .Property(p => p.PlyId).ValueGeneratedOnAdd();

            // Seed one default settings row
            modelBuilder.Entity<CostSheetSettings>().HasData(new CostSheetSettings
            {
                Id = 1,
                LabourRate = 5m,
                EnergyRate = 5.08m,
                WastePercentage = 6m,
                AdminExpPercentage = 1.52m,
                SellingDistPercentage = 1m,
                RepairMaintenancePercentage = 1.5m,
                StoreSparesPercentage = 1.5m,
                ManufacturingCostPercentage = 1.72m,
                DefaultFreightRate = 0.5m,
                DefaultProfitPct = 11m,
                UpdatedAt = new DateTime(2026, 1, 1)
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}