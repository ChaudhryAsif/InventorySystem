using InventorySystem.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        // ── Master data ─────────────────────────────────────────────────────────
        public DbSet<ItemCategory> ItemCategory => Set<ItemCategory>();
        public DbSet<Items> Items => Set<Items>();
        public DbSet<Party> Parties => Set<Party>();

        // ── Inventory ───────────────────────────────────────────────────────────
        public DbSet<Stock> Stock => Set<Stock>();

        // ── Purchase ────────────────────────────────────────────────────────────
        public DbSet<PurchaseInvoice> PurchaseInvoice => Set<PurchaseInvoice>();
        public DbSet<PurchaseInvoiceBody> PurchaseInvoiceBody => Set<PurchaseInvoiceBody>();

        // ── Sale ────────────────────────────────────────────────────────────────
        public DbSet<SaleInvoice> SaleInvoice => Set<SaleInvoice>();
        public DbSet<SaleInvoiceBody> SaleInvoiceBody => Set<SaleInvoiceBody>();

        // ── Accounts ────────────────────────────────────────────────────────────
        public DbSet<AccountLedger> AccountLedger => Set<AccountLedger>();
        public DbSet<PaymentVoucher> PaymentVoucher => Set<PaymentVoucher>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PurchaseInvoiceBody>()
                .Property(p => p.Srno).ValueGeneratedNever();

            modelBuilder.Entity<PurchaseInvoiceBody>()
                .Property(p => p.Srno).ValueGeneratedOnAdd();

            modelBuilder.Entity<SaleInvoiceBody>()
                .Property(p => p.Srno).ValueGeneratedOnAdd();

            modelBuilder.Entity<Stock>()
                .HasIndex(s => new { s.ItemId, s.BranchId }).IsUnique();

            // Useful query indexes
            modelBuilder.Entity<AccountLedger>()
                .HasIndex(l => new { l.PartyId, l.EntryDate });

            modelBuilder.Entity<PaymentVoucher>()
                .HasIndex(v => new { v.PartyId, v.VoucherDate });

            base.OnModelCreating(modelBuilder);
        }
    }
}