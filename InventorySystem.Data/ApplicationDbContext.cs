using InventorySystem.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Product> Products => Set<Product>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
        public DbSet<Account> Accounts => Set<Account>();
        public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // InvoiceItem relationship
            modelBuilder.Entity<InvoiceItem>()
                .HasOne(i => i.Invoice)
                .WithMany(p => p.Items)
                .HasForeignKey(i => i.InvoiceId);

            // Prevent multiple cascade paths for JournalEntry
            modelBuilder.Entity<JournalEntry>()
                .HasOne(j => j.DebitAccount)
                .WithMany()
                .HasForeignKey(j => j.DebitAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<JournalEntry>()
                .HasOne(j => j.CreditAccount)
                .WithMany()
                .HasForeignKey(j => j.CreditAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // Optional: set decimal precision to avoid warnings
            modelBuilder.Entity<Product>().Property(p => p.CostPrice).HasPrecision(18, 2);
            modelBuilder.Entity<Product>().Property(p => p.SalePrice).HasPrecision(18, 2);
            modelBuilder.Entity<Invoice>().Property(p => p.TotalAmount).HasPrecision(18, 2);
            modelBuilder.Entity<InvoiceItem>().Property(p => p.Rate).HasPrecision(18, 2);
            modelBuilder.Entity<JournalEntry>().Property(p => p.Amount).HasPrecision(18, 2);
        }

    }
}
