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

let rowCounter    = 0;
let selectedCustomer = null;
let allCustomers  = [];
let selectedItem  = null;
let allItems      = [];
let currentRow    = null;

// ── Init ────────────────────────────────────────────────────────────────────
window.addEventListener('DOMContentLoaded', function () {
    addNewRow();
    loadNextInvoiceNumber();
});

// ── Row management ──────────────────────────────────────────────────────────
document.getElementById('addRowBtn').addEventListener('click', addNewRow);

function addNewRow() {
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

function removeRow(row) {
    if (document.getElementById('itemsBody').rows.length > 1) {
        if (confirm('Remove this item?')) {
            row.remove();
            updateRowNumbers();
            calculateTotals();
        }
    } else {
        alert('At least one row is required!');
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
}

// ── New invoice ─────────────────────────────────────────────────────────────
document.getElementById('newBtn').addEventListener('click', function () {
    if (confirm('Create new invoice? Unsaved changes will be lost.')) {
        resetForm();
    }
});

// ── Form submit ─────────────────────────────────────────────────────────────
document.getElementById('saleForm').addEventListener('submit', function (e) {
    e.preventDefault();

    const customerId = document.getElementById('hftxtCustomerId').value;
    if (!customerId) {
        alert('Please select a customer!');
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
        alert('Please add at least one item with quantity!');
        return;
    }

    if (stockErrors.length > 0) {
        alert('⚠️ Insufficient stock:\n\n' + stockErrors.join('\n'));
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
        SaleDate:    document.getElementById('invoiceDate').value,
        CustomerID:  customerId,
        BranchID:    1,
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

    fetch('/Sale/Save', {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify(payload)
    })
        .then(res => res.json())
        .then(data => {
            if (data.success) {
                alert('✅ ' + data.message);
                resetForm();
            } else {
                alert('❌ ' + (data.message || 'Unknown error'));
            }
        })
        .catch(err => {
            console.error(err);
            alert('Error saving sale invoice.');
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
            <td>${customer.partyName}</td>
            <td><span class="party-badge party-badge-${customer.partyType}">${formatPartyType(customer.partyType)}</span></td>
            <td>${customer.contactPerson || '-'}</td>
            <td>${customer.phone || '-'}</td>
            <td>${customer.email || '-'}</td>
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
            <td><img src="${item.imageUrl || 'https://via.placeholder.com/40'}" class="item-image-thumb" alt="Item"></td>
            <td>${item.itemName}</td>
            <td>${item.categoryName}</td>
            <td>${item.barcode || '-'}</td>
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

function selectItem() {
    if (!selectedItem || !currentRow) return;

    // ── Prevent duplicate item rows ───────────────────────────────────────
    const allRows = document.querySelectorAll('#itemsBody tr');
    for (const row of allRows) {
        if (row !== currentRow) {
            const existingId = row.cells[1].querySelector('input').value;
            if (existingId && existingId == selectedItem.itemID) {
                alert(`⚠️ "${selectedItem.itemName}" is already added in row ${row.cells[0].textContent}.\nPlease update the quantity in that row instead.`);
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
    qtyInput.title = `Max available: ${avail}`;

    calculateRowTotal(currentRow);
    closeItemModal();
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