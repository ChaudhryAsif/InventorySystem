using InventorySystem.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Data
{
    public class ApplicationDbContext : DbContext
    {
        // ── Static Seed Date to prevent Model Change Warnings ────────────────────
        private static readonly DateTime SeedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<MenuItem> MenuItems => Set<MenuItem>();

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
            // ── Value Generation ────────────────────────────────────────────────────
            modelBuilder.Entity<PurchaseInvoiceBody>()
                .Property(p => p.Srno).ValueGeneratedOnAdd();

            modelBuilder.Entity<SaleInvoiceBody>()
                .Property(p => p.Srno).ValueGeneratedOnAdd();

            modelBuilder.Entity<PurchaseReturnBody>()
                .Property(p => p.Srno).ValueGeneratedOnAdd();

            modelBuilder.Entity<SaleReturnBody>()
                .Property(p => p.Srno).ValueGeneratedOnAdd();

            modelBuilder.Entity<ConsumeInvoiceBody>()
                .Property(p => p.Srno).ValueGeneratedOnAdd();

            modelBuilder.Entity<CostSheetPly>()
                .Property(p => p.PlyId).ValueGeneratedOnAdd();

            // ── Indexes ─────────────────────────────────────────────────────────────
            modelBuilder.Entity<Stock>()
                .HasIndex(s => new { s.ItemId, s.BranchId }).IsUnique();

            modelBuilder.Entity<AccountLedger>()
                .HasIndex(l => new { l.PartyId, l.EntryDate });

            modelBuilder.Entity<PaymentVoucher>()
                .HasIndex(v => new { v.PartyId, v.VoucherDate });

            modelBuilder.Entity<AppUser>()
                .HasIndex(u => u.Username).IsUnique();

            modelBuilder.Entity<Role>().HasIndex(r => r.Name).IsUnique();
            modelBuilder.Entity<Permission>().HasIndex(p => p.Code).IsUnique();

            // ── DECIMAL PRECISION FOR ALL ENTITIES ───────────────────────────────────

            // Items
            modelBuilder.Entity<Items>()
                .Property(x => x.SalePrice)
                .HasPrecision(18, 2);

            // Stock
            modelBuilder.Entity<Stock>()
                .Property(x => x.Quantity)
                .HasPrecision(18, 2);

            // StockAdjustmentDetail
            modelBuilder.Entity<StockAdjustmentDetail>()
                .Property(x => x.Quantity)
                .HasPrecision(18, 2);

            modelBuilder.Entity<StockAdjustmentDetail>()
                .Property(x => x.TotalCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<StockAdjustmentDetail>()
                .Property(x => x.UnitCost)
                .HasPrecision(18, 2);

            // PurchaseInvoice
            modelBuilder.Entity<PurchaseInvoice>()
                .Property(x => x.Discount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseInvoice>()
                .Property(x => x.TotalAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseInvoice>()
                .Property(x => x.AmountPaid)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseInvoice>()
                .Property(x => x.GSTPer)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseInvoice>()
                .Property(x => x.GSTAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseInvoice>()
                .Property(x => x.FreightExp)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseInvoice>()
                .Property(x => x.OtherExp)
                .HasPrecision(18, 2);

            // PurchaseInvoiceBody
            modelBuilder.Entity<PurchaseInvoiceBody>()
                .Property(x => x.Quantity)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseInvoiceBody>()
                .Property(x => x.PurPrice)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseInvoiceBody>()
                .Property(x => x.SalePrice)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseInvoiceBody>()
                .Property(x => x.Discount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseInvoiceBody>()
                .Property(x => x.DiscPer)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseInvoiceBody>()
                .Property(x => x.DiscAmt)
                .HasPrecision(18, 2);

            // PurchaseReturn
            modelBuilder.Entity<PurchaseReturn>()
                .Property(x => x.TotalAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseReturn>()
                .Property(x => x.NetAmount)
                .HasPrecision(18, 2);

            // PurchaseReturnBody
            modelBuilder.Entity<PurchaseReturnBody>()
                .Property(x => x.Quantity)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseReturnBody>()
                .Property(x => x.PurPrice)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseReturnBody>()
                .Property(x => x.DiscPer)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseReturnBody>()
                .Property(x => x.DiscAmt)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PurchaseReturnBody>()
                .Property(x => x.Total)
                .HasPrecision(18, 2);

            // SaleInvoice
            modelBuilder.Entity<SaleInvoice>()
                .Property(x => x.GSTPer)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleInvoice>()
                .Property(x => x.GSTAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleInvoice>()
                .Property(x => x.Discount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleInvoice>()
                .Property(x => x.FreightExp)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleInvoice>()
                .Property(x => x.OtherExp)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleInvoice>()
                .Property(x => x.TotalAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleInvoice>()
                .Property(x => x.NetAmount)
                .HasPrecision(18, 2);

            // SaleInvoiceBody
            modelBuilder.Entity<SaleInvoiceBody>()
                .Property(x => x.Quantity)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleInvoiceBody>()
                .Property(x => x.SalePrice)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleInvoiceBody>()
                .Property(x => x.DiscPer)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleInvoiceBody>()
                .Property(x => x.DiscAmt)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleInvoiceBody>()
                .Property(x => x.Total)
                .HasPrecision(18, 2);

            // SaleReturn
            modelBuilder.Entity<SaleReturn>()
                .Property(x => x.TotalAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleReturn>()
                .Property(x => x.NetAmount)
                .HasPrecision(18, 2);

            // SaleReturnBody
            modelBuilder.Entity<SaleReturnBody>()
                .Property(x => x.Quantity)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleReturnBody>()
                .Property(x => x.SalePrice)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleReturnBody>()
                .Property(x => x.DiscPer)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleReturnBody>()
                .Property(x => x.DiscAmt)
                .HasPrecision(18, 2);

            modelBuilder.Entity<SaleReturnBody>()
                .Property(x => x.Total)
                .HasPrecision(18, 2);

            // ConsumeInvoiceBody
            modelBuilder.Entity<ConsumeInvoiceBody>()
                .Property(x => x.Quantity)
                .HasPrecision(18, 2);

            // CostSheet
            modelBuilder.Entity<CostSheet>()
                .Property(x => x.Length)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.Width)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.Height)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.Flap)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AutoFlapGap)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.SheetWidthIn)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.SheetLengthIn)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.WithFlap)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AdjWidthIn)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AdjLength1In)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AdjLength2In)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AdjWidthMm)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AdjLengthMm)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.TotalSheetSqIn)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.PrintingCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.DickelCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.LaminationCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.GlueRate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.SilicateRate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.GlueSilicateKg)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.GlueSilicateCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.PaperCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.LabourRate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.LabourCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.EnergyRate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.EnergyCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.BindingCost)
                .HasPrecision(18, 2);

            // CostSheetPly
            modelBuilder.Entity<CostSheetPly>()
                .Property(x => x.GSM)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheetPly>()
                .Property(x => x.Rate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheetPly>()
                .Property(x => x.KGs)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheetPly>()
                .Property(x => x.Cost)
                .HasPrecision(18, 2);

            // CostSheetSettings
            modelBuilder.Entity<CostSheetSettings>()
                .Property(x => x.LabourRate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheetSettings>()
                .Property(x => x.EnergyRate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheetSettings>()
                .Property(x => x.WastePercentage)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheetSettings>()
                .Property(x => x.AdminExpPercentage)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheetSettings>()
                .Property(x => x.SellingDistPercentage)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheetSettings>()
                .Property(x => x.RepairMaintenancePercentage)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheetSettings>()
                .Property(x => x.StoreSparesPercentage)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheetSettings>()
                .Property(x => x.ManufacturingCostPercentage)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheetSettings>()
                .Property(x => x.DefaultFreightRate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheetSettings>()
                .Property(x => x.DefaultProfitPct)
                .HasPrecision(18, 2);

            // ProductionMaterial
            modelBuilder.Entity<ProductionMaterial>()
                .Property(x => x.KgPerUnit)
                .HasPrecision(18, 2);

            modelBuilder.Entity<ProductionMaterial>()
                .Property(x => x.RequiredKg)
                .HasPrecision(18, 2);

            modelBuilder.Entity<ProductionMaterial>()
                .Property(x => x.AvailableKg)
                .HasPrecision(18, 2);

            modelBuilder.Entity<ProductionMaterial>()
                .Property(x => x.ShortfallKg)
                .HasPrecision(18, 2);

            // ProductionConsumption
            modelBuilder.Entity<ProductionConsumption>()
                .Property(x => x.PlannedKg)
                .HasPrecision(18, 2);

            modelBuilder.Entity<ProductionConsumption>()
                .Property(x => x.ActualKg)
                .HasPrecision(18, 2);

            modelBuilder.Entity<ProductionConsumption>()
                .Property(x => x.VarianceKg)
                .HasPrecision(18, 2);

            // Party
            modelBuilder.Entity<Party>()
                .Property(x => x.OpeningBalance)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Party>()
                .Property(x => x.CreditLimit)
                .HasPrecision(18, 2);

            // AccountLedger
            modelBuilder.Entity<AccountLedger>()
                .Property(x => x.Debit)
                .HasPrecision(18, 2);

            modelBuilder.Entity<AccountLedger>()
                .Property(x => x.Credit)
                .HasPrecision(18, 2);

            // PaymentVoucher
            modelBuilder.Entity<PaymentVoucher>()
                .Property(x => x.Amount)
                .HasPrecision(18, 2);

            // ── RELATIONSHIP CONFIGURATIONS ──────────────────────────────────────────
            modelBuilder.Entity<RolePermission>()
                .HasOne(rp => rp.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(rp => rp.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RolePermission>()
                .HasOne(rp => rp.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(rp => rp.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(ur => ur.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.Role)
                .WithMany()
                .HasForeignKey(ur => ur.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MenuItem>()
                .HasOne(m => m.Parent)
                .WithMany(m => m.Children)
                .HasForeignKey(m => m.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            // ── SEED DEFAULT DATA ────────────────────────────────────────────────────
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
                UpdatedAt = SeedDate
            });

            // CostSheet - ALL decimal properties
            modelBuilder.Entity<CostSheet>()
                .Property(x => x.Length)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.Width)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.Height)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.Flap)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AutoFlapGap)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.SheetWidthIn)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.SheetLengthIn)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.WithFlap)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AdjWidthIn)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AdjLength1In)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AdjLength2In)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AdjWidthMm)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AdjLengthMm)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.TotalSheetSqIn)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.PrintingCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.DickelCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.LaminationCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.GlueRate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.SilicateRate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.GlueSilicateKg)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.GlueSilicateCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.PaperCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.LabourRate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.LabourCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.EnergyRate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.EnergyCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.BindingCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.FreightRate)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.FreightCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.SubTotal)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.WastePct)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.WasteCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AdminExpPct)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.AdminExpCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.SellingDistPct)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.SellingDistCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.RepairMaintPct)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.RepairMaintCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.StoreSparesPct)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.StoreSparesCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.MfgCostPct)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.MfgCostValue)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.ManufacturingTotal)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.CommPersonAPct)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.CommPersonACost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.CommPersonBPct)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.CommPersonBCost)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.WHTaxPct)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.GSTPercentage)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.ProfitPct)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.ProfitAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.FinalCostWOGST)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.TaxPct)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CostSheet>()
                .Property(x => x.FinalRateWithGST)
                .HasPrecision(18, 2);

            SeedRoles(modelBuilder);
            SeedPermissions(modelBuilder);
            SeedRolePermissions(modelBuilder);
            SeedMenuItems(modelBuilder);
            SeedAdminUser(modelBuilder);
        }

        private void SeedRoles(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Role>().HasData(
                new Role { Id = 1, Name = "Admin", Description = "Full system access", IsActive = true },
                new Role { Id = 2, Name = "CostSheetUser", Description = "Only Cost Sheet access", IsActive = true },
                new Role { Id = 3, Name = "StandardUser", Description = "Basic access", IsActive = true }
            );
        }

        private void SeedPermissions(ModelBuilder modelBuilder)
        {
            var permissions = new Permission[]
            {
                new Permission { Id = 1, Code = "Dashboard.View", Name = "View Dashboard", Module = "Dashboard", Action = "View" },
                new Permission { Id = 2, Code = "ItemCategory.View", Name = "View Item Category", Module = "ItemCategory", Action = "View" },
                new Permission { Id = 3, Code = "ItemCategory.Create", Name = "Create Item Category", Module = "ItemCategory", Action = "Create" },
                new Permission { Id = 4, Code = "ItemCategory.Edit", Name = "Edit Item Category", Module = "ItemCategory", Action = "Edit" },
                new Permission { Id = 5, Code = "ItemCategory.Delete", Name = "Delete Item Category", Module = "ItemCategory", Action = "Delete" },
                new Permission { Id = 6, Code = "Item.View", Name = "View Items", Module = "Item", Action = "View" },
                new Permission { Id = 7, Code = "Item.Create", Name = "Create Item", Module = "Item", Action = "Create" },
                new Permission { Id = 8, Code = "Item.Edit", Name = "Edit Item", Module = "Item", Action = "Edit" },
                new Permission { Id = 9, Code = "Item.Delete", Name = "Delete Item", Module = "Item", Action = "Delete" },
                new Permission { Id = 10, Code = "CostSheet.View", Name = "View Cost Sheets", Module = "CostSheet", Action = "View" },
                new Permission { Id = 11, Code = "CostSheet.Create", Name = "Create Cost Sheet", Module = "CostSheet", Action = "Create" },
                new Permission { Id = 12, Code = "CostSheet.Edit", Name = "Edit Cost Sheet", Module = "CostSheet", Action = "Edit" },
                new Permission { Id = 13, Code = "CostSheet.Delete", Name = "Delete Cost Sheet", Module = "CostSheet", Action = "Delete" },
                new Permission { Id = 14, Code = "CostSheet.Settings", Name = "Cost Sheet Settings", Module = "CostSheet", Action = "Settings" },
                new Permission { Id = 15, Code = "Purchase.View", Name = "View Purchase Invoices", Module = "Purchase", Action = "View" },
                new Permission { Id = 16, Code = "Purchase.Create", Name = "Create Purchase Invoice", Module = "Purchase", Action = "Create" },
                new Permission { Id = 17, Code = "Purchase.Edit", Name = "Edit Purchase Invoice", Module = "Purchase", Action = "Edit" },
                new Permission { Id = 18, Code = "Purchase.Delete", Name = "Delete Purchase Invoice", Module = "Purchase", Action = "Delete" },
                new Permission { Id = 19, Code = "StockAdjustment.View", Name = "View Stock Adjustments", Module = "StockAdjustment", Action = "View" },
                new Permission { Id = 20, Code = "StockAdjustment.Create", Name = "Create Stock Adjustment", Module = "StockAdjustment", Action = "Create" },
                new Permission { Id = 21, Code = "Sale.View", Name = "View Sale Invoices", Module = "Sale", Action = "View" },
                new Permission { Id = 22, Code = "Sale.Create", Name = "Create Sale Invoice", Module = "Sale", Action = "Create" },
                new Permission { Id = 23, Code = "Production.View", Name = "View Production", Module = "Production", Action = "View" },
                new Permission { Id = 24, Code = "Production.Create", Name = "Create Production", Module = "Production", Action = "Create" }
            };
            modelBuilder.Entity<Permission>().HasData(permissions);
        }

        private void SeedRolePermissions(ModelBuilder modelBuilder)
        {
            var adminPermissions = Enumerable.Range(1, 24).Select(i => new RolePermission
            {
                Id = i,
                RoleId = 1,
                PermissionId = i
            }).ToList();

            var costSheetUserPermissions = new[]
            {
                new RolePermission { Id = 25, RoleId = 2, PermissionId = 10 },
                new RolePermission { Id = 26, RoleId = 2, PermissionId = 11 },
                new RolePermission { Id = 27, RoleId = 2, PermissionId = 12 },
                new RolePermission { Id = 28, RoleId = 2, PermissionId = 14 }
            };

            modelBuilder.Entity<RolePermission>().HasData(adminPermissions);
            modelBuilder.Entity<RolePermission>().HasData(costSheetUserPermissions);
        }

        private void SeedMenuItems(ModelBuilder modelBuilder)
        {
            var menuItems = new MenuItem[]
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
                new MenuItem { Id = 18, Name = "New Order", Icon = "➕", Controller = "Production", Action = "Create", ParentId = 16, SortOrder = 2, RequiredPermission = "Production.Create" }
            };
            modelBuilder.Entity<MenuItem>().HasData(menuItems);
        }

        private void SeedAdminUser(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AppUser>().HasData(new AppUser
            {
                Id = 1,
                Username = "admin",
                PasswordHash = "$2a$11$gGgjUBMKp1bmCsGlTZzUp.REHp88eeHmVtsF3ur3CA0wREWLPqQA6",
                FullName = "System Administrator",
                Role = "Admin",
                IsActive = true,
                CreatedAt = SeedDate, // Use static date
                PrimaryRoleId = 1
            });

            modelBuilder.Entity<UserRole>().HasData(new UserRole
            {
                Id = 1,
                UserId = 1,
                RoleId = 1,
                AssignedAt = SeedDate // Use static date
            });
        }
    }
}