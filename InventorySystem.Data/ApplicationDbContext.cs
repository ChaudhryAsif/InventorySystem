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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

            // 👇 This disables EF Core OUTPUT clause
            modelBuilder.Entity<PurchaseInvoiceBody>()
                .Property(p => p.Srno)
                .ValueGeneratedNever();

            modelBuilder.Entity<PurchaseInvoiceBody>()
       .Property(p => p.Srno)
       .ValueGeneratedOnAdd(); // ✅ Auto identity in SQL

            base.OnModelCreating(modelBuilder);
        }

    }
}
