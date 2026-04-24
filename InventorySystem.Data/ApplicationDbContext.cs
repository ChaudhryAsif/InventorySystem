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

            base.OnModelCreating(modelBuilder);
        }
    }
}