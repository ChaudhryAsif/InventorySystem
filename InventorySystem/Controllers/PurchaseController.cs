using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class PurchaseController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILedgerPostingService _ledgerPosting;
        private readonly AccountingOptions _accountingOptions;
        private readonly IStockService _stock;

        public PurchaseController(ApplicationDbContext context, ILedgerPostingService ledgerPosting, IOptions<AccountingOptions> accountingOptions, IStockService stock)
        {
            _context = context;
            _ledgerPosting = ledgerPosting;
            _accountingOptions = accountingOptions.Value;
            _stock = stock;
        }

        [HttpGet]
        public IActionResult Index() => View();

        [HttpGet]
        public async Task<IActionResult> GetSupplierGroups(bool includeInactive = false)
        {
            var suppliers = await _context.ItemCategory.AsNoTracking()
                .Where(g => (includeInactive || g.IsActive == true) && g.GroupType == "RawMaterial")
                .OrderBy(g => g.CategoryName)
                .Select(g => new { partyId = "group:" + g.CategoryId, partyName = g.CategoryName,
                    groupId = g.CategoryId, phone = g.ContactNumber, address = g.Address,
                    description = g.Description }).ToListAsync();
            return Json(new { success = true, suppliers });
        }

        private async Task<int> SupplierGroupIdAsync(string? vendor)
        {
            var selection=vendor;
            if(int.TryParse(vendor,out var partyId))selection=await _context.Parties.Where(p=>p.PartyId==partyId).Select(p=>p.PartyCode).SingleOrDefaultAsync();
            var raw=selection?.StartsWith("group:")==true?selection[6..]:selection?.StartsWith("RAW-GROUP:")==true?selection[10..]:null;
            if(!int.TryParse(raw,out var id)||!await _context.ItemCategory.AnyAsync(g=>g.CategoryId==id&&g.GroupType=="RawMaterial"&&g.IsActive==true))
                throw new InvalidOperationException("Select an active Raw Material supplier group.");
            return id;
        }
        private async Task ValidateGroupItemsAsync(PurchaseInvoiceViewModel model)
        {
            var groupId=await SupplierGroupIdAsync(model.VendorID);
            var ids=model.Items.Where(l=>l.Itemid.HasValue).Select(l=>l.Itemid!.Value).Distinct().ToList();
            if(await _context.Items.CountAsync(i=>ids.Contains(i.ItemID)&&i.CategoryID==groupId&&i.IsActive==true)!=ids.Count)
                throw new InvalidOperationException("Every purchase item must belong to the selected supplier group. Select its items again.");
        }
        [HttpGet]
        public async Task<IActionResult> GetItemOptions(string? vendor,long? itemId=null,int page=1,int pageSize=200)
        {
            int groupId;try{groupId=await SupplierGroupIdAsync(vendor);}catch(InvalidOperationException ex){return BadRequest(new{success=false,message=ex.Message});}
            page=Math.Clamp(page,1,1000000);pageSize=Math.Clamp(pageSize,1,200);
            var query=_context.Items.AsNoTracking().Where(i=>i.CategoryID==groupId&&i.IsActive==true);
            if(itemId.HasValue)query=query.Where(i=>i.ItemID==itemId);
            var totalCount=await query.CountAsync();
            var items=await query.OrderBy(i=>i.ItemID).Skip((page-1)*pageSize).Take(pageSize).Select(i=>new{itemID=i.ItemID,itemName=i.ItemName,categoryName=i.Category.CategoryName,categoryID=i.CategoryID,itemType=i.ItemType,stockUnit=i.StockUnit,barcode=i.Barcode,imageUrl=i.ImagePath,salePrice=i.SalePrice,purchasePrice=_context.PurchaseInvoiceBody.Where(b=>b.Itemid==i.ItemID&&_context.PurchaseInvoice.Any(p=>p.PurchaseId==b.PurchaseId)).OrderByDescending(b=>b.Srno).Select(b=>b.PurPrice).FirstOrDefault()??0,currentStock=_context.Stock.Where(x=>x.ItemId==i.ItemID).Sum(x=>(decimal?)x.Quantity)??0}).ToListAsync();
            return Json(new{success=true,items,totalCount,page,pageSize});
        }

        // Group IDs are never used as accounting Party IDs. Resolve the stable account
        // inside the invoice transaction so a failed save cannot leave an orphan account.
        private async Task ResolveSupplierGroupAsync(PurchaseInvoiceViewModel model)
        {
            if (model.VendorID?.StartsWith("group:", StringComparison.Ordinal) != true) return;
            if (!int.TryParse(model.VendorID[6..], out var groupId))
                throw new InvalidOperationException("Select a valid Raw Material group.");
            var group = await _context.ItemCategory.SingleOrDefaultAsync(g =>
                g.CategoryId == groupId && g.GroupType == "RawMaterial" && g.IsActive == true);
            if (group == null) throw new InvalidOperationException("Select an active Raw Material group.");
            var code = "RAW-GROUP:" + groupId;
            var party = await _context.Parties.SingleOrDefaultAsync(p => p.PartyCode == code);
            if (party == null)
            {
                party = new Party { PartyCode = code, PartyType = "supplier", Status = "active",
                    PartyName = group.CategoryName, Phone = group.ContactNumber, Address = group.Address,
                    OpeningBalance = 0 };
                _context.Parties.Add(party);
            }
            else
            {
                if (party.Status != "active" || party.PartyType is not ("supplier" or "both"))
                    throw new InvalidOperationException("The supplier account for this group is inactive or unavailable.");
                party.PartyName = group.CategoryName;
                party.Phone = group.ContactNumber;
                party.Address = group.Address;
            }
            await _context.SaveChangesAsync();
            model.VendorID = party.PartyId.ToString();
        }

        // ── Saved-invoice list page ───────────────────────────────────────────
        [HttpGet]
        public IActionResult List() => View();

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] PurchaseInvoiceViewModel model)
        {
            if (model == null || model.Items == null || !model.Items.Any())
                return BadRequest("Invalid invoice data.");

            var validItems = model.Items.Where(i => i.Itemid != null && i.Quantity > 0).ToList();
            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            var branchId = model.BranchID ?? 1;

            using var transaction = _context.Database.BeginAtomicTransaction();
            try
            {
                try
                {
                    await ValidateGroupItemsAsync(model);
                    await ResolveSupplierGroupAsync(model);
                    await InvoiceValidation.Validate(_context, model);
                }
                catch (InvalidOperationException ex)
                {
                    transaction.Rollback();
                    return BadRequest(new { success = false, message = ex.Message });
                }
                var invoice = new PurchaseInvoice
                {
                    PurchaseDate = model.PurchaseDate,
                    VendorID = model.VendorID,
                    BillNo = model.BillNo,
                    BranchID = model.BranchID,
                    PaymentMode = model.PaymentMode,
                    Remarks = model.Remarks,
                    GSTPer = model.GSTPer,
                    GSTAmount = model.GSTAmount,
                    FreightExp = model.FreightExp,
                    OtherExp = model.OtherExp,
                    Discount = model.Discount,
                    TotalAmount = model.TotalAmount,
                    AmountPaid = model.NetAmount   // total amount the invoice is worth
                };

                _context.PurchaseInvoice.Add(invoice);
                _context.SaveChanges(); // materialise PurchaseId

                await ApplyPurchaseLinesAndStockAsync(invoice, validItems, branchId);
                await PostLedgerForPurchaseAsync(invoice);

                transaction.Commit();
                return Ok(new { success = true, message = "Invoice saved successfully", id = invoice.PurchaseId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error saving purchase invoice: " + ex.Message });
            }
        }

        // ── UPDATE an existing invoice ────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] PurchaseInvoiceViewModel model)
        {
            if (model == null || model.PurchaseId is null or <= 0)
                return BadRequest("Invalid invoice id.");
            if (model.Items == null || !model.Items.Any())
                return BadRequest("Invalid invoice data.");

            var validItems = model.Items.Where(i => i.Itemid != null && i.Quantity > 0).ToList();
            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            var branchId = model.BranchID ?? 1;

            using var transaction = _context.Database.BeginAtomicTransaction();
            try
            {
                var invoice = _context.PurchaseInvoice.FirstOrDefault(p => p.PurchaseId == model.PurchaseId);
                if (invoice == null)
                    return Ok(new { success = false, message = "Invoice not found." });

                if (await _context.PurchaseReturn.AnyAsync(r => r.OriginalPurchaseId == invoice.PurchaseId))
                    return BadRequest(new { success = false, message = "Cancel linked returns before editing or cancelling this invoice." });

                try
                {
                    await ValidateGroupItemsAsync(model);
                    await ResolveSupplierGroupAsync(model);
                    await InvoiceValidation.Validate(_context, model);
                }
                catch (InvalidOperationException ex)
                {
                    transaction.Rollback();
                    return BadRequest(new { success = false, message = ex.Message });
                }
                var bodies = _context.PurchaseInvoiceBody.Where(b => b.PurchaseId == invoice.PurchaseId).ToList();

                // Apply only the net change: old receipts may already be consumed.
                var stockChanges = new Dictionary<(long ItemId, int BranchId), decimal>();
                foreach (var body in bodies.Where(b => b.Itemid.HasValue))
                {
                    var key = (body.Itemid!.Value, invoice.BranchID ?? 1);
                    stockChanges[key] = stockChanges.GetValueOrDefault(key) - (body.Quantity ?? 0);
                }
                foreach (var line in validItems)
                {
                    var key = (line.Itemid!.Value, branchId);
                    stockChanges[key] = stockChanges.GetValueOrDefault(key) + (line.Quantity ?? 0);
                }
                foreach (var change in stockChanges.Where(c => c.Value != 0))
                    await _stock.AdjustAsync(change.Key.ItemId, change.Key.BranchId, change.Value);

                await ReversePurchaseEffectsAsync(invoice, bodies, adjustStock: false);
                _context.SaveChanges();

                // 2) Update header + re-apply lines/stock/ledger
                invoice.PurchaseDate = model.PurchaseDate;
                invoice.VendorID = model.VendorID;
                invoice.BillNo = model.BillNo;
                invoice.BranchID = model.BranchID;
                invoice.PaymentMode = model.PaymentMode;
                invoice.Remarks = model.Remarks;
                invoice.GSTPer = model.GSTPer;
                invoice.GSTAmount = model.GSTAmount;
                invoice.FreightExp = model.FreightExp;
                invoice.OtherExp = model.OtherExp;
                invoice.Discount = model.Discount;
                invoice.TotalAmount = model.TotalAmount;
                invoice.AmountPaid = model.NetAmount;

                await ApplyPurchaseLinesAndStockAsync(invoice, validItems, branchId, adjustStock: false);
                await PostLedgerForPurchaseAsync(invoice);

                transaction.Commit();
                return Ok(new { success = true, message = "Invoice updated successfully", id = invoice.PurchaseId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error updating purchase invoice: " + ex.Message });
            }
        }

        // ── DELETE an invoice (with stock + ledger reversal) ──────────────────
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var reason = Request.Headers["X-Operation-Reason"].ToString().Trim();
            if (reason.Length < 3 || reason.Length > 500)
                return BadRequest(new { success = false, message = "Enter a cancellation reason (3–500 characters)." });

            using var transaction = _context.Database.BeginAtomicTransaction();
            try
            {
                var invoice = _context.PurchaseInvoice.FirstOrDefault(p => p.PurchaseId == id);
                if (invoice == null)
                    return Ok(new { success = false, message = "Invoice not found." });

                if (await _context.PurchaseReturn.AnyAsync(r => r.OriginalPurchaseId == invoice.PurchaseId))
                    return BadRequest(new { success = false, message = "Cancel linked returns before editing or cancelling this invoice." });

                var bodies = _context.PurchaseInvoiceBody.Where(b => b.PurchaseId == id).ToList();

                await ReversePurchaseEffectsAsync(invoice, bodies, keepBodies: true);
                invoice.IsCancelled = true;
                invoice.CancelledUtc = DateTime.UtcNow;
                invoice.CancelledBy = User.Identity?.Name;
                invoice.CancellationReason = reason;
                _context.SaveChanges();

                transaction.Commit();
                return Ok(new { success = true, message = "Purchase invoice cancelled successfully." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error deleting purchase invoice: " + ex.Message });
            }
        }

        // ── LIST data (with optional search + date range) ─────────────────────
        [HttpGet]
        public IActionResult GetPurchases(string? search, DateTime? from, DateTime? to, int page = 1, int pageSize = 50, bool includeCancelled = false, bool cancelledOnly = false, int? groupId = null)
        {
            var query = includeCancelled || cancelledOnly
                ? _context.PurchaseInvoice.IgnoreQueryFilters().Where(s => !_context.RestrictBranches || _context.AllowedBranchIds.Contains(s.BranchID ?? 1))
                : _context.PurchaseInvoice.AsQueryable();
            if (cancelledOnly) query = query.Where(p => p.IsCancelled);
            if (groupId.HasValue)
            {
                var group = _context.ItemCategory.SingleOrDefault(g => g.CategoryId == groupId && g.GroupType == "RawMaterial");
                if (group == null) return BadRequest(new { success = false, message = "Select a valid Raw Material group." });
                var code = "RAW-GROUP:" + group.CategoryId;
                var vendors = _context.Parties.Where(p => p.PartyCode == code || p.PartyName == group.CategoryName).Select(p => p.PartyId.ToString());
                query = query.Where(p => vendors.Contains(p.VendorID));
            }
            page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var parties = _context.Parties.Where(p => p.PartyName.Contains(term)).Select(p => p.PartyId.ToString());
                query = query.Where(s => parties.Contains(s.VendorID) || (s.BillNo != null && s.BillNo.Contains(term)) || s.PurchaseId.ToString().Contains(term));
            }


            if (from.HasValue) query = query.Where(p => p.PurchaseDate >= from.Value.Date);
            if (to.HasValue) query = query.Where(p => p.PurchaseDate < to.Value.Date.AddDays(1));

            var totalCount = query.Count();
            var purchases = query
                .OrderByDescending(p => p.PurchaseId)
                .Select(p => new
                {
                    p.PurchaseId,
                p.IsCancelled,
                    PurchaseDate = p.PurchaseDate,
                    p.VendorID,
                    VendorName = _context.Parties
                        .Where(v => v.PartyId.ToString() == p.VendorID)
                        .Select(v => v.PartyName)
                        .FirstOrDefault(),
                    p.BillNo,
                    p.PaymentMode,
                    NetAmount = p.AmountPaid,
                    ItemCount = _context.PurchaseInvoiceBody.Count(b => b.PurchaseId == p.PurchaseId)
                })
                .Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var result = purchases.Select(p => new
            {
                p.PurchaseId,
                p.IsCancelled,
                PurchaseDate = p.PurchaseDate.HasValue ? p.PurchaseDate.Value.ToString("yyyy-MM-dd") : "",
                VendorName = p.VendorName ?? $"#{p.VendorID}",
                p.BillNo,
                PaymentMode = GetPaymentModeLabel(p.PaymentMode),
                p.NetAmount,
                p.ItemCount
            });

            var paged = result.ToList();

            return Json(new { data = paged, totalCount, page, pageSize });
        }

        // ── Single invoice (for view + edit) ──────────────────────────────────
        [HttpGet]
        public IActionResult GetPurchaseById(int id)
        {
            var invoice = _context.PurchaseInvoice.IgnoreQueryFilters()
                .Where(p => !_context.RestrictBranches || _context.AllowedBranchIds.Contains(p.BranchID ?? 1))
                .FirstOrDefault(p => p.PurchaseId == id);
            if (invoice == null) return NotFound();

            var vendorName = _context.Parties
                .Where(v => v.PartyId.ToString() == invoice.VendorID)
                .Select(v => v.PartyName)
                .FirstOrDefault();

            var branchId = invoice.BranchID ?? 1;

            var bodies = _context.PurchaseInvoiceBody
                .Where(b => b.PurchaseId == id)
                .Select(b => new
                {
                    b.Itemid,
                    b.Descr,
                    b.PackageCount, b.PackageType,
                    b.Quantity,
                    StockUnit = _context.Items.Where(i=>i.ItemID==b.Itemid).Select(i=>i.StockUnit).FirstOrDefault(),
                    b.PurPrice,
                    b.SalePrice,
                    b.DiscPer,
                    b.DiscAmt,
                    // For edit mode: max sellable = current stock + qty already on this invoice
                    AvailStock = (_context.Stock
                        .Where(s => s.ItemId == b.Itemid && s.BranchId == branchId)
                        .Select(s => (decimal?)s.Quantity)
                        .FirstOrDefault() ?? 0) + (b.Quantity ?? 0)
                })
                .ToList();

            return Json(new
            {
                invoice.PurchaseId,
                invoice.IsCancelled,
                PurchaseDate = invoice.PurchaseDate.HasValue ? invoice.PurchaseDate.Value.ToString("yyyy-MM-dd") : "",
                invoice.VendorID,
                SupplierGroupId = _context.Parties.Where(p=>p.PartyId.ToString()==invoice.VendorID).Select(p=>p.PartyCode).FirstOrDefault(),
                VendorName = vendorName ?? $"#{invoice.VendorID}",
                invoice.BillNo,
                invoice.BranchID,
                invoice.PaymentMode,
                invoice.Remarks,
                invoice.GSTPer,
                invoice.GSTAmount,
                invoice.FreightExp,
                invoice.OtherExp,
                invoice.Discount,
                invoice.TotalAmount,
                NetAmount = invoice.AmountPaid,
                Items = bodies
            });
        }

        // ── Print view ────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Print(int id)
        {
            var invoice = _context.PurchaseInvoice.IgnoreQueryFilters().Where(s => !_context.RestrictBranches || _context.AllowedBranchIds.Contains(s.BranchID ?? 1))
                .Include(p => p.PurchaseInvoiceBodies)
                .FirstOrDefault(p => p.PurchaseId == id);
            if (invoice == null) return NotFound();

            ViewBag.VendorName = _context.Parties
                .Where(v => v.PartyId.ToString() == invoice.VendorID)
                .Select(v => v.PartyName)
                .FirstOrDefault() ?? $"#{invoice.VendorID}";
            ViewBag.PartyContact = string.Join(" · ", _context.Parties.Where(p => p.PartyId.ToString() == invoice.VendorID).Select(p => new[] { p.Phone, p.Address }).FirstOrDefault()?.Where(x => !string.IsNullOrWhiteSpace(x)) ?? Enumerable.Empty<string?>());
            ViewBag.PaymentMode = GetPaymentModeLabel(invoice.PaymentMode);
            var itemIds=invoice.PurchaseInvoiceBodies.Where(b=>b.Itemid.HasValue).Select(b=>b.Itemid!.Value).Distinct().ToList();
            ViewBag.ItemUnits=_context.Items.Where(i=>itemIds.Contains(i.ItemID)).ToDictionary(i=>i.ItemID,i=>i.StockUnit);

            return View(invoice);
        }

        [HttpGet]
        public IActionResult GetNextInvoiceNumber()
        {
            var nextId = (_context.PurchaseInvoice.IgnoreQueryFilters().Max(c => (int?)c.PurchaseId) ?? 0) + 1;
            return Json(new { invoiceNo = nextId });
        }

        // ───────────────────────────────────────────────────────────────────────
        //  Private helpers — single source of truth for stock + ledger effects
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>Adds line items and increases stock (goods received).</summary>
        private async Task ApplyPurchaseLinesAndStockAsync(PurchaseInvoice invoice, List<PurchaseInvoiceBodyViewModel> validItems, int branchId, bool adjustStock = true)
        {
            foreach (var item in validItems)
            {
                _context.PurchaseInvoiceBody.Add(new PurchaseInvoiceBody
                {
                    PurchaseId = invoice.PurchaseId,
                    Itemid = item.Itemid,
                    Descr = item.Desc,
                    PackageCount = item.PackageCount, PackageType = item.PackageType,
                    Quantity = item.Quantity,
                    PurPrice = item.PurPrice,
                    SalePrice = item.SalePrice,
                    DiscPer = item.DiscPer,
                    DiscAmt = item.DiscAmt
                });

                var itemId = long.Parse(item.Itemid.ToString()!);
                if (adjustStock) await _stock.AdjustAsync(itemId, branchId, item.Quantity ?? 0);
            }
            _context.SaveChanges();
        }

        /// <summary>Posts the supplier payable + (for non-credit) the payment voucher and ledger.</summary>
        private async Task PostLedgerForPurchaseAsync(PurchaseInvoice invoice)
        {
            if (!int.TryParse(invoice.VendorID, out var vendorPartyId) || vendorPartyId <= 0)
                return;

            var modeLabel = GetPaymentModeLabel(invoice.PaymentMode);
            var entryDate = invoice.PurchaseDate ?? DateTime.Now;

            _context.AccountLedger.Add(new AccountLedger
            {
                PartyId = vendorPartyId,
                EntryDate = entryDate,
                TransactionType = TransactionTypes.PurchaseInvoice,
                ReferenceId = invoice.PurchaseId,
                Description = $"Purchase Invoice #{invoice.PurchaseId}" +
                                  (invoice.BillNo != null ? $" | Bill: {invoice.BillNo}" : ""),
                Debit = 0,
                Credit = invoice.AmountPaid ?? 0,   // we owe this amount
                BranchId = invoice.BranchID,
                CreatedDate = DateTime.Now
            });

            if (invoice.PaymentMode != 0)
            {
                var voucher = new PaymentVoucher
                {
                    PartyId = vendorPartyId,
                    VoucherType = "Payment",
                    VoucherDate = entryDate,
                    Amount = invoice.AmountPaid ?? 0,
                    PaymentMode = modeLabel,
                    ReferenceNo = invoice.BillNo,
                    PurchaseId = invoice.PurchaseId,
                    Notes = $"Auto-recorded — Invoice #{invoice.PurchaseId}",
                    CreatedDate = DateTime.Now
                };
                _context.PaymentVoucher.Add(voucher);
                _context.SaveChanges(); // materialise VoucherId

                _context.AccountLedger.Add(new AccountLedger
                {
                    PartyId = vendorPartyId,
                    EntryDate = entryDate,
                    TransactionType = TransactionTypes.Payment,
                    ReferenceId = voucher.VoucherId,
                    Description = $"{modeLabel} Payment | Invoice #{invoice.PurchaseId}",
                    Debit = invoice.AmountPaid ?? 0,   // we paid — reduces what we owe
                    Credit = 0,
                    BranchId = invoice.BranchID,
                    CreatedDate = DateTime.Now
                });
            }

            _context.SaveChanges();

            if (_accountingOptions.PostToGeneralLedger)
            {
                invoice.GLVoucherId = await _ledgerPosting.PostPurchaseAsync(invoice);
                _context.SaveChanges();
            }
        }

        /// <summary>Restores stock and removes all ledger/voucher/body rows tied to this invoice.</summary>
        private async Task ReversePurchaseEffectsAsync(PurchaseInvoice invoice, List<PurchaseInvoiceBody> bodies, bool keepBodies = false, bool adjustStock = true)
        {
            var branchId = invoice.BranchID ?? 1;

            // Restore stock: goods were added on save, so we subtract on reversal
            foreach (var body in adjustStock ? bodies : [])
            {
                if (body.Itemid == null) continue;
                await _stock.AdjustAsync(body.Itemid.Value, branchId, -(body.Quantity ?? 0));
            }

            // Remove the purchase-invoice payable ledger row(s)
            var purchaseLedgers = _context.AccountLedger
                .Where(l => l.TransactionType == TransactionTypes.PurchaseInvoice && l.ReferenceId == invoice.PurchaseId)
                .ToList();
            foreach (var historical in purchaseLedgers) historical.IsVoid = true;

            // Remove auto-payment voucher(s) + their ledger rows
            var vouchers = _context.PaymentVoucher.Where(v => v.PurchaseId == invoice.PurchaseId).ToList();
            foreach (var v in vouchers)
            {
                var paymentLedgers = _context.AccountLedger
                    .Where(l => l.TransactionType == TransactionTypes.Payment && l.ReferenceId == v.VoucherId)
                    .ToList();
                foreach (var historical in paymentLedgers) historical.IsVoid = true;
            }
            foreach (var historical in vouchers) historical.IsVoid = true;

            // Void the matching double-entry voucher, if one was posted
            if (_accountingOptions.PostToGeneralLedger && invoice.GLVoucherId != null)
            {
                await _ledgerPosting.VoidAsync(invoice.GLVoucherId);
                invoice.GLVoucherId = null;
            }

            // Remove the old line items
            if (!keepBodies) _context.PurchaseInvoiceBody.RemoveRange(bodies);
        }

        private static string GetPaymentModeLabel(int? mode) => mode switch
        {
            1 => "Cash",
            2 => "Cheque",
            3 => "Bank Transfer",
            _ => "Credit"
        };
    }
}
