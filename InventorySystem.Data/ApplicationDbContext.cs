using InventorySystem.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<ItemCategory> ItemCategory => Set<ItemCategory>();
        public DbSet<Items> Items => Set<Items>();
        public DbSet<Party> Parties => Set<Party>();
        public DbSet<PurchaseInvoice> PurchaseInvoice => Set<PurchaseInvoice>();
        public DbSet<PurchaseInvoiceBody> PurchaseInvoiceBody => Set<PurchaseInvoiceBody>();
        public DbSet<Stock> Stock => Set<Stock>();
        public DbSet<SaleInvoice> SaleInvoice => Set<SaleInvoice>();
        public DbSet<SaleInvoiceBody> SaleInvoiceBody => Set<SaleInvoiceBody>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // This disables EF Core OUTPUT clause
            modelBuilder.Entity<PurchaseInvoiceBody>()
                .Property(p => p.Srno)
                .ValueGeneratedNever();

            modelBuilder.Entity<PurchaseInvoiceBody>()
       .Property(p => p.Srno)
       .ValueGeneratedOnAdd(); // Auto identity in SQL

            modelBuilder.Entity<SaleInvoiceBody>()
                .Property(p => p.Srno)
                .ValueGeneratedOnAdd();

            // Unique constraint: one stock row per item+branch
            modelBuilder.Entity<Stock>()
                .HasIndex(s => new { s.ItemId, s.BranchId })
                .IsUnique();

            base.OnModelCreating(modelBuilder);
        }

    }
}
