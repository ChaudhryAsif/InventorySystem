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

        // ADD these DbSets inside ApplicationDbContext (after existing Accounts DbSets):

        // ── Accounts (Full Module) ────────────────────────────────────────────────
        public DbSet<AccountHead> AccountHeads => Set<AccountHead>();
        public DbSet<GeneralLedger> GeneralLedger => Set<GeneralLedger>();
        public DbSet<Voucher> Vouchers => Set<Voucher>();
        public DbSet<VoucherDetail> VoucherDetails => Set<VoucherDetail>();

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

            // ── AccountHead Precision ─────────────────────────────────────────────────
            modelBuilder.Entity<AccountHead>()
                .Property(x => x.OpeningBalance).HasPrecision(18, 2);

            modelBuilder.Entity<GeneralLedger>()
                .Property(x => x.Debit).HasPrecision(18, 2);
            modelBuilder.Entity<GeneralLedger>()
                .Property(x => x.Credit).HasPrecision(18, 2);

            modelBuilder.Entity<Voucher>()
                .Property(x => x.TotalAmount).HasPrecision(18, 2);

            modelBuilder.Entity<VoucherDetail>()
                .Property(x => x.Debit).HasPrecision(18, 2);
            modelBuilder.Entity<VoucherDetail>()
                .Property(x => x.Credit).HasPrecision(18, 2);

            // ── Indexes ───────────────────────────────────────────────────────────────
            modelBuilder.Entity<AccountHead>()
                .HasIndex(a => a.AccountCode).IsUnique();

            modelBuilder.Entity<AccountHead>()
                .HasOne(a => a.Parent)
                .WithMany(a => a.Children)
                .HasForeignKey(a => a.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<GeneralLedger>()
                .HasIndex(g => new { g.VoucherNo, g.VoucherType });

            modelBuilder.Entity<Voucher>()
                .HasIndex(v => v.VoucherNo).IsUnique();

            // ── Seed Chart of Accounts ────────────────────────────────────────────────
            SeedChartOfAccounts(modelBuilder);

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
                new Permission { Id = 24, Code = "Production.Create", Name = "Create Production", Module = "Production", Action = "Create" },
                new Permission { Id = 25, Code = "Accounts.View",   Name = "View Accounts",   Module = "Accounts", Action = "View" },
                new Permission { Id = 26, Code = "Accounts.Create", Name = "Create Vouchers", Module = "Accounts", Action = "Create" },
                new Permission { Id = 27, Code = "Accounts.Delete", Name = "Void Vouchers",   Module = "Accounts", Action = "Delete" },
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
            modelBuilder.Entity<RolePermission>().HasData(
                new RolePermission { Id = 29, RoleId = 1, PermissionId = 25 },
                new RolePermission { Id = 30, RoleId = 1, PermissionId = 26 },
                new RolePermission { Id = 31, RoleId = 1, PermissionId = 27 }
            );
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

        private void SeedChartOfAccounts(ModelBuilder modelBuilder)
        {
            var accounts = new AccountHead[]
            {
        // ── LEVEL 1: ROOT GROUPS ─────────────────────────────────────────────
        new AccountHead { AccountHeadId=1,  AccountCode="1000", AccountName="Assets",              AccountType="Assets",      Level=1, NormalBalance="Debit",  IsSystem=true, ParentId=null },
        new AccountHead { AccountHeadId=2,  AccountCode="2000", AccountName="Liabilities",         AccountType="Liabilities", Level=1, NormalBalance="Credit", IsSystem=true, ParentId=null },
        new AccountHead { AccountHeadId=3,  AccountCode="3000", AccountName="Equity",              AccountType="Equity",      Level=1, NormalBalance="Credit", IsSystem=true, ParentId=null },
        new AccountHead { AccountHeadId=4,  AccountCode="4000", AccountName="Income",              AccountType="Income",      Level=1, NormalBalance="Credit", IsSystem=true, ParentId=null },
        new AccountHead { AccountHeadId=5,  AccountCode="5000", AccountName="Expenses",            AccountType="Expenses",    Level=1, NormalBalance="Debit",  IsSystem=true, ParentId=null },

        // ── LEVEL 2: SUB-GROUPS ──────────────────────────────────────────────
        new AccountHead { AccountHeadId=10, AccountCode="1100", AccountName="Current Assets",      AccountType="Assets",      Level=2, NormalBalance="Debit",  IsSystem=true, ParentId=1 },
        new AccountHead { AccountHeadId=11, AccountCode="1200", AccountName="Fixed Assets",        AccountType="Assets",      Level=2, NormalBalance="Debit",  IsSystem=true, ParentId=1 },
        new AccountHead { AccountHeadId=12, AccountCode="1300", AccountName="Accounts Receivable", AccountType="Assets",      Level=2, NormalBalance="Debit",  IsSystem=true, ParentId=1 },
        new AccountHead { AccountHeadId=20, AccountCode="2100", AccountName="Current Liabilities", AccountType="Liabilities", Level=2, NormalBalance="Credit", IsSystem=true, ParentId=2 },
        new AccountHead { AccountHeadId=21, AccountCode="2200", AccountName="Accounts Payable",    AccountType="Liabilities", Level=2, NormalBalance="Credit", IsSystem=true, ParentId=2 },
        new AccountHead { AccountHeadId=30, AccountCode="3100", AccountName="Owner's Equity",      AccountType="Equity",      Level=2, NormalBalance="Credit", IsSystem=true, ParentId=3 },
        new AccountHead { AccountHeadId=40, AccountCode="4100", AccountName="Sales Revenue",       AccountType="Income",      Level=2, NormalBalance="Credit", IsSystem=true, ParentId=4 },
        new AccountHead { AccountHeadId=41, AccountCode="4200", AccountName="Other Income",        AccountType="Income",      Level=2, NormalBalance="Credit", IsSystem=true, ParentId=4 },
        new AccountHead { AccountHeadId=50, AccountCode="5100", AccountName="Cost of Goods Sold",  AccountType="Expenses",    Level=2, NormalBalance="Debit",  IsSystem=true, ParentId=5 },
        new AccountHead { AccountHeadId=51, AccountCode="5200", AccountName="Operating Expenses",  AccountType="Expenses",    Level=2, NormalBalance="Debit",  IsSystem=true, ParentId=5 },
        new AccountHead { AccountHeadId=52, AccountCode="5300", AccountName="Financial Expenses",  AccountType="Expenses",    Level=2, NormalBalance="Debit",  IsSystem=true, ParentId=5 },

        // ── LEVEL 3: POSTABLE LEDGER ACCOUNTS ───────────────────────────────
        new AccountHead { AccountHeadId=100, AccountCode="1101", AccountName="Cash in Hand",          AccountType="Assets",      Level=3, NormalBalance="Debit",  IsSystem=true, ParentId=10 },
        new AccountHead { AccountHeadId=101, AccountCode="1102", AccountName="Bank Account",          AccountType="Assets",      Level=3, NormalBalance="Debit",  IsSystem=true, ParentId=10 },
        new AccountHead { AccountHeadId=102, AccountCode="1103", AccountName="Inventory / Stock",     AccountType="Assets",      Level=3, NormalBalance="Debit",  IsSystem=true, ParentId=10 },
        new AccountHead { AccountHeadId=103, AccountCode="1301", AccountName="Trade Receivables",     AccountType="Assets",      Level=3, NormalBalance="Debit",  IsSystem=true, ParentId=12 },
        new AccountHead { AccountHeadId=104, AccountCode="1201", AccountName="Machinery & Equipment", AccountType="Assets",      Level=3, NormalBalance="Debit",  IsSystem=false, ParentId=11 },
        new AccountHead { AccountHeadId=200, AccountCode="2101", AccountName="GST Payable",           AccountType="Liabilities", Level=3, NormalBalance="Credit", IsSystem=true, ParentId=20 },
        new AccountHead { AccountHeadId=201, AccountCode="2102", AccountName="Salaries Payable",      AccountType="Liabilities", Level=3, NormalBalance="Credit", IsSystem=false, ParentId=20 },
        new AccountHead { AccountHeadId=202, AccountCode="2201", AccountName="Trade Payables",        AccountType="Liabilities", Level=3, NormalBalance="Credit", IsSystem=true, ParentId=21 },
        new AccountHead { AccountHeadId=300, AccountCode="3101", AccountName="Capital Account",       AccountType="Equity",      Level=3, NormalBalance="Credit", IsSystem=true, ParentId=30 },
        new AccountHead { AccountHeadId=301, AccountCode="3102", AccountName="Retained Earnings",     AccountType="Equity",      Level=3, NormalBalance="Credit", IsSystem=true, ParentId=30 },
        new AccountHead { AccountHeadId=400, AccountCode="4101", AccountName="Sales",                 AccountType="Income",      Level=3, NormalBalance="Credit", IsSystem=true, ParentId=40 },
        new AccountHead { AccountHeadId=401, AccountCode="4102", AccountName="Sales Returns",         AccountType="Income",      Level=3, NormalBalance="Debit",  IsSystem=true, ParentId=40 },
        new AccountHead { AccountHeadId=402, AccountCode="4201", AccountName="Discount Received",     AccountType="Income",      Level=3, NormalBalance="Credit", IsSystem=false, ParentId=41 },
        new AccountHead { AccountHeadId=500, AccountCode="5101", AccountName="Purchases",             AccountType="Expenses",    Level=3, NormalBalance="Debit",  IsSystem=true, ParentId=50 },
        new AccountHead { AccountHeadId=501, AccountCode="5102", AccountName="Purchase Returns",      AccountType="Expenses",    Level=3, NormalBalance="Credit", IsSystem=true, ParentId=50 },
        new AccountHead { AccountHeadId=502, AccountCode="5103", AccountName="Freight & Cartage",     AccountType="Expenses",    Level=3, NormalBalance="Debit",  IsSystem=false, ParentId=50 },
        new AccountHead { AccountHeadId=510, AccountCode="5201", AccountName="Salaries & Wages",      AccountType="Expenses",    Level=3, NormalBalance="Debit",  IsSystem=false, ParentId=51 },
        new AccountHead { AccountHeadId=511, AccountCode="5202", AccountName="Rent Expense",          AccountType="Expenses",    Level=3, NormalBalance="Debit",  IsSystem=false, ParentId=51 },
        new AccountHead { AccountHeadId=512, AccountCode="5203", AccountName="Utilities Expense",     AccountType="Expenses",    Level=3, NormalBalance="Debit",  IsSystem=false, ParentId=51 },
        new AccountHead { AccountHeadId=513, AccountCode="5204", AccountName="Depreciation",          AccountType="Expenses",    Level=3, NormalBalance="Debit",  IsSystem=false, ParentId=51 },
        new AccountHead { AccountHeadId=520, AccountCode="5301", AccountName="Bank Charges",          AccountType="Expenses",    Level=3, NormalBalance="Debit",  IsSystem=false, ParentId=52 },
        new AccountHead { AccountHeadId=521, AccountCode="5302", AccountName="Discount Allowed",      AccountType="Expenses",    Level=3, NormalBalance="Debit",  IsSystem=false, ParentId=52 },
            };

            foreach (var a in accounts)
            {
                a.IsActive = true;
                a.CreatedDate = SeedDate;
            }

            modelBuilder.Entity<AccountHead>().HasData(accounts);
        }
    }
}