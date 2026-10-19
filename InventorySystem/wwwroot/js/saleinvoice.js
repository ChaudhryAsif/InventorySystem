// ── Sale Invoice column map ─────────────────────────────────────────────────
//  cells[0]  #
//  cells[1]  Item ID input + 🔍 lookup button
//  cells[2]  Item Name (readonly)
//  cells[3]  Avail. Stock (readonly, green tint)   ← extra vs purchase
//  cells[4]  Quantity                              data-field="qty"
//  cells[5]  Sale Price                            data-field="price"
//  cells[6]  Disc %                                data-field="disc"
//  cells[7]  Disc Amt (readonly, calculated)
//  cells[8]  Total    (readonly, calculated)
//  cells[9]  Remove button
// ───────────────────────────────────────────────────────────────────────────

// Self-contained placeholder — via.placeholder.com is no longer online, so items
// without a real photo must fall back to something that doesn't depend on the network.
const NO_IMAGE_PLACEHOLDER = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='100'%3E%3Crect width='100' height='100' fill='%23e2e8f0'/%3E%3Ccircle cx='38' cy='36' r='8' fill='%23ffffff'/%3E%3Cpath d='M20 78 L42 50 L58 68 L72 48 L90 78 Z' fill='%23ffffff'/%3E%3C/svg%3E";

let rowCounter    = 0;
let selectedCustomer = null;
let allCustomers  = [];
let selectedItem  = null;
let allItems      = [];
let currentRow    = null;
let editId        = null;   // set when editing an existing invoice (?id=)
let productionOrders = [];
let linkedProductionOrder = null;
let savedProductionOrderId = null;
let orderSelectionVersion = 0;

// ── Init ────────────────────────────────────────────────────────────────────
window.addEventListener('DOMContentLoaded', async function () {
    const id = new URLSearchParams(window.location.search).get('id');
    if (id) {
        editId = parseInt(id);
        await loadInvoiceForEdit(editId);
        await loadProductionOrders(savedProductionOrderId, true);
    } else {
        addNewRow();
        loadNextInvoiceNumber();
        await loadProductionOrders(new URLSearchParams(location.search).get('productionOrderId'));
    }
});

// ── Load an existing invoice into the form (edit mode) ───────────────────────
async function loadInvoiceForEdit(id) {
    try {
        const res = await fetch('/Sale/GetSaleById?id=' + id);
        if (!res.ok) { await alertDialog('Invoice not found.', { type: 'error' }); window.location.href = '/Sale/List'; return; }
        const inv = await res.json();
        savedProductionOrderId = inv.productionOrderId || null;

        const header = document.querySelector('.page-header h2');
        if (header) header.textContent = '✏️ Edit Sale Invoice #' + inv.saleId;

        document.getElementById('invoiceNo').value   = inv.saleId;
        document.getElementById('refNo').value        = inv.refNo || '';
        if (inv.saleDate) document.getElementById('invoiceDate').value = inv.saleDate;
        if (document.getElementById("posBranch")) document.getElementById("posBranch").value = inv.branchID || 1;
        document.getElementById('paymentMode').value  = (inv.paymentMode ?? 0).toString();

        document.getElementById('hftxtCustomerId').value = inv.customerID || '';
        document.getElementById('customerAccount').value = inv.customerName || '';
        document.getElementById('customerDetails').value = '';
        document.getElementById('remarks').value         = inv.remarks || '';

        document.getElementById('gstPercent').value  = inv.gstPer || 0;
        document.getElementById('freight').value     = inv.freightExp || 0;
        document.getElementById('otherExp').value    = inv.otherExp || 0;
        const total = parseFloat(inv.totalAmount) || 0;
        const discPct = total > 0 ? ((parseFloat(inv.discount) || 0) / total) * 100 : 0;
        document.getElementById('discPercent').value = discPct.toFixed(2);

        // Rebuild item rows
        document.getElementById('itemsBody').innerHTML = '';
        rowCounter = 0;
        (inv.items || []).forEach(it => {
            addNewRow();
            const row = document.querySelector('#itemsBody tr:last-child');
            const avail = parseFloat(it.availStock) || 0;
            row.cells[1].querySelector('input').value = it.itemId;
            row.cells[2].querySelector('input').value = it.descr || '';
            row.cells[3].querySelector('input').value = avail;
            const qtyInput = row.cells[4].querySelector('input');
            qtyInput.value = it.quantity;
            qtyInput.max   = avail;
            row.cells[5].querySelector('input').value = parseFloat(it.salePrice || 0).toFixed(2);
            row.cells[6].querySelector('input').value = it.discPer || 0;
            calculateRowTotal(row);
        });
        if ((inv.items || []).length === 0) addNewRow();

        calculateTotals();
    } catch (e) {
        console.error(e);
        await alertDialog('Error loading invoice for edit.', { type: 'error' });
        window.location.href = '/Sale/List';
    }
}

// ── Row management ──────────────────────────────────────────────────────────
document.getElementById('addRowBtn').addEventListener('click', addNewRow);

function addNewRow() {
    if (linkedProductionOrder && document.getElementById('itemsBody').rows.length > 0) return;
    rowCounter++;
    const tbody = document.getElementById('itemsBody');
    const newRow = document.createElement('tr');

    newRow.innerHTML = `
        <td>${rowCounter}</td>
        <td>
            <div style="display: flex; align-items: center; gap: 0.3rem;">
                <input type="text" class="table-input" placeholder="ID" readonly style="flex: 1;">
                <button type="button" class="item-lookup-icon" onclick="openItemLookup(this)">🔍</button>
            </div>
        </td>
        <td><input type="text" class="table-input" placeholder="Enter item name" readonly></td>
        <td><input type="text" class="table-input stock-avail" value="0" readonly></td>
        <td><input type="number" class="table-input" value="0" min="0" step="0.01" data-field="qty"></td>
        <td><input type="number" class="table-input" value="0" step="0.01" data-field="price"></td>
        <td><input type="number" class="table-input highlight" value="0" min="0" max="100" data-field="disc"></td>
        <td><input type="number" class="table-input" value="0" readonly></td>
        <td><input type="number" class="table-input" value="0" readonly></td>
        <td><button type="button" class="btn-remove">×</button></td>
    `;

    tbody.appendChild(newRow);

    newRow.querySelectorAll('input[data-field]').forEach(input => {
        input.addEventListener('input', function () {
            calculateRowTotal(newRow);
        });
    });

    newRow.querySelector('.btn-remove').addEventListener('click', function () {
        removeRow(newRow);
    });
}

async function removeRow(row) {
    if (document.getElementById('itemsBody').rows.length > 1) {
        if (await confirmDialog('Remove this item?', { danger: true })) {
            row.remove();
            updateRowNumbers();
            calculateTotals();
        }
    } else {
        await alertDialog('At least one row is required!', { type: 'warning' });
    }
}

function updateRowNumbers() {
    const rows = document.querySelectorAll('#itemsBody tr');
    rows.forEach((row, index) => {
        row.cells[0].textContent = index + 1;
    });
    rowCounter = rows.length;
}

// ── Calculations ────────────────────────────────────────────────────────────
// ── Calculations ────────────────────────────────────────────────────────────
function calculateRowTotal(row) {
    const qtyInput = row.cells[4].querySelector('input');
    const avail = parseFloat(row.cells[3].querySelector('input').value) || 0;
    let qty = parseFloat(qtyInput.value) || 0;

    // ── Clamp qty to available stock ──────────────────────────────────────
    if (avail > 0 && qty > avail) {
        qty = avail;
        qtyInput.value = avail;
        qtyInput.style.outline = '2px solid #dc3545';
        qtyInput.title = `⚠️ Max available stock: ${avail}`;
    } else {
        qtyInput.style.outline = '';
        qtyInput.title = avail > 0 ? `Max available: ${avail}` : '';
    }

    const price = parseFloat(row.cells[5].querySelector('input').value) || 0;
    const discPct = parseFloat(row.cells[6].querySelector('input').value) || 0;

    const subtotal = qty * price;
    const discAmt = (subtotal * discPct) / 100;
    const total = subtotal - discAmt;

    row.cells[7].querySelector('input').value = discAmt.toFixed(2);
    row.cells[8].querySelector('input').value = total.toFixed(2);

    calculateTotals();
}

function calculateTotals() {
    const rows = document.querySelectorAll('#itemsBody tr');
    let totalQty = 0, invoiceTotal = 0;

    rows.forEach(row => {
        totalQty     += parseFloat(row.cells[4].querySelector('input').value) || 0;
        invoiceTotal += parseFloat(row.cells[8].querySelector('input').value) || 0;
    });

    const gstPct   = parseFloat(document.getElementById('gstPercent').value) || 0;
    const freight  = parseFloat(document.getElementById('freight').value)    || 0;
    const otherExp = parseFloat(document.getElementById('otherExp').value)   || 0;
    const discPct  = parseFloat(document.getElementById('discPercent').value) || 0;

    const gstAmt   = (invoiceTotal * gstPct)  / 100;
    const discAmt  = (invoiceTotal * discPct) / 100;
    const netAmt   = invoiceTotal + gstAmt + freight + otherExp - discAmt;

    document.getElementById('totalQty').value        = totalQty.toFixed(0);
    document.getElementById('invoiceTotal').value    = invoiceTotal.toFixed(2);
    document.getElementById('gstAmount').value       = gstAmt.toFixed(2);
    document.getElementById('discAmount').value      = discAmt.toFixed(2);
    document.getElementById('netAmount').textContent = netAmt.toFixed(2);
}

document.getElementById('gstPercent').addEventListener('input', calculateTotals);
document.getElementById('freight').addEventListener('input',    calculateTotals);
document.getElementById('otherExp').addEventListener('input',   calculateTotals);
document.getElementById('discPercent').addEventListener('input', calculateTotals);

// ── Invoice number ──────────────────────────────────────────────────────────
function loadNextInvoiceNumber() {
    fetch('/Sale/GetNextInvoiceNumber')
        .then(res => res.json())
        .then(data => { document.getElementById('invoiceNo').value = data.invoiceNo; })
        .catch(err => console.error('Error fetching invoice number:', err));
}

// ── Reset form ──────────────────────────────────────────────────────────────
function resetForm() {
    if (window.posMarkSaved) window.posMarkSaved();
    editId = null;
    savedProductionOrderId = null;
    linkedProductionOrder = null;
    document.querySelector('#saleForm button[type="submit"]').disabled = false;
    document.getElementById('productionOrderSelect').disabled = false;
    document.getElementById('productionOrderSelect').value = '';
    document.getElementById('productionOrderInfo').textContent = 'Normal sale. Select an order to load agreed pricing.';
    setOrderLocks(false);
    const header = document.querySelector('.page-header h2');
    if (header) header.textContent = '🛒 Sale Invoice';
    document.getElementById('refNo').value           = '';
    document.getElementById('invoiceDate').value     = new Date().toISOString().split('T')[0];
    document.getElementById('customerAccount').value = '';
    document.getElementById('hftxtCustomerId').value = '';
    document.getElementById('customerDetails').value = '';
    document.getElementById('remarks').value         = '';
    document.getElementById('paymentMode').selectedIndex = 0;

    selectedCustomer = null;

    document.getElementById('itemsBody').innerHTML = '';
    rowCounter = 0;
    addNewRow();

    document.getElementById('gstPercent').value  = '0';
    document.getElementById('freight').value     = '0';
    document.getElementById('otherExp').value    = '0';
    document.getElementById('discPercent').value = '0';
    calculateTotals();
    loadNextInvoiceNumber();
    loadProductionOrders();
}

async function loadProductionOrders(selectedId = null, preserveLines = false) {
    const version = ++orderSelectionVersion;
    const select = document.getElementById('productionOrderSelect');
    select.disabled = true;
    try {
        const response = await fetch('/Sale/GetProductionOrders' + (editId ? '?saleId=' + editId : ''));
        const data = await response.json();
        if (!response.ok || !data.success) throw new Error('Orders could not be loaded.');
        if (version !== orderSelectionVersion) return;
        productionOrders = data.orders || [];
        select.replaceChildren(new Option('Normal sale — no production order', ''));
        for (const order of productionOrders) {
            select.add(new Option(`#${order.productionOrderId} — ${order.customerName} — ${order.itemName} — ${order.remainingQuantity} boxes remaining`, order.productionOrderId));
        }
        if (selectedId) {
            select.value = String(selectedId);
            if (!select.value) throw new Error('This order is unavailable or fully invoiced. Review its status and remaining quantity.');
            await selectProductionOrder(preserveLines);
        }
    } catch (error) {
        document.getElementById('productionOrderInfo').textContent = error.message + ' Refresh the page before invoicing an order.';
        // Preserve the stored link even if lookup fails; never silently convert an edit into a normal sale.
        if (savedProductionOrderId) document.querySelector('#saleForm button[type="submit"]').disabled = true;
    } finally {
        if (version === orderSelectionVersion) select.disabled = !!savedProductionOrderId;
    }
}

function setOrderLocks(locked) {
    document.getElementById('btnCustomerAccount').disabled = locked;
    document.getElementById('addRowBtn').disabled = locked;
    document.getElementById('scanInput').disabled = locked;
    for (const id of ['gstPercent','freight','otherExp','discPercent']) document.getElementById(id).readOnly = locked;
    document.querySelectorAll('#itemsBody .item-lookup-icon, #itemsBody .btn-remove').forEach(b => b.disabled = locked);
    document.querySelectorAll('#itemsBody input[data-field="price"], #itemsBody input[data-field="disc"]').forEach(i => i.readOnly = locked);
}

async function selectProductionOrder(preserveLines = false) {
    const select = document.getElementById('productionOrderSelect');
    const order = productionOrders.find(o => String(o.productionOrderId) === select.value);
    const previous = linkedProductionOrder;
    const hasItems = [...document.querySelectorAll('#itemsBody tr')].some(row => row.cells[1].querySelector('input').value);
    if (!preserveLines && hasItems && previous?.productionOrderId !== order?.productionOrderId) {
        if (!await confirmDialog('Changing the production order will replace the customer, items, quantities and pricing in this unsaved invoice. Continue?')) {
            select.value = previous ? String(previous.productionOrderId) : ''; return;
        }
    }
    linkedProductionOrder = order || null;
    if (!order) {
        setOrderLocks(false);
        document.getElementById('productionOrderInfo').textContent = 'Normal sale. Select an order to load agreed pricing.';
        if (!preserveLines) {
            document.getElementById('itemsBody').replaceChildren(); rowCounter = 0; addNewRow();
            document.getElementById('hftxtCustomerId').value = '';
            document.getElementById('customerAccount').value = '';
            document.getElementById('customerDetails').value = '';
            for (const id of ['gstPercent','freight','otherExp','discPercent']) document.getElementById(id).value = 0;
            calculateTotals();
        }
        return;
    }
    document.getElementById('hftxtCustomerId').value = order.customerId;
    document.getElementById('customerAccount').value = order.customerName || '';
    document.getElementById('customerDetails').value = order.customerReference ? 'Customer reference: ' + order.customerReference : '';
    if (document.getElementById('posBranch')) document.getElementById('posBranch').value = order.branchId;
    document.getElementById('gstPercent').value = order.agreedGstPercent;
    for (const id of ['freight','otherExp','discPercent']) document.getElementById(id).value = 0;
    if (!preserveLines) {
        document.getElementById('itemsBody').replaceChildren(); rowCounter = 0; addNewRow();
        const row = document.querySelector('#itemsBody tr');
        row.cells[1].querySelector('input').value = order.finishedItemId;
        row.cells[2].querySelector('input').value = order.itemName;
        row.cells[3].querySelector('input').value = order.availableStock;
        const qty = row.cells[4].querySelector('input');
        qty.value = Math.max(0, Math.min(order.remainingQuantity, order.availableStock));
        qty.max = order.remainingQuantity; qty.step = '1';
        row.cells[5].querySelector('input').value = Number(order.agreedUnitPrice).toFixed(2);
        row.cells[6].querySelector('input').value = 0;
        calculateRowTotal(row);
    }
    document.getElementById('productionOrderInfo').textContent = `Order #${order.productionOrderId}: agreed price ${Number(order.agreedUnitPrice).toFixed(2)} per box before GST; GST ${order.agreedGstPercent}%; ${order.remainingQuantity} boxes remaining to invoice. Customer reference: ${order.customerReference || '—'}. Price, customer and item are fixed for this order-linked sale.`;
    setOrderLocks(true);
    calculateTotals();
}

// ── New invoice ─────────────────────────────────────────────────────────────
document.getElementById('newBtn').addEventListener('click', async function () {
    if (await confirmDialog('Create new invoice? Unsaved changes will be lost.')) {
        resetForm();
    }
});

// ── Form submit ─────────────────────────────────────────────────────────────
document.getElementById('saleForm').addEventListener('submit', async function (e) {
    e.preventDefault();

    const customerId = document.getElementById('hftxtCustomerId').value;
    if (!customerId) {
        await alertDialog('Please select a customer!', { type: 'warning' });
        return;
    }

    const rows = document.querySelectorAll('#itemsBody tr');
    let hasValidItem = false;
    const stockErrors = [];

    rows.forEach(row => {
        const itemName = row.cells[2].querySelector('input').value;
        const avail    = parseFloat(row.cells[3].querySelector('input').value) || 0;
        const qty      = parseFloat(row.cells[4].querySelector('input').value) || 0;

        if (itemName && qty > 0) {
            hasValidItem = true;
            if (qty > avail) {
                stockErrors.push(`"${itemName}": Requested ${qty}, Available ${avail}`);
            }
        }
    });

    if (!hasValidItem) {
        await alertDialog('Please add at least one item with quantity!', { type: 'warning' });
        return;
    }

    if (stockErrors.length > 0) {
        await alertDialog('⚠️ Insufficient stock:\n\n' + stockErrors.join('\n'));
        return;
    }

    // Build items array (skip empty rows)
    const items = [];
    rows.forEach(row => {
        const itemId = row.cells[1].querySelector('input').value;
        const qty    = parseFloat(row.cells[4].querySelector('input').value) || 0;
        if (!itemId || qty <= 0) return;

        items.push({
            ItemId:    parseInt(itemId),
            Desc:      row.cells[2].querySelector('input').value,
            Quantity:  qty,
            SalePrice: parseFloat(row.cells[5].querySelector('input').value) || 0,
            DiscPer:   parseFloat(row.cells[6].querySelector('input').value) || 0,
            DiscAmt:   parseFloat(row.cells[7].querySelector('input').value) || 0,
            Total:     parseFloat(row.cells[8].querySelector('input').value) || 0
        });
    });

    const payload = {
        SaleId:      editId,
        ProductionOrderId: linkedProductionOrder?.productionOrderId || savedProductionOrderId || null,
        SaleDate:    document.getElementById('invoiceDate').value,
        CustomerID:  customerId,
        BranchID:    Number(document.getElementById("posBranch")?.value || 1),
        PaymentMode: parseInt(document.getElementById('paymentMode').value),
        RefNo:       document.getElementById('refNo').value || null,
        Remarks:     document.getElementById('remarks').value || null,
        GSTPer:      parseFloat(document.getElementById('gstPercent').value)  || 0,
        GSTAmount:   parseFloat(document.getElementById('gstAmount').value)   || 0,
        FreightExp:  parseFloat(document.getElementById('freight').value)     || 0,
        OtherExp:    parseFloat(document.getElementById('otherExp').value)    || 0,
        Discount:    parseFloat(document.getElementById('discAmount').value)  || 0,
        TotalAmount: parseFloat(document.getElementById('invoiceTotal').value)|| 0,
        NetAmount:   parseFloat(document.getElementById('netAmount').textContent) || 0,
        Items:       items
    };

    const url = editId ? '/Sale/Update' : '/Sale/Save';
    fetch(url, {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify(payload)
    })
        .then(res => res.json())
        .then(async data => {
            if (data.success) {
                if (window.posMarkSaved) window.posMarkSaved();
                await alertDialog('✅ ' + data.message);
                if (editId) {
                    window.location.href = '/Sale/List';
                } else {
                    resetForm();
                }
            } else {
                await alertDialog('❌ ' + (data.message || 'Unknown error'));
            }
        })
        .catch(async err => {
            console.error(err);
            await alertDialog('Error saving sale invoice.', { type: 'error' });
        });
});

// ── Customer modal ──────────────────────────────────────────────────────────
document.getElementById('btnCustomerAccount').addEventListener('click', function (e) {
    e.preventDefault();
    openCustomerModal();
});

function openCustomerModal() {
    document.getElementById('customerModal').classList.add('show');
    loadCustomers();
}

function closeCustomerModal() {
    document.getElementById('customerModal').classList.remove('show');
    selectedCustomer = null;
    document.getElementById('selectCustomerBtn').disabled = true;
    document.querySelectorAll('#customerTableBody tr').forEach(r => r.classList.remove('selected'));
}

function loadCustomers() {
    const tbody = document.getElementById('customerTableBody');
    tbody.innerHTML = '<tr><td colspan="7" style="text-align: center; padding: 2rem;">Loading customers...</td></tr>';

    fetch('/Parties/GetCustomers')
        .then(response => response.json())
        .then(data => {
            if (data.success && data.customers && data.customers.length > 0) {
                allCustomers = data.customers;
                displayCustomers(allCustomers);
            } else {
                showCustomerEmptyState();
            }
        })
        .catch(error => {
            console.error('Error loading customers:', error);
            tbody.innerHTML = '<tr><td colspan="7" style="text-align: center; padding: 2rem; color: #dc3545;">Error loading customers. Please try again.</td></tr>';
        });
}

function displayCustomers(customers) {
    const tbody      = document.getElementById('customerTableBody');
    const emptyState = document.getElementById('customerEmptyState');

    if (customers.length === 0) { showCustomerEmptyState(); return; }

    emptyState.style.display = 'none';
    tbody.innerHTML = '';

    customers.forEach(customer => {
        const row = document.createElement('tr');
        row.setAttribute('data-customer-id', customer.partyId);
        row.innerHTML = `
            <td>${customer.partyId}</td>
            <td>${escapeHtml(customer.partyName)}</td>
            <td><span class="party-badge party-badge-${customer.partyType}">${formatPartyType(customer.partyType)}</span></td>
            <td>${escapeHtml(customer.contactPerson || '-')}</td>
            <td>${escapeHtml(customer.phone || '-')}</td>
            <td>${escapeHtml(customer.email || '-')}</td>
            <td>${parseFloat(customer.openingBalance || 0).toFixed(2)}</td>
        `;
        row.addEventListener('click', function () {
            selectCustomerRow(this, customer);
        });
        tbody.appendChild(row);
    });
}

function showCustomerEmptyState() {
    document.getElementById('customerTableBody').innerHTML = '';
    document.getElementById('customerEmptyState').style.display = 'block';
}

function formatPartyType(type) {
    if (type === 'supplier') return 'Supplier';
    if (type === 'customer') return 'Customer';
    if (type === 'both')     return 'Both';
    return type;
}

function selectCustomerRow(row, customer) {
    document.querySelectorAll('#customerTableBody tr').forEach(r => r.classList.remove('selected'));
    row.classList.add('selected');
    selectedCustomer = customer;
    document.getElementById('selectCustomerBtn').disabled = false;
}

function selectCustomer() {
    if (!selectedCustomer) return;

    document.getElementById('hftxtCustomerId').value    = selectedCustomer.partyId;
    document.getElementById('customerAccount').value   = selectedCustomer.partyName;

    let details = '';
    if (selectedCustomer.contactPerson) details += '\nContact: ' + selectedCustomer.contactPerson;
    if (selectedCustomer.phone)         details += '\nPhone: '   + selectedCustomer.phone;
    if (selectedCustomer.email)         details += '\nEmail: '   + selectedCustomer.email;
    if (selectedCustomer.address)       details += '\nAddress: ' + selectedCustomer.address;

    document.getElementById('customerDetails').value = details.trimStart();
    closeCustomerModal();
}

function searchCustomers() {
    const q = document.getElementById('customerSearchInput').value.toLowerCase();
    if (!q) { displayCustomers(allCustomers); return; }

    const filtered = allCustomers.filter(c =>
        c.partyName.toLowerCase().includes(q) ||
        (c.phone         && c.phone.includes(q)) ||
        (c.email         && c.email.toLowerCase().includes(q)) ||
        (c.contactPerson && c.contactPerson.toLowerCase().includes(q))
    );
    displayCustomers(filtered);
}

document.getElementById('customerModal').addEventListener('click', function (e) {
    if (e.target === this) closeCustomerModal();
});

// ── Item modal ──────────────────────────────────────────────────────────────
function openItemLookup(button) {
    currentRow = button.closest('tr');
    document.getElementById('itemModal').classList.add('show');
    loadItems();
}

function closeItemModal() {
    document.getElementById('itemModal').classList.remove('show');
    selectedItem = null;
    currentRow   = null;
    document.getElementById('selectItemBtn').disabled = true;
    document.querySelectorAll('#itemTableBody tr').forEach(r => r.classList.remove('selected'));
}

function loadItems() {
    const tbody = document.getElementById('itemTableBody');
    tbody.innerHTML = '<tr><td colspan="7" style="text-align: center; padding: 2rem;">Loading items...</td></tr>';

    fetch('/Item/GetItems')
        .then(response => response.json())
        .then(data => {
            if (data.success && data.items && data.items.length > 0) {
                allItems = data.items;
                displayItems(allItems);
                loadCategories(allItems);
            } else {
                showItemEmptyState();
            }
        })
        .catch(error => {
            console.error('Error loading items:', error);
            tbody.innerHTML = '<tr><td colspan="7" style="text-align: center; padding: 2rem; color: #dc3545;">Error loading items. Please try again.</td></tr>';
        });
}

function loadCategories(items) {
    const categoryFilter = document.getElementById('categoryFilter');
    const categories = [...new Set(items.map(i => i.categoryName).filter(Boolean))].sort();

    categoryFilter.innerHTML = '<option value="">All Categories</option>';
    categories.forEach(cat => {
        const option = document.createElement('option');
        option.value = option.textContent = cat;
        categoryFilter.appendChild(option);
    });
}

function displayItems(items) {
    const tbody      = document.getElementById('itemTableBody');
    const emptyState = document.getElementById('itemEmptyState');

    if (items.length === 0) { showItemEmptyState(); return; }

    emptyState.style.display = 'none';
    tbody.innerHTML = '';

    items.forEach(item => {
        const stock = item.currentStock || 0;
        let stockBadge;
        if (stock > 50)     stockBadge = `<span class="stock-badge stock-badge-high">${stock}</span>`;
        else if (stock > 0) stockBadge = `<span class="stock-badge stock-badge-low">${stock}</span>`;
        else                stockBadge = `<span class="stock-badge stock-badge-out">${stock}</span>`;

        const row = document.createElement('tr');
        row.innerHTML = `
            <td>${item.itemID}</td>
            <td><img src="${item.imageUrl || NO_IMAGE_PLACEHOLDER}" class="item-image-thumb" alt="Item"></td>
            <td>${escapeHtml(item.itemName)}</td>
            <td>${escapeHtml(item.categoryName)}</td>
            <td>${escapeHtml(item.barcode || '-')}</td>
            <td>${parseFloat(item.salePrice).toFixed(2)}</td>
            <td>${stockBadge}</td>
        `;
        row.addEventListener('click', function () {
            selectItemRow(this, item);
        });
        tbody.appendChild(row);
    });
}

function showItemEmptyState() {
    document.getElementById('itemTableBody').innerHTML = '';
    document.getElementById('itemEmptyState').style.display = 'block';
}

function selectItemRow(row, item) {
    document.querySelectorAll('#itemTableBody tr').forEach(r => r.classList.remove('selected'));
    row.classList.add('selected');
    selectedItem = item;
    document.getElementById('selectItemBtn').disabled = false;
}

async function selectItem() {
    if (!selectedItem || !currentRow) return;

    // ── Prevent duplicate item rows ───────────────────────────────────────
    const allRows = document.querySelectorAll('#itemsBody tr');
    for (const row of allRows) {
        if (row !== currentRow) {
            const existingId = row.cells[1].querySelector('input').value;
            if (existingId && existingId == selectedItem.itemID) {
                await alertDialog(`⚠️ "${escapeHtml(selectedItem.itemName)}" is already added in row ${row.cells[0].textContent}.\nPlease update the quantity in that row instead.`);
                closeItemModal();
                return;
            }
        }
    }

    // ── Populate the row ──────────────────────────────────────────────────
    const avail = selectedItem.currentStock ?? 0;
    const qtyInput = currentRow.cells[4].querySelector('input');

    currentRow.cells[1].querySelector('input').value = selectedItem.itemID;
    currentRow.cells[2].querySelector('input').value = selectedItem.itemName;
    currentRow.cells[3].querySelector('input').value = avail;
    currentRow.cells[5].querySelector('input').value = parseFloat(selectedItem.salePrice).toFixed(2);

    // Set hard limit on the qty input
    qtyInput.max = avail;
    if (Number(qtyInput.value) === 0 && avail >= 1) qtyInput.value = 1;
    qtyInput.title = `Max available: ${avail}`;

    calculateRowTotal(currentRow);
    closeItemModal();
    document.getElementById("scanInput")?.focus();
}

function filterItems() {
    const searchText     = document.getElementById('itemSearchInput').value.toLowerCase();
    const categoryFilter = document.getElementById('categoryFilter').value;
    const stockFilter    = document.getElementById('stockFilter').value;

    let filtered = allItems;

    if (searchText) {
        filtered = filtered.filter(item =>
            item.itemName.toLowerCase().includes(searchText) ||
            (item.barcode      && item.barcode.toLowerCase().includes(searchText)) ||
            (item.categoryName && item.categoryName.toLowerCase().includes(searchText))
        );
    }

    if (categoryFilter) {
        filtered = filtered.filter(item => item.categoryName === categoryFilter);
    }

    if (stockFilter) {
        filtered = filtered.filter(item => {
            const s = item.currentStock || 0;
            if (stockFilter === 'instock')    return s > 50;
            if (stockFilter === 'lowstock')   return s > 0 && s <= 50;
            if (stockFilter === 'outofstock') return s === 0;
            return true;
        });
    }

    displayItems(filtered);
}

document.getElementById('itemModal').addEventListener('click', function (e) {
    if (e.target === this) closeItemModal();
});

document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;
    if (document.getElementById('customerModal').classList.contains('show')) closeCustomerModal();
    if (document.getElementById('itemModal').classList.contains('show'))     closeItemModal();
});
document.getElementById('scanInput')?.addEventListener('keydown', async e => {
    if (e.key !== 'Enter') return; e.preventDefault();
    const code = e.target.value.trim(); if (!code) return;
    const result = await fetch('/Item/GetItems?search=' + encodeURIComponent(code));
    if (!result.ok) return;
    const data = await result.json(); const item = data.items?.find(i => i.barcode === code);
    if (!item) { await alertDialog('Barcode not found.', {type:'warning'}); return; }
    if (item.currentStock < 1) { await alertDialog('Item is out of stock.', {type:'warning'}); return; }
    const existing = [...document.querySelectorAll('#itemsBody tr')].find(r => r.cells[1].querySelector('input').value == item.itemID);
    if (existing) {
        const qty = existing.cells[4].querySelector('input');
        if (Number(qty.value) + 1 > item.currentStock) { await alertDialog('Insufficient stock.', {type:'warning'}); return; }
        qty.value = Number(qty.value) + 1; calculateRowTotal(existing);
    } else {
        let row = [...document.querySelectorAll('#itemsBody tr')].find(r => !r.cells[1].querySelector('input').value);
        if (!row) { addNewRow(); row = document.querySelector('#itemsBody tr:last-child'); }
        currentRow = row; selectedItem = item; await selectItem();
    }
    e.target.value = ''; e.target.dispatchEvent(new Event('input', {bubbles:true})); e.target.focus();
});
document.addEventListener('keydown', e => {
    if (e.ctrlKey && e.key === 'Enter') { e.preventDefault(); document.getElementById('saleForm').requestSubmit(); }
    if (e.altKey && e.key.toLowerCase() === 'a') { e.preventDefault(); addNewRow(); }
});

function updateCashChange() {
    const cash = document.getElementById('cashTender'); if (!cash) return;
    cash.hidden = document.getElementById('paymentMode').value !== '1';
    const received = Number(document.getElementById('cashReceived').value || 0);
    const total = Number(document.getElementById('netAmount').textContent || 0);
    document.getElementById('cashChange').textContent = Math.max(0, received - total).toFixed(2);
}
document.getElementById('paymentMode').addEventListener('change', updateCashChange);
document.getElementById('saleForm').addEventListener('input', updateCashChange);
