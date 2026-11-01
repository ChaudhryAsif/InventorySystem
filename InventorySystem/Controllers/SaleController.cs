using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using InventorySystem.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class SaleController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILedgerPostingService _ledgerPosting;
        private readonly AccountingOptions _accountingOptions;
        private readonly IStockService _stock;

        public SaleController(ApplicationDbContext context, ILedgerPostingService ledgerPosting, IOptions<AccountingOptions> accountingOptions, IStockService stock)
        {
            _context = context;
            _ledgerPosting = ledgerPosting;
            _accountingOptions = accountingOptions.Value;
            _stock = stock;
        }

        [HttpGet]
        public IActionResult Index() => View();

        // ── Saved-invoice list page ───────────────────────────────────────────
        [HttpGet]
        public IActionResult List() => View();

        [HttpGet]
        public async Task<IActionResult> GetProductionOrders(int? saleId = null)
        {
            var orders = await _context.ProductionOrder.Where(o => (o.Status == "Completed" || o.Status == "InProduction") && o.UsesEstimatedConsumption && o.AgreedUnitPrice > 0 &&
                _context.Parties.Any(p => p.PartyId.ToString() == o.CustomerId && p.Status == "active" && (p.PartyType == "customer" || p.PartyType == "both")) &&
                _context.Items.Any(i => i.ItemID == o.FinishedItemId && i.IsActive == true &&
                    _context.ItemCategory.Any(c => c.CategoryId == i.CategoryID && c.IsActive == true)))
                .OrderByDescending(o => o.ProductionOrderId)
                .Select(o => new { o.ProductionOrderId, o.CustomerId, o.FinishedItemId, o.BranchId,
                    o.ProductName, o.ProducedQty, o.AgreedUnitPrice, o.AgreedGstPercent, o.CustomerReference,
                    CustomerName = _context.Parties.Where(p => p.PartyId.ToString() == o.CustomerId).Select(p => p.PartyName).FirstOrDefault(),
                    ItemName = _context.Items.Where(i => i.ItemID == o.FinishedItemId).Select(i => i.ItemName).FirstOrDefault(),
                    AvailableStock = _context.Stock.Where(s => s.ItemId == o.FinishedItemId && s.BranchId == o.BranchId).Sum(s => (decimal?)s.Quantity) ?? 0,
                    Invoiced = (from b in _context.SaleInvoiceBody join s in _context.SaleInvoice on b.SaleId equals s.SaleId
                        where s.ProductionOrderId == o.ProductionOrderId && (!saleId.HasValue || s.SaleId != saleId.Value) select b.Quantity ?? 0).Sum(),
                    EditQuantity = (from b in _context.SaleInvoiceBody join s in _context.SaleInvoice on b.SaleId equals s.SaleId
                        where saleId.HasValue && s.SaleId == saleId.Value && s.ProductionOrderId == o.ProductionOrderId select b.Quantity ?? 0).Sum()
                }).ToListAsync();
            return Json(new { success = true, orders = orders.Where(o => o.ProducedQty > o.Invoiced)
                .Select(o => new { o.ProductionOrderId, o.CustomerId, o.CustomerName, o.FinishedItemId, o.ItemName, o.BranchId,
                    o.AgreedUnitPrice, o.AgreedGstPercent, o.CustomerReference,
                    remainingQuantity = o.ProducedQty - o.Invoiced, availableStock = o.AvailableStock + o.EditQuantity }) });
        }

        [HttpGet]
        public async Task<IActionResult> GetCustomerGroups()
        {
            var groups=await _context.ItemCategory.Where(g=>g.GroupType=="FinishedGoods"&&g.IsActive==true).OrderBy(g=>g.CategoryName).ToListAsync();
            var parties=await _context.Parties.Where(p=>p.Status=="active"&&(p.PartyType=="customer"||p.PartyType=="both")).ToListAsync();
            return Json(new{success=true,customers=groups.Select(g=>new{partyId="group:"+g.CategoryId,groupId=g.CategoryId,partyName=g.CategoryName,partyType="customer",phone=g.ContactNumber,address=g.Address,contactPerson="",email="",openingBalance=parties.Where(p=>p.PartyCode=="FG-GROUP:"+g.CategoryId).Select(p=>p.OpeningBalance??0).FirstOrDefault()})});
        }
        [HttpGet]
        public async Task<IActionResult> GetCustomerGroup(string? customer)
        {
            try{return Json(new{success=true,groupId=await SaleGroupSelection.GroupIdAsync(_context,customer)});}catch(InvalidOperationException ex){return BadRequest(new{success=false,message=ex.Message});}
        }
        [HttpGet]
        public async Task<IActionResult> GetItemOptions(string? customer,int branchId=1,int page=1,int pageSize=200,string? search=null,long? itemId=null)
        {
            if(string.IsNullOrWhiteSpace(customer))return BadRequest(new{success=false,message="Select a Finished Goods customer group first."});
            int? group;try{group=await SaleGroupSelection.GroupIdAsync(_context,customer);}catch(InvalidOperationException ex){return BadRequest(new{success=false,message=ex.Message});}
            if(!group.HasValue&&(!int.TryParse(customer,out var partyId)||!await _context.Parties.AnyAsync(p=>p.PartyId==partyId&&p.Status=="active"&&(p.PartyType=="customer"||p.PartyType=="both"))))return BadRequest(new{success=false,message="Select a valid customer."});
            if(_context.RestrictBranches&&!_context.AllowedBranchIds.Contains(branchId))return Forbid();
            var q=_context.Items.Where(i=>i.IsActive==true&&i.ItemType=="FinishedGoods"&&i.Category.GroupType=="FinishedGoods"&&i.Category.IsActive==true);
            if(group.HasValue)q=q.Where(i=>i.CategoryID==group);if(itemId.HasValue)q=q.Where(i=>i.ItemID==itemId);
            if(!string.IsNullOrWhiteSpace(search))q=q.Where(i=>(i.ItemName??"").Contains(search)||i.Barcode==search);
            page=Math.Clamp(page,1,1000000);pageSize=Math.Clamp(pageSize,1,200);var totalCount=await q.CountAsync();
            var items=await q.OrderBy(i=>i.ItemName).ThenBy(i=>i.ItemID).Skip((page-1)*pageSize).Take(pageSize).Select(i=>new{itemID=i.ItemID,itemName=i.ItemName,categoryID=i.CategoryID,categoryName=i.Category.CategoryName,itemType=i.ItemType,stockUnit=i.StockUnit,barcode=i.Barcode,imageUrl=i.ImagePath,salePrice=i.SalePrice??0,currentStock=_context.Stock.Where(st=>st.ItemId==i.ItemID&&st.BranchId==branchId).Select(st=>(decimal?)st.Quantity).FirstOrDefault()??0}).ToListAsync();
            return Json(new{success=true,items,totalCount,page,pageSize});
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] SaleInvoiceViewModel model)
        {
            if (model == null || model.Items == null || !model.Items.Any())
                return BadRequest("Invalid sale data.");

            // Create cannot use another invoice's ID to exclude its billed quantity.
            model.SaleId = null;
            using var transaction = _context.Database.BeginAtomicTransaction();
            var branchId   = model.BranchID ?? 1;
            try { await SaleGroupSelection.ValidateItemsAsync(_context,model);
                if(model.CustomerID?.StartsWith("group:",StringComparison.Ordinal)==true)model.CustomerID=await FinishedGoodsCustomers.ResolveAsync(_context,model.CustomerID);
                await InvoiceValidation.Validate(_context, model); }
            catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }

            var validItems = model.Items.Where(i => i.ItemId != null && i.Quantity > 0).ToList();

            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            // ── Pre-validate ALL stock (grouped by item) before touching the database ──
            var stockError = ValidateStock(validItems, branchId);
            if (stockError != null)
                return Ok(new { success = false, message = stockError });

            // ── All checks passed — write inside a single transaction ─────────
            try
            {
                var invoice = new SaleInvoice
                {
                    SaleDate    = model.SaleDate,
                    CustomerID  = model.CustomerID,
                    BranchID    = model.BranchID,
                    PaymentMode = model.PaymentMode,
                    ProductionOrderId = model.ProductionOrderId,
                    RefNo       = model.RefNo,
                    GSTPer      = model.GSTPer,
                    GSTAmount   = model.GSTAmount,
                    Discount    = model.Discount,
                    FreightExp  = model.FreightExp,
                    OtherExp    = model.OtherExp,
                    TotalAmount = model.TotalAmount,
                    NetAmount   = model.NetAmount,
                    Remarks     = model.Remarks
                };

                _context.SaleInvoice.Add(invoice);
                _context.SaveChanges(); // materialise SaleId

                await ApplyInvoiceLinesAndStockAsync(invoice, validItems, branchId);
                await PostLedgerForSaleAsync(invoice);

                transaction.Commit();
                return Ok(new { success = true, message = "Sale invoice saved successfully", id = invoice.SaleId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error saving sale invoice: " + ex.Message });
            }
        }

        // ── UPDATE an existing invoice ────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] SaleInvoiceViewModel model)
        {
            if (model == null || model.SaleId is null or <= 0)
                return BadRequest("Invalid invoice id.");
            if (model.Items == null || !model.Items.Any())
                return BadRequest("Invalid sale data.");

            using var transaction = _context.Database.BeginAtomicTransaction();
            var branchId   = model.BranchID ?? 1;
            try { await InvoiceValidation.Validate(_context, model); }
            catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }

            var validItems = model.Items.Where(i => i.ItemId != null && i.Quantity > 0).ToList();
            if (!validItems.Any())
                return BadRequest("No valid items provided.");

            try
            {
                var invoice = _context.SaleInvoice.FirstOrDefault(s => s.SaleId == model.SaleId);
                if (invoice == null)
                    return Ok(new { success = false, message = "Invoice not found." });

                if (await _context.SaleReturn.AnyAsync(r => r.OriginalSaleId == invoice.SaleId))
                    return BadRequest(new { success = false, message = "Cancel linked returns before editing or cancelling this invoice." });

                var bodies = _context.SaleInvoiceBody.Where(b => b.SaleId == invoice.SaleId).ToList();

                // 1) Reverse the old invoice's effects (restore stock, drop ledger + bodies)
                await ReverseInvoiceEffectsAsync(invoice, bodies);
                _context.SaveChanges();

                // 2) Validate the new lines against the now-restored stock
                var stockError = ValidateStock(validItems, branchId);
                if (stockError != null)
                {
                    transaction.Rollback();
                    return Ok(new { success = false, message = stockError });
                }

                // 3) Update header + re-apply lines/stock/ledger
                invoice.SaleDate    = model.SaleDate;
                invoice.CustomerID  = model.CustomerID;
                invoice.BranchID    = model.BranchID;
                invoice.PaymentMode = model.PaymentMode;
                invoice.ProductionOrderId = model.ProductionOrderId;
                invoice.RefNo       = model.RefNo;
                invoice.GSTPer      = model.GSTPer;
                invoice.GSTAmount   = model.GSTAmount;
                invoice.Discount    = model.Discount;
                invoice.FreightExp  = model.FreightExp;
                invoice.OtherExp    = model.OtherExp;
                invoice.TotalAmount = model.TotalAmount;
                invoice.NetAmount   = model.NetAmount;
                invoice.Remarks     = model.Remarks;

                await ApplyInvoiceLinesAndStockAsync(invoice, validItems, branchId);
                await PostLedgerForSaleAsync(invoice);

                transaction.Commit();
                return Ok(new { success = true, message = "Sale invoice updated successfully", id = invoice.SaleId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error updating sale invoice: " + ex.Message });
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
                var invoice = _context.SaleInvoice.FirstOrDefault(s => s.SaleId == id);
                if (invoice == null)
                    return Ok(new { success = false, message = "Invoice not found." });

                if (await _context.SaleReturn.AnyAsync(r => r.OriginalSaleId == invoice.SaleId))
                    return BadRequest(new { success = false, message = "Cancel linked returns before editing or cancelling this invoice." });

                var bodies = _context.SaleInvoiceBody.Where(b => b.SaleId == id).ToList();

                await ReverseInvoiceEffectsAsync(invoice, bodies, keepBodies: true);
                invoice.IsCancelled = true;
                invoice.CancelledUtc = DateTime.UtcNow;
                invoice.CancelledBy = User.Identity?.Name;
                invoice.CancellationReason = reason;
                _context.SaveChanges();

                transaction.Commit();
                return Ok(new { success = true, message = "Sale invoice cancelled successfully." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { success = false, message = "Error deleting sale invoice: " + ex.Message });
            }
        }

        // ── LIST data (with optional search + date range) ─────────────────────
        [HttpGet]
        public IActionResult GetSales(string? search, DateTime? from, DateTime? to, int page = 1, int pageSize = 50, bool includeCancelled = false, int? orderId = null,
            string? customerId = null, int? paymentMode = null, string? status = null, long? itemId = null, decimal? minAmount = null, decimal? maxAmount = null)
        {
            status = string.IsNullOrWhiteSpace(status) ? (includeCancelled ? "all" : "active") : status.Trim().ToLowerInvariant();
            if (status is not ("active" or "cancelled" or "all")) status = "active";
            var query = status != "active"
                ? _context.SaleInvoice.IgnoreQueryFilters().Where(s => !_context.RestrictBranches || _context.AllowedBranchIds.Contains(s.BranchID ?? 1))
                : _context.SaleInvoice.AsQueryable();
            if (status == "cancelled") query = query.Where(s => s.IsCancelled);
            page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var parties = _context.Parties.Where(p => p.PartyName.Contains(term)).Select(p => p.PartyId.ToString());
                query = query.Where(s => parties.Contains(s.CustomerID) || (s.RefNo != null && s.RefNo.Contains(term)) || (s.Remarks != null && s.Remarks.Contains(term)) || s.SaleId.ToString().Contains(term)
                    || _context.SaleInvoiceBody.Any(b => b.SaleId == s.SaleId && b.Descr != null && b.Descr.Contains(term)));
            }


            if (orderId.HasValue) query = query.Where(s => s.ProductionOrderId == orderId.Value);
            if (!string.IsNullOrWhiteSpace(customerId)) query = query.Where(s => s.CustomerID == customerId);
            if (paymentMode.HasValue) query = paymentMode.Value is 1 or 2 or 3
                ? query.Where(s => s.PaymentMode == paymentMode.Value)
                : query.Where(s => s.PaymentMode != 1 && s.PaymentMode != 2 && s.PaymentMode != 3);
            if (itemId.HasValue) query = query.Where(s => _context.SaleInvoiceBody.Any(b => b.SaleId == s.SaleId && b.ItemId == itemId.Value));
            if (minAmount.HasValue) query = query.Where(s => s.NetAmount >= minAmount.Value);
            if (maxAmount.HasValue) query = query.Where(s => s.NetAmount <= maxAmount.Value);
            if (from.HasValue) query = query.Where(s => s.SaleDate >= from.Value.Date);
            if (to.HasValue)   query = query.Where(s => s.SaleDate < to.Value.Date.AddDays(1));

            var totalCount = query.Count();
            var totalNet = query.Where(s => !s.IsCancelled).Sum(s => (decimal?)(s.NetAmount ?? 0)) ?? 0m;
            var sales = query
                .OrderByDescending(s => s.SaleId)
                .Select(s => new
                {
                    s.SaleId,
                    s.ProductionOrderId,
                s.IsCancelled,
                    SaleDate = s.SaleDate,
                    s.CustomerID,
                    CustomerName = _context.Parties
                        .Where(p => p.PartyId.ToString() == s.CustomerID)
                        .Select(p => p.PartyName)
                        .FirstOrDefault(),
                    s.RefNo,
                    s.PaymentMode,
                    s.NetAmount,
                    ItemCount = _context.SaleInvoiceBody.Count(b => b.SaleId == s.SaleId)
                })
                .Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var result = sales.Select(s => new
            {
                s.SaleId,
                s.ProductionOrderId,
                s.IsCancelled,
                SaleDate = s.SaleDate.HasValue ? s.SaleDate.Value.ToString("yyyy-MM-dd") : "",
                CustomerName = s.CustomerName ?? $"#{s.CustomerID}",
                s.RefNo,
                PaymentMode = GetPaymentModeLabel(s.PaymentMode),
                s.NetAmount,
                s.ItemCount
            });

            var paged = result.ToList();

            return Json(new { data = paged, totalCount, totalNet, page, pageSize });
        }

        // Customers and items that appear on sales, for the Sale list filter dropdowns.
        [HttpGet]
        public IActionResult GetSaleFilterOptions()
        {
            var sales = _context.SaleInvoice.IgnoreQueryFilters().Where(s => !_context.RestrictBranches || _context.AllowedBranchIds.Contains(s.BranchID ?? 1));
            var customerIds = sales.Where(s => s.CustomerID != null).Select(s => s.CustomerID!).Distinct().ToList();
            var customers = _context.Parties.Where(p => customerIds.Contains(p.PartyId.ToString())).OrderBy(p => p.PartyName)
                .Select(p => new { id = p.PartyId.ToString(), name = p.PartyName }).ToList();
            var itemIds = _context.SaleInvoiceBody.Where(b => b.ItemId != null && sales.Any(s => s.SaleId == b.SaleId)).Select(b => b.ItemId!.Value).Distinct().ToList();
            var items = _context.Items.Where(i => itemIds.Contains(i.ItemID)).OrderBy(i => i.ItemName).Select(i => new { id = i.ItemID, name = i.ItemName }).ToList();
            return Json(new { success = true, customers, items });
        }

        // Production orders that have sales, for the Sale list order filter and its summary line.
        [HttpGet]
        public IActionResult GetSaleOrders(bool includeCancelled = false)
        {
            var sales = includeCancelled
                ? _context.SaleInvoice.IgnoreQueryFilters().Where(s => !_context.RestrictBranches || _context.AllowedBranchIds.Contains(s.BranchID ?? 1))
                : _context.SaleInvoice.AsQueryable();
            var ids = sales.Where(s => s.ProductionOrderId != null).Select(s => s.ProductionOrderId!.Value).Distinct().ToList();
            var orders = _context.ProductionOrder.Where(o => ids.Contains(o.ProductionOrderId))
                .OrderByDescending(o => o.ProductionOrderId)
                .Select(o => new
                {
                    o.ProductionOrderId, o.ProductName, o.OrderQty, o.ProducedQty,
                    CustomerName = _context.Parties.Where(p => p.PartyId.ToString() == o.CustomerId).Select(p => p.PartyName).FirstOrDefault(),
                    Invoiced = _context.SaleInvoiceBody.Where(b => _context.SaleInvoice.Any(x => x.SaleId == b.SaleId && x.ProductionOrderId == o.ProductionOrderId)).Sum(b => (decimal?)(b.Quantity ?? 0)) ?? 0m,
                    InvoicedAmount = _context.SaleInvoice.Where(x => x.ProductionOrderId == o.ProductionOrderId).Sum(x => (decimal?)(x.NetAmount ?? 0)) ?? 0m
                }).ToList();
            return Json(new { success = true, orders });
        }

        // ── Single invoice (for view + edit) ──────────────────────────────────
        [HttpGet]
        public IActionResult GetSaleById(int id)
        {
            var invoice = _context.SaleInvoice.FirstOrDefault(s => s.SaleId == id);
            if (invoice == null) return NotFound();

            var customerName = _context.Parties
                .Where(p => p.PartyId.ToString() == invoice.CustomerID)
                .Select(p => p.PartyName)
                .FirstOrDefault();

            var branchId = invoice.BranchID ?? 1;

            var bodies = _context.SaleInvoiceBody
                .Where(b => b.SaleId == id)
                .Select(b => new
                {
                    b.ItemId,
                    StockUnit=_context.Items.Where(i=>i.ItemID==b.ItemId).Select(i=>i.StockUnit).FirstOrDefault(),
                    b.Descr,
                    b.Quantity,
                    b.SalePrice,
                    b.DiscPer,
                    b.DiscAmt,
                    b.Total,
                    // For edit mode: max sellable = current stock + qty already on this invoice
                    AvailStock = (_context.Stock
                        .Where(s => s.ItemId == b.ItemId && s.BranchId == branchId)
                        .Select(s => (decimal?)s.Quantity)
                        .FirstOrDefault() ?? 0) + (b.Quantity ?? 0)
                })
                .ToList();

            return Json(new
            {
                invoice.SaleId,
                invoice.ProductionOrderId,
                SaleDate = invoice.SaleDate.HasValue ? invoice.SaleDate.Value.ToString("yyyy-MM-dd") : "",
                invoice.CustomerID,
                CustomerGroupId=CustomerGroupId(invoice.CustomerID),
                CustomerName = customerName ?? $"#{invoice.CustomerID}",
                invoice.BranchID,
                invoice.PaymentMode,
                invoice.RefNo,
                invoice.GSTPer,
                invoice.GSTAmount,
                invoice.Discount,
                invoice.FreightExp,
                invoice.OtherExp,
                invoice.TotalAmount,
                invoice.NetAmount,
                invoice.Remarks,
                Items = bodies
            });
        }

        private int? CustomerGroupId(string? customer)
        {
            var code=_context.Parties.Where(p=>p.PartyId.ToString()==customer).Select(p=>p.PartyCode).FirstOrDefault();
            return code?.StartsWith("FG-GROUP:",StringComparison.Ordinal)==true&&int.TryParse(code[9..],out var id)?id:null;
        }

        // ── Print view ────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Print(int id)
        {
            var invoice = _context.SaleInvoice.IgnoreQueryFilters().Where(s => !_context.RestrictBranches || _context.AllowedBranchIds.Contains(s.BranchID ?? 1))
                .Include(s => s.SaleInvoiceBodies)
                .FirstOrDefault(s => s.SaleId == id);
            if (invoice == null) return NotFound();

            ViewBag.CustomerName = _context.Parties
                .Where(p => p.PartyId.ToString() == invoice.CustomerID)
                .Select(p => p.PartyName)
                .FirstOrDefault() ?? $"#{invoice.CustomerID}";
            ViewBag.PartyContact = string.Join(" · ", _context.Parties.Where(p => p.PartyId.ToString() == invoice.CustomerID).Select(p => new[] { p.Phone, p.Address }).FirstOrDefault()?.Where(x => !string.IsNullOrWhiteSpace(x)) ?? Enumerable.Empty<string?>());
            ViewBag.PaymentMode = GetPaymentModeLabel(invoice.PaymentMode);
            var itemIds=invoice.SaleInvoiceBodies.Select(b=>b.ItemId).ToArray();ViewBag.ItemUnits=_context.Items.Where(i=>itemIds.Contains(i.ItemID)).ToDictionary(i=>i.ItemID,i=>i.StockUnit);

            return View(invoice);
        }

        [HttpGet]
        public IActionResult GetNextInvoiceNumber()
        {
            var nextId = (_context.SaleInvoice.IgnoreQueryFilters().Max(s => (int?)s.SaleId) ?? 0) + 1;
            return Json(new { invoiceNo = nextId });
        }

        [HttpGet]
        public IActionResult GetStockByItem(long itemId, int branchId = 1)
        {
            var stock = _context.Stock
                .FirstOrDefault(s => s.ItemId == itemId && s.BranchId == branchId);
            return Json(new { quantity = stock?.Quantity ?? 0 });
        }

        // ───────────────────────────────────────────────────────────────────────
        //  Private helpers — single source of truth for stock + ledger effects
        // ───────────────────────────────────────────────────────────────────────

        /// <summary>Returns an error message if any grouped line exceeds stock, else null.</summary>
        private string? ValidateStock(List<SaleItemViewModel> validItems, int branchId)
        {
            var groupedItems = validItems
                .GroupBy(i => i.ItemId)
                .Select(g => new { ItemId = g.Key, TotalQty = g.Sum(x => x.Quantity), Desc = g.First().Desc });

            foreach (var group in groupedItems)
            {
                var stock = _context.Stock
                    .FirstOrDefault(s => s.ItemId == group.ItemId && s.BranchId == branchId);

                if (stock == null || stock.Quantity < group.TotalQty)
                    return $"Insufficient stock for \"{group.Desc ?? group.ItemId.ToString()}\". " +
                           $"Available: {stock?.Quantity ?? 0}, Requested: {group.TotalQty}";
            }
            return null;
        }

        /// <summary>Adds line items and deducts stock. Caller must have validated stock first.</summary>
        private async Task ApplyInvoiceLinesAndStockAsync(SaleInvoice invoice, List<SaleItemViewModel> validItems, int branchId)
        {
            foreach (var item in validItems)
            {
                _context.SaleInvoiceBody.Add(new SaleInvoiceBody
                {
                    SaleId    = invoice.SaleId,
                    ItemId    = item.ItemId,
                    Descr     = item.Desc,
                    Quantity  = item.Quantity,
                    SalePrice = item.SalePrice,
                    DiscPer   = item.DiscPer,
                    DiscAmt   = item.DiscAmt,
                    Total     = item.Total
                });

                await _stock.AdjustAsync(item.ItemId!.Value, branchId, -item.Quantity);
            }
            _context.SaveChanges();
        }

        /// <summary>Posts the customer receivable + (for non-credit) the receipt voucher and ledger.</summary>
        private async Task PostLedgerForSaleAsync(SaleInvoice invoice)
        {
            if (!int.TryParse(invoice.CustomerID, out var customerPartyId) || customerPartyId <= 0)
                return;

            var modeLabel = GetPaymentModeLabel(invoice.PaymentMode);
            var entryDate = invoice.SaleDate ?? DateTime.Now;

            _context.AccountLedger.Add(new AccountLedger
            {
                PartyId         = customerPartyId,
                EntryDate       = entryDate,
                TransactionType = TransactionTypes.SaleInvoice,
                ReferenceId     = invoice.SaleId,
                Description     = $"Sale Invoice #{invoice.SaleId}" +
                                  (invoice.RefNo != null ? $" | Ref: {invoice.RefNo}" : ""),
                Debit           = invoice.NetAmount ?? 0,
                Credit          = 0,
                BranchId        = invoice.BranchID,
                CreatedDate     = DateTime.Now
            });

            if (invoice.PaymentMode != 0)
            {
                var voucher = new PaymentVoucher
                {
                    PartyId     = customerPartyId,
                    VoucherType = "Receipt",
                    VoucherDate = entryDate,
                    Amount      = invoice.NetAmount ?? 0,
                    PaymentMode = modeLabel,
                    SaleId      = invoice.SaleId,
                    Notes       = $"Auto-recorded — Sale #{invoice.SaleId}",
                    CreatedDate = DateTime.Now
                };
                _context.PaymentVoucher.Add(voucher);
                _context.SaveChanges(); // materialise VoucherId

                _context.AccountLedger.Add(new AccountLedger
                {
                    PartyId         = customerPartyId,
                    EntryDate       = entryDate,
                    TransactionType = TransactionTypes.Receipt,
                    ReferenceId     = voucher.VoucherId,
                    Description     = $"{modeLabel} Receipt | Sale #{invoice.SaleId}",
                    Debit           = 0,
                    Credit          = invoice.NetAmount ?? 0,
                    BranchId        = invoice.BranchID,
                    CreatedDate     = DateTime.Now
                });
            }

            _context.SaveChanges();

            if (_accountingOptions.PostToGeneralLedger)
            {
                invoice.GLVoucherId = await _ledgerPosting.PostSaleAsync(invoice);
                _context.SaveChanges();
            }
        }

        /// <summary>Restores stock and removes all ledger/voucher/body rows tied to this invoice.</summary>
        private async Task ReverseInvoiceEffectsAsync(SaleInvoice invoice, List<SaleInvoiceBody> bodies, bool keepBodies = false)
        {
            var branchId = invoice.BranchID ?? 1;

            // Restore stock
            foreach (var body in bodies)
            {
                if (body.ItemId == null) continue;
                await _stock.AdjustAsync(body.ItemId.Value, branchId, body.Quantity ?? 0);
            }

            // Remove the sale-invoice receivable ledger row(s)
            var saleLedgers = _context.AccountLedger
                .Where(l => l.TransactionType == TransactionTypes.SaleInvoice && l.ReferenceId == invoice.SaleId)
                .ToList();
            foreach (var historical in saleLedgers) historical.IsVoid = true;

            // Remove auto-receipt voucher(s) + their ledger rows
            var vouchers = _context.PaymentVoucher.Where(v => v.SaleId == invoice.SaleId).ToList();
            foreach (var v in vouchers)
            {
                var receiptLedgers = _context.AccountLedger
                    .Where(l => l.TransactionType == TransactionTypes.Receipt && l.ReferenceId == v.VoucherId)
                    .ToList();
                foreach (var historical in receiptLedgers) historical.IsVoid = true;
            }
            foreach (var historical in vouchers) historical.IsVoid = true;

            // Void the matching double-entry voucher, if one was posted
            if (_accountingOptions.PostToGeneralLedger && invoice.GLVoucherId != null)
            {
                await _ledgerPosting.VoidAsync(invoice.GLVoucherId);
                invoice.GLVoucherId = null;
            }

            // Remove the old line items
            if (!keepBodies) _context.SaleInvoiceBody.RemoveRange(bodies);
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
